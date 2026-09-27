namespace OpenCsms.Infrastructure.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;

/// <summary>
/// The background half of the outbox: every interval it retries the pending rows that became due, so
/// an event whose immediate publish attempt failed reaches the broker once it is reachable again.
/// Every host that composes the infrastructure runs one; the row reservation keeps the hosts from
/// publishing the same attempt twice, and the row stays pending until an attempt succeeds.
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    /// <summary>The gap between sweeps; a recovered broker is used again within one interval.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOutbox>().DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The outbox sweep failed; the pending rows are retried next interval.");
            }
        }
    }
}
