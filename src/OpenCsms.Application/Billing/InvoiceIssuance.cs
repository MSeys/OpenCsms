namespace OpenCsms.Application.Billing;

using Microsoft.Extensions.Logging;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Domain;

/// <summary>
/// Issues the session's invoice exactly once per delivery of <c>session.ended</c>, then publishes
/// <c>invoice.issued</c>. The bill uses the tariff terms the session started under, so a repricing
/// while the session was open cannot change it.
/// </summary>
public sealed class InvoiceIssuance(
    IInvoiceQueries invoiceQueries,
    IInvoiceCommands invoiceCommands,
    ISessionQueries sessions,
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

            invoice = InvoiceCalculator.Calculate(session, clock.GetUtcNow());
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
