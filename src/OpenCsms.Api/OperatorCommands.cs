namespace OpenCsms.Api;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCsms.Api.Ocpp;
using OpenCsms.Data;
using OpenCsms.Domain.Ocpp;

/// <summary>
/// The operator commands that reach a connected charge point. One implementation serves the machine
/// API and the signed-in dashboard, which adds the operator role and the tenant scope in front of the
/// same code, so a fix reaches both doors.
/// </summary>
internal static class OperatorCommands
{
    /// <summary>
    /// The operator asks a connected charge point to start a transaction. The session itself starts
    /// when the charge point sends its StartTransaction, exactly as OCPP prescribes.
    /// </summary>
    public static async Task<IResult> RemoteStartAsync(
        Guid id,
        RemoteStartRequest request,
        CsmsDbContext db,
        ChargePointConnections connections,
        IOptions<OcppGatewayOptions> gatewayOptions,
        CancellationToken cancellationToken)
    {
        var station = await db.Stations.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (station is null)
        {
            return Results.NotFound(new { message = $"No station '{id}'." });
        }

        if (!connections.TryGet(station.ChargePointId, out var connection))
        {
            return Results.Conflict(new { message = $"Charge point '{station.ChargePointId}' is not connected." });
        }

        try
        {
            var answer = await connection.CallAsync<RemoteStartTransactionResponse>(
                OcppActions.RemoteStartTransaction,
                new RemoteStartTransactionRequest(request.IdTag, request.ConnectorId),
                TimeSpan.FromSeconds(gatewayOptions.Value.RemoteCallTimeoutSeconds),
                cancellationToken);
            return Results.Ok(RemoteCommandResponse.From(answer.IdTagInfo));
        }
        catch (TimeoutException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (OcppCallException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>The operator asks a connected charge point to stop the transaction of a session.</summary>
    public static async Task<IResult> RemoteStopAsync(
        Guid id,
        CsmsDbContext db,
        ChargePointConnections connections,
        IOptions<OcppGatewayOptions> gatewayOptions,
        CancellationToken cancellationToken)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (session is null)
        {
            return Results.NotFound(new { message = $"No session '{id}'." });
        }

        if (!session.IsOpen)
        {
            return Results.Conflict(new { message = $"Session '{id}' has already ended." });
        }

        var station = await db.Stations.FirstOrDefaultAsync(candidate => candidate.Id == session.StationId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{id}' references an unknown station '{session.StationId}'.");
        if (!connections.TryGet(station.ChargePointId, out var connection))
        {
            return Results.Conflict(new { message = $"Charge point '{station.ChargePointId}' is not connected." });
        }

        try
        {
            var answer = await connection.CallAsync<RemoteStopTransactionResponse>(
                OcppActions.RemoteStopTransaction,
                new RemoteStopTransactionRequest(session.TransactionId),
                TimeSpan.FromSeconds(gatewayOptions.Value.RemoteCallTimeoutSeconds),
                cancellationToken);
            return Results.Ok(RemoteCommandResponse.From(answer.IdTagInfo));
        }
        catch (TimeoutException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (OcppCallException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
