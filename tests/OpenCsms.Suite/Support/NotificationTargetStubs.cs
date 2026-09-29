namespace OpenCsms.Suite.Support;

using System.Net;
using ProtoTest.WireMock;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

/// <summary>
/// Per-run target stubs with explicit priorities, so parallel tests cannot change which stub answers.
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
