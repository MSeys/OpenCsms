namespace OpenCsms.Application.Billing;

using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Domain;

/// <summary>
/// The billing use case behind a delivered <c>session.ended</c>: issue the session's invoice exactly
/// once, then publish <c>invoice.issued</c>. The unique session index in the store makes the issue
/// idempotent; publishing is mandatory on every delivery, including a redelivery, and a failed
/// publish surfaces to the caller so its retry path sees it. The invoice is stamped with the
/// application clock, not the machine's. Delivery, retries and dead-lettering are the transport
/// host's business, not this use case's.
/// </summary>
public sealed class InvoiceIssuance(
    IInvoiceQueries invoiceQueries,
    IInvoiceCommands invoiceCommands,
    ISessionQueries sessions,
    IStationQueries stations,
    ITariffQueries tariffs,
    IEventPublisher publisher,
    TimeProvider clock,
    ILogger<InvoiceIssuance> logger)
{
    /// <summary>Bills the session the message names (or republishes its stored invoice) and publishes.</summary>
    public async Task IssueAsync(SessionEnded message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var invoice = await invoiceQueries.FindBySessionAsync(message.SessionId, cancellationToken);
        if (invoice is null)
        {
            var session = await sessions.FindAsync(message.SessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Session '{message.SessionId}' is not in the store.");
            var station = await stations.FindAsync(session.StationId, cancellationToken)
                ?? throw new InvalidOperationException($"Station '{session.StationId}' is not in the store.");
            var tariff = await tariffs.FindAsync(station.TariffId, cancellationToken)
                ?? throw new InvalidOperationException($"Tariff '{station.TariffId}' is not in the store.");

            invoice = InvoiceCalculator.Calculate(session, tariff, clock.GetUtcNow());
            await invoiceCommands.AddAsync(invoice, cancellationToken);
            logger.LogInformation("Billed session '{SessionId}' as invoice '{InvoiceId}'.", session.Id, invoice.Id);
        }
        else
        {
            logger.LogInformation(
                "Session '{SessionId}' already has invoice '{InvoiceId}'; republishing.",
                message.SessionId,
                invoice.Id);
        }

        // Always publish: the event is the contract, and an exception here must reach the caller's
        // retry path rather than being swallowed, so a redelivery republishes the same invoice.
        await publisher.PublishAsync(
            CsmsEvents.InvoiceIssuedRoutingKey,
            new InvoiceIssued(
                invoice.Id,
                invoice.SessionId,
                invoice.TenantId,
                invoice.Total,
                invoice.Currency,
                invoice.IssuedAtUtc),
            cancellationToken);
    }
}
