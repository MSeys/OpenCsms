namespace OpenCsms.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the event publisher. The broker address every environment provides - the suite's
    /// RabbitMQ container (through infrastructure settings) or a configured broker - is read on first
    /// use, after the run's settings are visible.
    /// </summary>
    public static IServiceCollection AddRabbitMqEventPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<IEventPublisher>(provider =>
            new RabbitMqEventPublisher(provider.GetRequiredService<IConfiguration>()));
    }
}
