namespace OpenCsms.Api;

using OpenCsms.Application.Commands;
using OpenCsms.Contracts;

/// <summary>
/// The one mapping from the application's remote-command outcome to the API's own answer. Both the
/// machine surface and the dashboard reach it, so a failure branch answers the same status code and
/// message through either door.
/// </summary>
internal static class RemoteCommandResults
{
    public static IResult Map(RemoteCommandOutcome outcome) => outcome switch
    {
        RemoteCommandAccepted accepted => Results.Ok(ApiMappings.ToRemoteCommandResponse(accepted.Status)),
        RemoteCommandStationMissing missing => Results.NotFound(new { message = $"No station '{missing.StationId}'." }),
        RemoteCommandSessionMissing missing => Results.NotFound(new { message = $"No session '{missing.SessionId}'." }),
        RemoteCommandSessionEnded ended => Results.Conflict(new { message = $"Session '{ended.SessionId}' has already ended." }),
        RemoteCommandChargePointOffline offline
            => Results.Conflict(new { message = $"Charge point '{offline.ChargePointId}' is not connected." }),
        RemoteCommandTimedOut timedOut
            => Results.Problem(timedOut.Message, statusCode: StatusCodes.Status504GatewayTimeout),
        RemoteCommandRefused refused
            => Results.Problem(refused.Message, statusCode: StatusCodes.Status502BadGateway),
        _ => throw new InvalidOperationException("Unhandled remote command outcome.")
    };
}
