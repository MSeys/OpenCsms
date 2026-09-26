namespace OpenCsms.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Application.Ports;
using OpenCsms.Infrastructure.Persistence.Stores;

/// <summary>The persistence half of the infrastructure registration.</summary>
internal static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CSMS store and the application's persistence ports it implements. The connection
    /// string is read when the context is first resolved, not when it is registered, so a host that
    /// receives the run's settings after its entry point ran still composes.
    /// </summary>
    internal static IServiceCollection AddCsmsPersistence(this IServiceCollection services)
    {
        services.AddDbContext<CsmsDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>()[CsmsInfrastructureExtensions.ConnectionStringKey];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"'{CsmsInfrastructureExtensions.ConnectionStringKey}' is not configured. Start PostgreSQL through the suite, or set the key.");
            }

            options.UseNpgsql(connectionString);
        });

        // One store instance per scope serves both its query and its command port, so a use case that
        // reads and writes sees the same tracked entities and the same one SaveChanges.
        services.AddScoped<TariffStore>();
        services.AddScoped<ITariffQueries>(provider => provider.GetRequiredService<TariffStore>());
        services.AddScoped<ITariffCommands>(provider => provider.GetRequiredService<TariffStore>());

        services.AddScoped<StationStore>();
        services.AddScoped<IStationQueries>(provider => provider.GetRequiredService<StationStore>());
        services.AddScoped<IStationCommands>(provider => provider.GetRequiredService<StationStore>());

        services.AddScoped<SessionStore>();
        services.AddScoped<ISessionQueries>(provider => provider.GetRequiredService<SessionStore>());
        services.AddScoped<ISessionCommands>(provider => provider.GetRequiredService<SessionStore>());

        services.AddScoped<ConnectorStore>();
        services.AddScoped<IConnectorQueries>(provider => provider.GetRequiredService<ConnectorStore>());
        services.AddScoped<IConnectorCommands>(provider => provider.GetRequiredService<ConnectorStore>());

        services.AddScoped<InvoiceStore>();
        services.AddScoped<IInvoiceQueries>(provider => provider.GetRequiredService<InvoiceStore>());
        services.AddScoped<IInvoiceCommands>(provider => provider.GetRequiredService<InvoiceStore>());

        services.AddScoped<UserStore>();
        services.AddScoped<IUserQueries>(provider => provider.GetRequiredService<UserStore>());
        services.AddScoped<IUserCommands>(provider => provider.GetRequiredService<UserStore>());
        return services;
    }
}
