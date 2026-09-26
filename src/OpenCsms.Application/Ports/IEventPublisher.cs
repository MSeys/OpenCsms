namespace OpenCsms.Application.Ports;

/// <summary>
/// The application's outbound event port: a use case publishes an integration event, and the
/// messaging infrastructure decides how it travels. The composition roots register the runtime
/// implementation; the application never sees a broker.
/// </summary>
public interface IEventPublisher
{
    ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default);
}
