namespace OpenCsms.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Infrastructure.Messaging;
using OpenCsms.Infrastructure.Persistence;

/// <summary>
/// The infrastructure's one entry point. A composition root calls <see cref="AddCsmsInfrastructure"/>
/// and gets the store with the application's persistence ports and the RabbitMQ event publisher;
/// <see cref="MigrateCsmsData"/> is the one migration step the API runs at boot.
/// </summary>
public static class CsmsInfrastructureExtensions
{
    /// <summary>The configuration key every environment provides: the suite's containers, or a real database.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:Csms";

    /// <summary>
    /// Registers the CSMS persistence and the event publisher; the API, the worker and the suite's
    /// verification reads call it, so one key reaches each whether the suite provided it or the
    /// environment did. The connection string and the broker address are read when their service is
    /// first resolved, not when it is registered: an in-process worker receives the run's settings
    /// when its host is built, which is after its entry point ran.
    /// </summary>
    public static IServiceCollection AddCsmsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCsmsPersistence();
        services.AddRabbitMqEventPublisher();
        // The readiness probe is the one store read the API asks without a use case.
        services.AddScoped<CsmsStoreProbe>();
        return services;
    }

    /// <summary>
    /// Applies the store's migrations. The API calls it at boot, so the one migration step is defined
    /// once; PostgreSQL is already up in every mode, and the migration lock makes the race between the
    /// hosts harmless.
    /// </summary>
    public static void MigrateCsmsData(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CsmsDbContext>().Database.Migrate();
    }
}
