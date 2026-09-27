namespace OpenCsms.Infrastructure.Messaging;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

/// <summary>
/// The delivery loop every queue consumer in the product shares: connect, declare the topology, then
/// hand each delivery to <see cref="HandleAsync"/>. A failed delivery is retried in-process -
/// republished with a retries header, then acknowledged - until <see cref="MaxRetries"/> completed
/// retries are spent; the next failure is dead-lettered through the queue's
/// <c>x-dead-letter-exchange</c> and never retried again, so a consumer never spins on a poisonous
/// message. The header counts completed retries, not attempts: absent on the first delivery, 1 on the
/// second, and <see cref="MaxRetries"/> on the dead-lettered attempt. What the message means and
/// which failures are retryable stay the subclass's business.
/// </summary>
public abstract class RetryingQueueConsumer<TMessage> : BackgroundService
    where TMessage : class
{
    /// <summary>
    /// Counts completed retries, not attempts: absent on the first delivery, 1 on the second, and
    /// <see cref="MaxRetries"/> on the dead-lettered attempt.
    /// </summary>
    private const string RetriesHeader = "x-opencsms-retries";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IConnection? _connection;
    private IChannel? _channel;

    protected RetryingQueueConsumer(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger logger)
    {
        ScopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The queue the consumer reads; its dead-letter path is the queue's declaration.</summary>
    protected abstract string QueueName { get; }

    /// <summary>Completed retries before the delivery is given up on; the default is three.</summary>
    protected virtual int MaxRetries => 3;

    /// <summary>
    /// Why the consumer stays idle instead of connecting - for example an unconfigured target. The
    /// reason is logged once at start; <see langword="null"/> runs the consumer.
    /// </summary>
    protected virtual string? DisabledReason => null;

    /// <summary>Creates scopes for the services a delivery needs.</summary>
    protected IServiceScopeFactory ScopeFactory { get; }

    /// <summary>The host's configuration; the broker address and target addresses come from here.</summary>
    protected IConfiguration Configuration { get; }

    /// <summary>The consumer's logger.</summary>
    protected ILogger Logger { get; }

    /// <summary>Handles one delivery; an exception starts the retry path.</summary>
    protected abstract ValueTask HandleAsync(TMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Reports a delivery that spent its retries, just before it is dead-lettered. An exception here
    /// is logged and the dead-lettering continues: the queue's copy is the source of truth.
    /// </summary>
    protected virtual ValueTask OnTerminalFailureAsync(
        TMessage? message,
        Exception exception,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (DisabledReason is { } reason)
        {
            Logger.LogWarning("{Consumer} is idle: {Reason}", GetType().Name, reason);
            return;
        }

        var connectionString = Configuration["Messaging:RabbitMq:ConnectionString"]
            ?? Configuration["ConnectionStrings:RabbitMq"]
            ?? throw new InvalidOperationException(
                "No broker is configured. Set 'Messaging:RabbitMq:ConnectionString' (the suite's infrastructure does this).");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.EnsureAsync(_channel, stoppingToken);
        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, args) => HandleDeliveryAsync(args, stoppingToken);
        await _channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
        Logger.LogInformation("{Consumer} consuming '{Queue}'.", GetType().Name, QueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleDeliveryAsync(BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        if (_channel is null)
        {
            return;
        }

        var retries = ReadRetries(args.BasicProperties);
        TMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(args.Body.Span, JsonOptions)
                ?? throw new InvalidOperationException($"The '{QueueName}' payload is empty.");
            await HandleAsync(message, stoppingToken);
            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacknowledged so the next run picks it up.
            throw;
        }
        catch (Exception exception)
        {
            if (retries >= MaxRetries)
            {
                Logger.LogError(
                    exception,
                    "{Consumer} failed a '{Queue}' message after {Attempts} attempt(s); dead-lettering.",
                    GetType().Name,
                    QueueName,
                    retries + 1);
                if (message is not null)
                {
                    try
                    {
                        await OnTerminalFailureAsync(message, exception, stoppingToken);
                    }
                    catch (Exception reportingFailure) when (reportingFailure is not OperationCanceledException)
                    {
                        Logger.LogError(
                            reportingFailure,
                            "{Consumer} could not report the failed '{Queue}' message before dead-lettering it.",
                            GetType().Name,
                            QueueName);
                    }
                }

                await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }

            Logger.LogWarning(
                exception,
                "{Consumer} failed a '{Queue}' message (attempt {Attempt}/{Max}); retrying.",
                GetType().Name,
                QueueName,
                retries + 1,
                MaxRetries + 1);
            await RepublishAsync(args, retries + 1, stoppingToken);
            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
            await Task.Delay(TimeSpan.FromMilliseconds(200 * (retries + 1)), stoppingToken);
        }
    }

    private async Task RepublishAsync(BasicDeliverEventArgs args, int retries, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = args.BasicProperties.ContentType ?? "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?> { [RetriesHeader] = retries }
        };
        // The default exchange routes by queue name, so the retry lands where the original did.
        await _channel!.BasicPublishAsync(
            string.Empty,
            QueueName,
            mandatory: false,
            basicProperties: properties,
            body: args.Body,
            cancellationToken: cancellationToken);
    }

    private static int ReadRetries(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(RetriesHeader, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            int number => number,
            long number => (int)number,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
