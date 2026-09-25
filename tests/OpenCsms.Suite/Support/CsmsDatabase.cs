namespace OpenCsms.Suite.Support;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Data;
using ProtoTest.Core;

/// <summary>
/// Reads the store for the invariants the REST surface cannot express - the suite's counterpart to the
/// raw broker client. It composes the product's own registration against the run's connection string,
/// so the query sees exactly the schema the API and the worker use.
/// </summary>
public static class CsmsDatabase
{
    /// <summary>Counts the invoice rows for a session; the store allows exactly zero or one.</summary>
    public static async Task<int> CountInvoicesAsync(
        ProtoExecutionContext context,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CsmsDataExtensions.ConnectionStringKey] = ResolveConnectionString(context)
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCsmsData();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsmsDbContext>();
        return await db.Invoices.CountAsync(invoice => invoice.SessionId == sessionId, cancellationToken);
    }

    private static string ResolveConnectionString(ProtoExecutionContext context)
    {
        var configured = context.Configuration[CsmsDataExtensions.ConnectionStringKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (context.TryService<ProtoInfrastructureSettings>() is { } settings
            && settings.Values.TryGetValue(CsmsDataExtensions.ConnectionStringKey, out var provided))
        {
            return provided;
        }

        throw new InvalidOperationException(
            $"No database address is available. Set '{CsmsDataExtensions.ConnectionStringKey}' " +
            "(a started PostgreSQL container does this).");
    }
}
