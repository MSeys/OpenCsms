namespace OpenCsms.Infrastructure.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Application.Ports;

/// <summary>The messaging half of the infrastructure registration.</summary>
internal static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the event publisher. The broker address every environment provides - the suite's
    /// RabbitMQ container (through infrastructure settings) or a configured broker - is read on first
    /// use, after the run's settings are visible.
    /// </summary>
    internal static IServiceCollection AddRabbitMqEventPublisher(this IServiceCollection services)
    {
        return services.AddSingleton<IEventPublisher>(provider =>
            new RabbitMqEventPublisher(provider.GetRequiredService<IConfiguration>()));
    }
}
