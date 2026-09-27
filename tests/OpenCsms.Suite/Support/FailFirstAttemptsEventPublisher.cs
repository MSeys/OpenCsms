namespace OpenCsms.Suite.Support;

using Microsoft.Extensions.Configuration;
using OpenCsms.Application.Ports;
using OpenCsms.Infrastructure.Messaging;

/// <summary>
/// The fault-injecting publisher a test substitutes for <see cref="IEventPublisher"/>: its first
/// <paramref name="failures"/> matching publish attempts fail and every later attempt delegates to
/// the product's own RabbitMQ publisher, so a retry travels the real broker path. <paramref name="matches"/>
/// narrows the fault to the test's own event - other pending rows a dispatcher sweeps must not spend
/// the failure budget - while <see langword="null"/> fails every attempt. The test keeps the
/// instance, so what it saw is the test's evidence: an event published directly by the failing flow
/// is lost, an event that survives it was delivered by an attempt after the failures.
/// </summary>
public sealed class FailFirstAttemptsEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqEventPublisher _publisher;
    private readonly int _failures;
    private readonly Func<string, object?, bool>? _matches;
    private int _attempts;
    private int _observedFailures;

    public FailFirstAttemptsEventPublisher(
        string brokerConnectionString,
        int failures,
        Func<string, object?, bool>? matches = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerConnectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(failures, 1);
        _failures = failures;
        _matches = matches;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:ConnectionString"] = brokerConnectionString
            })
            .Build();
        _publisher = new RabbitMqEventPublisher(configuration);
    }

    /// <summary>How many matching publish attempts reached this publisher.</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>How many of them failed; the injected fault fires exactly this many times.</summary>
    public int Failures => Volatile.Read(ref _observedFailures);

    public async ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default)
    {
        if (_matches is not null && !_matches(routingKey, message))
        {
            // Not this test's event; another test's pending row rides the real broker path.
            await _publisher.PublishAsync(routingKey, message, cancellationToken);
            return;
        }

        if (Interlocked.Increment(ref _attempts) <= _failures)
        {
            Interlocked.Increment(ref _observedFailures);
            throw new InvalidOperationException(
                $"Publish attempt {Attempts} for '{routingKey}' failed (injected for this test).");
        }

        await _publisher.PublishAsync(routingKey, message, cancellationToken);
    }

    public ValueTask DisposeAsync() => _publisher.DisposeAsync();
}
