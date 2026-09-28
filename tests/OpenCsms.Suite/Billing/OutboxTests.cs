namespace OpenCsms.Suite.Billing;

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Application.Ports;
using OpenCsms.Application.Sessions;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure.Persistence;
using OpenCsms.Suite.Support;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using CsmsApi = OpenCsms.Api.Program;

/// <summary>
/// The session-end outbox under a publish failure: the end commits the ended session and its
/// <c>session.ended</c> event, the substituted publisher fails the attempt that request makes, and the
/// event is still delivered - the stored row is retried by the dispatcher - while the worker stores
/// exactly one invoice. The substituted publisher delegates every later attempt to the product's own
/// RabbitMQ publisher, so the retry travels the real broker path. The second test rides out a longer
/// outage: two failed attempts, the store's bounded backoff between them, and the send still arrives.
/// </summary>
[Application(CsmsTargets.Api)]
[CsmsOperator]
[Auth<CsmsMachineKeyAuthenticator>]
[RequiresInProcess]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The retried event travels the product exchange; configure the broker.")]
[RequiresWorker<BillingWorker>]
public sealed class OutboxTests
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [ProtoTest]
    public async Task AFailedPublishIsRetriedByTheDispatcherAndBillsOnce()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // A session created through the product's REST front door, then the API's publisher is
        // substituted for this test: the request's own publish attempt will fail.
        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        await RecordMeterAsync(started.Id, 22m);
        var publisher = new FailFirstAttemptsEventPublisher(
            BrokerAddress(Proto.Context),
            failures: 1,
            matches: (_, message) => message?.ToString()?.Contains(started.Id.ToString(), StringComparison.Ordinal) == true);
        Proto.Context.Override<IEventPublisher>(publisher);

        // Act: the end commits the session and its event, then its publish attempt fails.
        SessionResponse ended;
        using (var response = await Proto.Context.Rest().PostAsync($"/api/sessions/{started.Id}/end"))
        {
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            ended = response.ReadAsJson<SessionResponse>()!;
        }

        // Assert: the failure did not roll the end back, and the event is still in the store.
        Assert.Multiple(() =>
        {
            Assert.That(ended.EndedAtUtc, Is.Not.Null, "the end committed even though its publish failed");
            Assert.That(publisher.Failures, Is.EqualTo(1), "the publisher rejected the request's own attempt");
        });
        Assert.That(
            await CsmsDatabase.CountPendingOutboxAsync(Proto.Context, started.Id),
            Is.EqualTo(1),
            "the event is stored and still pending a delivery");

        // Assert: the dispatcher retried the row, the worker billed once, and the row is sent.
        var issued = await AwaitInvoiceIssuedAsync(started.Id);
        var stored = await ReadInvoiceAsync(started.Id);
        var invoiceRows = await CsmsDatabase.CountInvoicesAsync(Proto.Context, started.Id);
        var pending = await AwaitOutboxEmptyAsync(started.Id);

        Assert.Multiple(() =>
        {
            Assert.That(issued.InvoiceId, Is.EqualTo(stored.Id), "the delivered event belongs to the stored invoice");
            Assert.That(invoiceRows, Is.EqualTo(1), "the failed attempt and its retry must bill the session once");
            Assert.That(pending, Is.EqualTo(0), "the dispatcher marked the delivered row as sent");
        });
    }

    [ProtoTest]
    public async Task ABrokerOutageIsRiddenOutByMoreThanOneBackedOffRetry()
    {
        var op = Proto.Context.Resolve<CsmsOperator>();

        // Arrange: a session created through the product's REST front door, and a publisher that
        // fails the first two attempts - the broker-down window this test rides out.
        var started = await StartSessionAsync(op.StationId, connectorId: 1);
        await RecordMeterAsync(started.Id, 22m);

        var publisher = new FailFirstAttemptsEventPublisher(
            BrokerAddress(Proto.Context),
            failures: 2,
            matches: (_, message) => message?.ToString()?.Contains(started.Id.ToString(), StringComparison.Ordinal) == true);
        Proto.Context.Override<IEventPublisher>(publisher);

        // The end and its event are committed in a unit of work the test holds open, so the stored
        // event stays invisible to the run's dispatchers until both failed attempts have happened:
        // the retry schedule under test is the store's own, not a race between the hosts.
        using var scope = Proto.Context.CreateServerScope<CsmsApi>();
        var db = scope.ServiceProvider.GetRequiredService<CsmsDbContext>();
        await using var unit = await db.Database.BeginTransactionAsync();

        var session = await scope.ServiceProvider.GetRequiredService<ISessionQueries>().FindAsync(started.Id)
            ?? throw new InvalidOperationException($"Session '{started.Id}' is not in the store.");
        await scope.ServiceProvider.GetRequiredService<SessionEnding>().EndAsync(session, SuiteClock.Instant);

        Assert.That(publisher.Failures, Is.EqualTo(1), "the request's own attempt failed");

        // Wait out the first backoff step and drive the second attempt through the same store; it
        // fails too and schedules the longer second step.
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await scope.ServiceProvider.GetRequiredService<IOutbox>().DispatchDueAsync();
        Assert.That(publisher.Failures, Is.EqualTo(2), "the first backed-off retry failed too");

        // Commit: the failed attempts are the store's own record now, and the next attempt is the
        // run's dispatchers' to make.
        await unit.CommitAsync();

        // Assert: the event still arrived, the row is sent with both failed attempts recorded, and
        // the session was billed exactly once.
        var issued = await AwaitInvoiceIssuedAsync(started.Id);
        var stored = await ReadInvoiceAsync(started.Id);
        var invoiceRows = await CsmsDatabase.CountInvoicesAsync(Proto.Context, started.Id);
        var row = await AwaitSentOutboxRowAsync(started.Id);

        Assert.Multiple(() =>
        {
            Assert.That(issued.InvoiceId, Is.EqualTo(stored.Id), "the delivered event belongs to the stored invoice");
            Assert.That(invoiceRows, Is.EqualTo(1), "the failed attempts and their retry must bill the session once");
            Assert.That(publisher.Failures, Is.EqualTo(2), "the substituted publisher saw both failures");
            Assert.That(publisher.Attempts, Is.GreaterThanOrEqualTo(2), "the attempts are observable at the publisher");
            Assert.That(row.Attempts, Is.EqualTo(2), "the store recorded both failed attempts before the send");
            Assert.That(row.SentAtUtc, Is.Not.Null, "the delivered row was marked sent");
        });
    }

    private static async Task<OutboxRowState> AwaitSentOutboxRowAsync(Guid sessionId)
    {
        var result = await ProtoPolling.PollAsync(
            cancellationToken => new ValueTask<OutboxRowState>(
                CsmsDatabase.ReadOutboxAsync(Proto.Context, sessionId, cancellationToken)),
            row => row.SentAtUtc is not null,
            DeliveryTimeout,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);
        return result.Value;
    }

    private static async Task<int> AwaitOutboxEmptyAsync(Guid sessionId)
    {
        var result = await ProtoPolling.PollAsync(
            cancellationToken => new ValueTask<int>(
                CsmsDatabase.CountPendingOutboxAsync(Proto.Context, sessionId, cancellationToken)),
            pending => pending == 0,
            DeliveryTimeout,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);
        return result.Value;
    }

    /// <summary>
    /// The broker address the run uses, resolved through the messaging options: an explicitly
    /// configured connection string wins, then the container or AppHost the run started.
    /// </summary>
    private static string BrokerAddress(ProtoExecutionContext context)
        => context.Service<RabbitMqOptions>().ConnectionString;

    private static async Task<InvoiceIssued> AwaitInvoiceIssuedAsync(Guid sessionId)
    {
        var message = await Proto.Context.Messaging().AwaitAsync(
            CsmsEvents.Exchange,
            candidate => IsInvoiceIssuedFor(candidate, sessionId),
            DeliveryTimeout);
        return message.ReadRequired<InvoiceIssued>();
    }

    private static async Task<InvoiceResponse> ReadInvoiceAsync(Guid sessionId)
    {
        using var response = await Proto.Context.Rest().GetAsync($"/api/sessions/{sessionId}/invoice");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<InvoiceResponse>()!;
    }

    private static async Task RecordMeterAsync(Guid sessionId, decimal totalKwh)
    {
        using var response = await Proto.Context.Rest()
            .Body(new { totalKwh })
            .PostAsync($"/api/sessions/{sessionId}/meter-values");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }

    private static async Task<SessionResponse> StartSessionAsync(Guid stationId, int connectorId)
    {
        using var response = await Proto.Context.Rest()
            .Body(new { stationId, connectorId })
            .PostAsync("/api/sessions");
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<SessionResponse>()!;
    }

    /// <summary>
    /// The tap receives every routing key on the exchange, so the match names the invoice event by
    /// requiring an invoice id as well as this session id.
    /// </summary>
    private static bool IsInvoiceIssuedFor(ProtoMessage message, Guid sessionId)
    {
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
