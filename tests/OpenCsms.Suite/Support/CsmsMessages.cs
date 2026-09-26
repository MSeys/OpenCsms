namespace OpenCsms.Suite.Support;

using System.Text.Json;
using OpenCsms.Contracts;
using ProtoTest.Messaging;

/// <summary>
/// The predicates the suite's messaging tap matches with. The tap sees every routing key on the product
/// exchange, so a match names the event's own ids instead of trusting the routing key.
/// </summary>
public static class CsmsMessages
{
    /// <summary>Whether the message is an <c>invoice.issued</c> event for the session.</summary>
    public static bool IsInvoiceIssuedFor(ProtoMessage message, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Payload is null || !message.Payload.Contains(sessionId.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var issued = message.ReadAsJson<InvoiceIssued>();
            return issued is { } invoice && invoice.SessionId == sessionId && invoice.InvoiceId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
