namespace OpenCsms.Suite.Support;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Infrastructure;
using OpenCsms.Infrastructure.Persistence;
using ProtoTest.Core;

/// <summary>
/// Reads the store for the invariants the REST surface cannot express - the suite's counterpart to the
/// raw broker client. It composes the product's own registration against the run's connection string,
/// so the query sees exactly the schema the API and the worker use.
/// </summary>
public static class CsmsDatabase
{
    /// <summary>Counts the invoice rows for a session; the store allows exactly zero or one.</summary>
    public static Task<int> CountInvoicesAsync(
        ProtoExecutionContext context,
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => QueryAsync(
            context,
            db => db.Invoices.CountAsync(invoice => invoice.SessionId == sessionId, cancellationToken));

    /// <summary>
    /// Counts the session's outbox rows that are still pending, so a test can watch the dispatcher
    /// carry an event from stored to sent. The payload is the only place the session id appears.
    /// </summary>
    public static Task<int> CountPendingOutboxAsync(
        ProtoExecutionContext context,
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => QueryAsync(
            context,
            db => db.OutboxMessages.CountAsync(
                message => message.SentAtUtc == null && message.PayloadJson.Contains(sessionId.ToString()),
                cancellationToken));

    private static async Task<int> QueryAsync(
        ProtoExecutionContext context,
        Func<CsmsDbContext, Task<int>> query)
    {
        ArgumentNullException.ThrowIfNull(context);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CsmsInfrastructureExtensions.ConnectionStringKey] = ResolveConnectionString(context)
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCsmsInfrastructure();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CsmsDbContext>());
    }

    private static string ResolveConnectionString(ProtoExecutionContext context)
    {
        var configured = context.Configuration[CsmsInfrastructureExtensions.ConnectionStringKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (context.TryService<ProtoInfrastructureSettings>() is { } settings
            && settings.Values.TryGetValue(CsmsInfrastructureExtensions.ConnectionStringKey, out var provided))
        {
            return provided;
        }

        throw new InvalidOperationException(
            $"No database address is available. Set '{CsmsInfrastructureExtensions.ConnectionStringKey}' " +
            "(a started PostgreSQL container does this).");
    }
}
