namespace OpenCsms.Suite.Support;

using Microsoft.Extensions.Configuration;
using OpenCsms.Application.Ports;
using OpenCsms.Infrastructure.Messaging;

/// <summary>
/// The fault-injecting publisher a test substitutes for <see cref="IEventPublisher"/>: its first
/// publish attempt fails and every later attempt delegates to the product's own RabbitMQ publisher,
/// so the retry travels the real broker path. The test keeps the instance, so what it saw is the
/// test's evidence: an event published directly by the failing flow is lost, an event that survives
/// it was delivered by an attempt after the failure.
/// </summary>
public sealed class FirstAttemptFailsEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqEventPublisher _publisher;
    private int _attempts;
    private int _failures;

    public FirstAttemptFailsEventPublisher(string brokerConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerConnectionString);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:ConnectionString"] = brokerConnectionString
            })
            .Build();
        _publisher = new RabbitMqEventPublisher(configuration);
    }

    /// <summary>How many publish attempts reached this publisher.</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>How many of them failed; the injected fault fires once.</summary>
    public int Failures => Volatile.Read(ref _failures);

    public async ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _attempts) == 1)
        {
            Interlocked.Increment(ref _failures);
            throw new InvalidOperationException(
                $"The first publish attempt for '{routingKey}' failed (injected for this test).");
        }

        await _publisher.PublishAsync(routingKey, message, cancellationToken);
    }

    public ValueTask DisposeAsync() => _publisher.DisposeAsync();
}
