namespace OpenCsms.Infrastructure.Notifications;

using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Application.Ports;

/// <summary>The notification transport registration: the outbound port and the one HTTP client it posts with.</summary>
internal static class NotificationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="INotificationSender"/> adapter. The addresses stay in configuration
    /// until a notification is sent, so a host that reads them after its registration - the suite's
    /// worker host, or a deployment whose environment provides the keys - sends to the right target.
    /// </summary>
    internal static IServiceCollection AddCsmsNotifications(this IServiceCollection services)
    {
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        services.AddSingleton<INotificationSender, HttpNotificationSender>();
        return services;
    }
}
