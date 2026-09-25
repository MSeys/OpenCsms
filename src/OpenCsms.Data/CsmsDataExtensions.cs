namespace OpenCsms.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class CsmsDataExtensions
{
    /// <summary>The configuration key every environment provides: the suite's containers, or a real database.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:Csms";

    /// <summary>
    /// Registers the CSMS store. The API and the worker both call this with their own configuration, so
    /// the same key reaches both whether the suite provided it or the environment did.
    /// </summary>
    public static IServiceCollection AddCsmsData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"'{ConnectionStringKey}' is not configured. Start PostgreSQL through the suite, or set the key.");
        }

        services.AddDbContext<CsmsDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
