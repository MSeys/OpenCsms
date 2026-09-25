namespace OpenCsms.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class CsmsDataExtensions
{
    /// <summary>The configuration key every environment provides: the suite's containers, or a real database.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:Csms";

    /// <summary>
    /// Registers the CSMS store; the API and the worker both call it, so one key reaches both whether
    /// the suite provided it or the environment did.
    /// </summary>
    public static IServiceCollection AddCsmsData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The string is read when the context is first resolved, not when it is registered: an
        // in-process worker receives the run's settings when its host is built, which is after its
        // entry point ran. Reading here keeps the worker composable the way a real application is.
        services.AddDbContext<CsmsDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>()[ConnectionStringKey];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"'{ConnectionStringKey}' is not configured. Start PostgreSQL through the suite, or set the key.");
            }

            options.UseNpgsql(connectionString);
        });
        return services;
    }

    /// <summary>
    /// Applies the store's migrations. The API and the worker both call it at boot, so the one migration
    /// step is defined once; PostgreSQL is already up in every mode, and the migration lock makes the
    /// race between the two hosts harmless.
    /// </summary>
    public static void MigrateCsmsData(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CsmsDbContext>().Database.Migrate();
    }
}
