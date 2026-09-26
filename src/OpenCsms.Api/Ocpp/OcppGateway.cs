namespace OpenCsms.Api.Ocpp;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCsms.Data;
using OpenCsms.Domain;
using OpenCsms.Domain.Ocpp;
using OpenCsms.Messaging;

/// <summary>
/// The OCPP 1.6J gateway: one WebSocket per charge point at <c>/ocpp/{chargePointId}</c>. Calls update
/// the station's domain state and the charging-session lifecycle through the same store and the same
/// <c>session.ended</c> event the REST API uses; answers are call results, and anything the subset does
/// not implement is refused with a call error instead of a guess. The exact subset is documented in the
/// repository README.
/// </summary>
public sealed class OcppGateway(
    IServiceScopeFactory scopeFactory,
    ChargePointConnections connections,
    IOptions<OcppGatewayOptions> options,
    ILogger<OcppGateway> logger)
{
    /// <summary>Handles the WebSocket request: accept, run the conversation, release the connection.</summary>
    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("The OCPP endpoint speaks WebSocket.");
            return;
        }

        if (context.Request.RouteValues["chargePointId"] is not string chargePointId || string.IsNullOrWhiteSpace(chargePointId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("The OCPP path needs the charge point's identity.");
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new ChargePointConnection(chargePointId, socket);
        await connections.RegisterAsync(connection);
        logger.LogInformation("Charge point '{ChargePointId}' connected over OCPP.", chargePointId);
        try
        {
            await connection.RunAsync(
                (call, cancellationToken) => DispatchAsync(chargePointId, call, cancellationToken),
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The run stopped or the client went away; the connection is released below either way.
        }
        finally
        {
            connections.Remove(connection);
            await connection.DisposeAsync();
            logger.LogInformation("Charge point '{ChargePointId}' disconnected.", chargePointId);
        }
    }

    private async Task<object> DispatchAsync(string chargePointId, OcppCall call, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsmsDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var station = await db.Stations.FirstOrDefaultAsync(
            candidate => candidate.ChargePointId == chargePointId,
            cancellationToken);

        return call.Action switch
        {
            OcppActions.BootNotification => await BootAsync(db, station, now, cancellationToken),
            OcppActions.Heartbeat => await HeartbeatAsync(db, Require(station, chargePointId), now, cancellationToken),
            OcppActions.StatusNotification => await StatusAsync(db, Require(station, chargePointId), Read<StatusNotificationRequest>(call), now, cancellationToken),
            OcppActions.StartTransaction => await StartTransactionAsync(db, Require(station, chargePointId), Read<StartTransactionRequest>(call), now, cancellationToken),
            OcppActions.MeterValues => await MeterValuesAsync(db, Require(station, chargePointId), Read<MeterValuesRequest>(call), now, cancellationToken),
            OcppActions.StopTransaction => await StopTransactionAsync(db, publisher, Require(station, chargePointId), Read<StopTransactionRequest>(call), now, cancellationToken),
            _ => throw new OcppCallException(
                OcppErrorCodes.NotImplemented,
                $"The CSMS does not implement '{call.Action}'. It speaks: BootNotification, Heartbeat, " +
                "StatusNotification, StartTransaction, MeterValues, StopTransaction, RemoteStartTransaction, RemoteStopTransaction.")
        };
    }

    private async Task<object> BootAsync(CsmsDbContext db, Station? station, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interval = options.Value.HeartbeatIntervalSeconds;
        if (station is null)
        {
            // The operator registered no station for this identity; the charge point retries after a fix.
            return new BootNotificationResponse(OcppRegistrationStatus.Rejected, now, interval);
        }

        station.MarkSeen(now);
        await db.SaveChangesAsync(cancellationToken);
        return new BootNotificationResponse(OcppRegistrationStatus.Accepted, now, interval);
    }

    private static async Task<object> HeartbeatAsync(CsmsDbContext db, Station station, DateTimeOffset now, CancellationToken cancellationToken)
    {
        station.MarkSeen(now);
        await db.SaveChangesAsync(cancellationToken);
        return new HeartbeatResponse(now);
    }

    private static async Task<object> StatusAsync(
        CsmsDbContext db,
        Station station,
        StatusNotificationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.ConnectorId < 0 || request.ConnectorId > station.ConnectorCount)
        {
            throw OutsideTheStation(station, request.ConnectorId);
        }

        var connector = await db.Connectors.FirstOrDefaultAsync(
            candidate => candidate.StationId == station.Id && candidate.ConnectorId == request.ConnectorId,
            cancellationToken);
        if (connector is null)
        {
            db.Connectors.Add(Connector.Report(station.Id, request.ConnectorId, request.Status, request.ErrorCode, now));
        }
        else
        {
            connector.Update(request.Status, request.ErrorCode, now);
        }

        station.MarkSeen(now);
        await db.SaveChangesAsync(cancellationToken);
        return new StatusNotificationResponse();
    }

    private static async Task<object> StartTransactionAsync(
        CsmsDbContext db,
        Station station,
        StartTransactionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.ConnectorId < 1 || request.ConnectorId > station.ConnectorCount)
        {
            throw OutsideTheStation(station, request.ConnectorId);
        }

        if (string.IsNullOrWhiteSpace(request.IdTag))
        {
            return new StartTransactionResponse(0, new IdTagInfo(OcppAuthorizationStatus.Invalid));
        }

        var open = await db.Sessions.FirstOrDefaultAsync(
            candidate => candidate.StationId == station.Id
                && candidate.ConnectorId == request.ConnectorId
                && candidate.EndedAtUtc == null,
            cancellationToken);
        if (open is not null)
        {
            // The connector is already charging; the CSMS points at the running transaction, as OCPP says.
            return new StartTransactionResponse(open.TransactionId, new IdTagInfo(OcppAuthorizationStatus.ConcurrentTx));
        }

        ChargingSession session;
        try
        {
            // The connector's register at plug-in is this session's baseline: a second session on the
            // same connector bills only what it adds on top, not the register it started from.
            session = ChargingSession.Start(
                station.TenantId,
                station.Id,
                request.ConnectorId,
                now,
                request.MeterStart / 1000m);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new OcppCallException(OcppErrorCodes.PropertyConstraintViolation, exception.Message);
        }

        db.Sessions.Add(session);
        station.MarkSeen(now);
        await db.SaveChangesAsync(cancellationToken);
        return new StartTransactionResponse(session.TransactionId, new IdTagInfo(OcppAuthorizationStatus.Accepted));
    }

    private static async Task<object> MeterValuesAsync(
        CsmsDbContext db,
        Station station,
        MeterValuesRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.TransactionId is not { } transactionId)
        {
            throw new OcppCallException(OcppErrorCodes.FormationViolation, "MeterValues needs the transaction id it belongs to.");
        }

        var session = await FindSessionAsync(db, station, transactionId, cancellationToken);
        if (!session.IsOpen)
        {
            throw new OcppCallException(OcppErrorCodes.InternalError, $"Transaction {transactionId} has already ended.");
        }

        var readingKwh = OcppEnergy.ReadKwh(request.MeterValue);
        try
        {
            // The frame carries the connector's cumulative register; the session bills the part above
            // its own start reading.
            session.RecordMeter(now, readingKwh);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new OcppCallException(OcppErrorCodes.PropertyConstraintViolation, exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new MeterValuesResponse();
    }

    private async Task<object> StopTransactionAsync(
        CsmsDbContext db,
        IEventPublisher publisher,
        Station station,
        StopTransactionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.TransactionId is not { } transactionId)
        {
            throw new OcppCallException(OcppErrorCodes.FormationViolation, "StopTransaction needs the transaction id it ends.");
        }

        var session = await FindSessionAsync(db, station, transactionId, cancellationToken);
        if (session.IsOpen)
        {
            // Only a stop reading above the last recorded register adds energy; a stop below it keeps
            // the session's last reading, exactly like a late MeterValues would.
            if (request.MeterStop / 1000m > session.LastReadingKwh)
            {
                try
                {
                    session.RecordMeter(now, request.MeterStop / 1000m);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new OcppCallException(OcppErrorCodes.PropertyConstraintViolation, exception.Message);
                }
            }

            try
            {
                await SessionEnding.EndAsync(db, session, now, cancellationToken);
            }
            catch (ArgumentException exception)
            {
                throw new OcppCallException(OcppErrorCodes.PropertyConstraintViolation, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                throw new OcppCallException(OcppErrorCodes.InternalError, exception.Message);
            }

            await SessionEnding.PublishAsync(publisher, session, cancellationToken);
        }

        // A stop transaction for a session that is already ended is answered, not processed twice: the
        // charge point that lost the connection may resend it.
        return new StopTransactionResponse(new IdTagInfo(OcppAuthorizationStatus.Accepted));
    }

    private static async Task<ChargingSession> FindSessionAsync(
        CsmsDbContext db,
        Station station,
        int transactionId,
        CancellationToken cancellationToken)
        => await db.Sessions.FirstOrDefaultAsync(
            candidate => candidate.TransactionId == transactionId && candidate.StationId == station.Id,
            cancellationToken)
            ?? throw new OcppCallException(
                OcppErrorCodes.InternalError,
                $"Station '{station.Name}' has no transaction {transactionId}.");

    private static T Read<T>(OcppCall call) => OcppJson.ReadPayload<T>(call.Payload);

    private static Station Require(Station? station, string chargePointId)
        => station ?? throw new OcppCallException(
            OcppErrorCodes.InternalError,
            $"No station is registered for charge point '{chargePointId}'; register it over the API first.");

    private static OcppCallException OutsideTheStation(Station station, int connectorId)
        => new(
            OcppErrorCodes.PropertyConstraintViolation,
            $"Connector {connectorId} is outside station '{station.Name}', which has {station.ConnectorCount} connector(s).");
}
