namespace OpenCsms.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the event publisher against the broker address every environment provides: the suite's
    /// RabbitMQ container (through infrastructure settings) or a configured broker.
    /// </summary>
    public static IServiceCollection AddRabbitMqEventPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddSingleton<IEventPublisher>(new RabbitMqEventPublisher(
            configuration["Messaging:RabbitMq:ConnectionString"]
            ?? configuration["ConnectionStrings:RabbitMq"]));
    }
}
