namespace OpenCsms.Suite.Support;

using System.Net;
using ProtoTest.WireMock;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

/// <summary>
/// The stubs the notification journeys put on the per-run target fakes. Parallel tests share those
/// fakes, so the stubs are registered on the raw server with explicit priorities instead of the
/// facade's path stubs: an accepting catch-all sits at a low priority, a rejection for one entity's
/// notifications sits above it and matches the body's session id, and no registration order can
/// change which one answers a request. The entity-keyed rejection is what lets a fault journey run
/// beside the notification journeys without touching their deliveries.
/// </summary>
public static class NotificationTargetStubs
{
    /// <summary>The catch-all's priority; lower numbers win, so this sits below the rejection.</summary>
    private const int AcceptPriority = 10;

    /// <summary>The rejection's priority: above the catch-all, below nothing else.</summary>
    private const int RejectPriority = 1;

    /// <summary>Accepts every notification under the path with a 202, the target's happy answer.</summary>
    public static void Accept(ProtoWireMockClient target, string path)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Server.Given(Request.Create()
                .WithPath(path + "/*")
                .UsingPost())
            .AtPriority(AcceptPriority)
            .RespondWith(Response.Create()
                .WithStatusCode((int)HttpStatusCode.Accepted)
                .WithBodyAsJson(new { status = "accepted" }));
    }

    /// <summary>Rejects with a 503 the notifications whose body names this session - and only those.</summary>
    public static void Reject(ProtoWireMockClient target, string path, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Server.Given(Request.Create()
                .WithPath(path + "/*")
                .UsingPost()
                .WithBody(new JsonPartialMatcher(new { sessionId })))
            .AtPriority(RejectPriority)
            .RespondWith(Response.Create()
                .WithStatusCode((int)HttpStatusCode.ServiceUnavailable));
    }
}
