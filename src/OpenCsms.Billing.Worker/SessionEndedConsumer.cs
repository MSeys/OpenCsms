namespace OpenCsms.Billing.Worker;

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCsms.Contracts;
using OpenCsms.Data;
using OpenCsms.Domain;
using OpenCsms.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

/// <summary>
/// Consumes <c>session.ended</c> and stores an invoice. Retries run in-process (republish with an
/// attempts header, then acknowledge), and a message that still fails is dead-lettered through the
/// queue's <c>x-dead-letter-exchange</c> - the worker never spins on a poisonous message. The unique
/// session index makes storing idempotent; publishing <c>invoice.issued</c> is mandatory on every
/// delivery, including a redelivery, and a failed publish fails the handler so the retry path sees it.
/// The invoice is stamped with the run's clock, not the machine's.
/// </summary>
public sealed class SessionEndedConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SessionEndedConsumer> logger) : BackgroundService
{
    private const int MaxAttempts = 3;
    private const string AttemptsHeader = "x-opencsms-attempts";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration["Messaging:RabbitMq:ConnectionString"]
            ?? configuration["ConnectionStrings:RabbitMq"]
            ?? throw new InvalidOperationException(
                "No broker is configured. Set 'Messaging:RabbitMq:ConnectionString' (the suite's infrastructure does this).");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.EnsureAsync(_channel, stoppingToken);
        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, args) => HandleAsync(args, stoppingToken);
        await _channel.BasicConsumeAsync(
            CsmsEvents.BillingQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
        logger.LogInformation("Billing worker consuming '{Queue}'.", CsmsEvents.BillingQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        if (_channel is null)
        {
            return;
        }

        var attempts = ReadAttempts(args.BasicProperties);
        try
        {
            await ProcessAsync(args, stoppingToken);
            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacknowledged so the next run picks it up.
            throw;
        }
        catch (Exception exception)
        {
            if (attempts >= MaxAttempts)
            {
                logger.LogError(
                    exception,
                    "Session ended message failed after {Attempts} attempt(s); dead-lettering.",
                    attempts);
                await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }

            logger.LogWarning(
                exception,
                "Session ended message failed (attempt {Attempt}/{Max}); retrying.",
                attempts + 1,
                MaxAttempts);
            await RepublishAsync(args, attempts + 1, stoppingToken);
            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);
            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempts + 1)), stoppingToken);
        }
    }

    private async Task ProcessAsync(BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<SessionEnded>(args.Body.Span, JsonOptions)
            ?? throw new InvalidOperationException("The session.ended payload is empty.");
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsmsDbContext>();
        var invoice = await db.Invoices.FirstOrDefaultAsync(
            candidate => candidate.SessionId == message.SessionId,
            cancellationToken);
        if (invoice is null)
        {
            var session = await db.Sessions.FirstOrDefaultAsync(candidate => candidate.Id == message.SessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Session '{message.SessionId}' is not in the store.");
            var station = await db.Stations.FirstOrDefaultAsync(candidate => candidate.Id == session.StationId, cancellationToken)
                ?? throw new InvalidOperationException($"Station '{session.StationId}' is not in the store.");
            var tariff = await db.Tariffs.FirstOrDefaultAsync(candidate => candidate.Id == station.TariffId, cancellationToken)
                ?? throw new InvalidOperationException($"Tariff '{station.TariffId}' is not in the store.");

            invoice = InvoiceCalculator.Calculate(session, tariff, timeProvider.GetUtcNow());
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Billed session '{SessionId}' as invoice '{InvoiceId}'.", session.Id, invoice.Id);
        }
        else
        {
            logger.LogInformation(
                "Session '{SessionId}' already has invoice '{InvoiceId}'; republishing.",
                message.SessionId,
                invoice.Id);
        }

        // Always publish: the event is the contract, and an exception here must reach the retry path
        // rather than being swallowed, so a redelivery republishes the same invoice.
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        await publisher.PublishAsync(
            CsmsEvents.InvoiceIssuedRoutingKey,
            new InvoiceIssued(
                invoice.Id,
                invoice.SessionId,
                invoice.TenantId,
                invoice.Total,
                invoice.Currency,
                invoice.IssuedAtUtc),
            cancellationToken);
    }

    private async Task RepublishAsync(BasicDeliverEventArgs args, int attempts, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = args.BasicProperties.ContentType ?? "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?> { [AttemptsHeader] = attempts }
        };
        // The default exchange routes by queue name, so the retry lands where the original did.
        await _channel!.BasicPublishAsync(
            string.Empty,
            CsmsEvents.BillingQueue,
            mandatory: false,
            basicProperties: properties,
            body: args.Body,
            cancellationToken: cancellationToken);
    }

    private static int ReadAttempts(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(AttemptsHeader, out var value) || value is null)
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
