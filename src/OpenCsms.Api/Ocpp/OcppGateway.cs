namespace OpenCsms.Api.Ocpp;

using Microsoft.Extensions.Options;
using OpenCsms.Application.Catalog;
using OpenCsms.Application.ChargePoints;
using OpenCsms.Domain;
using OpenCsms.Protocol.Ocpp;

/// <summary>
/// The OCPP 1.6J gateway: one WebSocket per charge point. Unimplemented actions are refused with a
/// call error instead of a guess.
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
            // The run stopped or the client went away.
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
        var services = scope.ServiceProvider;
        // The station is resolved before the payload is read, so an unknown identity is refused the
        // same way whatever shape the call has.
        var station = await services.GetRequiredService<StationReads>()
            .FindByChargePointIdAsync(chargePointId, cancellationToken);
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        return call.Action switch
        {
            OcppActions.BootNotification => await BootAsync(services, chargePointId, now, cancellationToken),
            OcppActions.Heartbeat => await HeartbeatAsync(services, Require(station, chargePointId), now, cancellationToken),
            OcppActions.StatusNotification => await StatusAsync(
                services,
                Require(station, chargePointId),
                Read<StatusNotificationRequest>(call),
                cancellationToken),
            OcppActions.StartTransaction => await StartTransactionAsync(
                services,
                Require(station, chargePointId),
                Read<StartTransactionRequest>(call),
                cancellationToken),
            OcppActions.MeterValues => await MeterValuesAsync(
                services,
                Require(station, chargePointId),
                Read<MeterValuesRequest>(call),
                cancellationToken),
            OcppActions.StopTransaction => await StopTransactionAsync(
                services,
                Require(station, chargePointId),
                Read<StopTransactionRequest>(call),
                cancellationToken),
            _ => throw new OcppCallException(
                OcppErrorCodes.NotImplemented,
                $"The CSMS does not implement '{call.Action}'. It speaks: BootNotification, Heartbeat, " +
                "StatusNotification, StartTransaction, MeterValues, StopTransaction, RemoteStartTransaction, RemoteStopTransaction.")
        };
    }

    private async Task<object> BootAsync(
        IServiceProvider services,
        string chargePointId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var seen = await services.GetRequiredService<StationSeen>().RecordAsync(chargePointId, cancellationToken);
        // The operator registered no station for this identity; the charge point retries after a fix.
        var status = seen is StationSeenRecorded
            ? OcppRegistrationStatus.Accepted
            : OcppRegistrationStatus.Rejected;
        return new BootNotificationResponse(status, now, options.Value.HeartbeatIntervalSeconds);
    }

    private static async Task<object> HeartbeatAsync(
        IServiceProvider services,
        Station station,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await services.GetRequiredService<StationSeen>().RecordAsync(station.ChargePointId, cancellationToken);
        return new HeartbeatResponse(now);
    }

    private static async Task<object> StatusAsync(
        IServiceProvider services,
        Station station,
        StatusNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await services.GetRequiredService<ConnectorStatusReport>().ReportAsync(
            station,
            request.ConnectorId,
            OcppTranslator.ToDomain(request.Status),
            OcppTranslator.ToDomain(request.ErrorCode),
            cancellationToken);
        return outcome switch
        {
            ConnectorStatusRecorded => new StatusNotificationResponse(),
            ConnectorOutsideStation outside => throw OutsideTheStation(outside.Station, outside.ConnectorId),
            _ => throw new InvalidOperationException("Unhandled connector status outcome.")
        };
    }

    private static async Task<object> StartTransactionAsync(
        IServiceProvider services,
        Station station,
        StartTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await services.GetRequiredService<TransactionStart>().StartAsync(
            station,
            request.ConnectorId,
            request.IdTag,
            OcppEnergy.WattHoursToKwh(request.MeterStart),
            cancellationToken);
        return outcome switch
        {
            TransactionStarted started => new StartTransactionResponse(
                started.Session.TransactionId,
                new IdTagInfo(OcppAuthorizationStatus.Accepted)),
            TransactionIdTagInvalid => new StartTransactionResponse(
                0,
                new IdTagInfo(OcppAuthorizationStatus.Invalid)),
            // The connector is already charging; the CSMS points at the running transaction, as OCPP says.
            TransactionConcurrent concurrent => new StartTransactionResponse(
                concurrent.Session.TransactionId,
                new IdTagInfo(OcppAuthorizationStatus.ConcurrentTx)),
            TransactionConnectorOutsideStation outside => throw OutsideTheStation(outside.Station, outside.ConnectorId),
            TransactionStartRejected rejected => throw new OcppCallException(
                OcppErrorCodes.PropertyConstraintViolation,
                rejected.Message),
            _ => throw new InvalidOperationException("Unhandled transaction start outcome.")
        };
    }

    private static async Task<object> MeterValuesAsync(
        IServiceProvider services,
        Station station,
        MeterValuesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TransactionId is not { } transactionId)
        {
            throw new OcppCallException(OcppErrorCodes.FormationViolation, "MeterValues needs the transaction id it belongs to.");
        }

        // The frame carries the connector's cumulative register; the session bills the part above
        // its own start reading.
        var readingKwh = OcppEnergy.ReadKwh(request.MeterValue);
        var outcome = await services.GetRequiredService<TransactionMeterValues>().RecordAsync(
            station,
            transactionId,
            readingKwh,
            cancellationToken);
        return outcome switch
        {
            MeterValuesRecorded => new MeterValuesResponse(),
            MeterValuesTransactionMissing missing => throw MissingTransaction(missing.Station, missing.TransactionId),
            MeterValuesTransactionEnded ended => throw new OcppCallException(
                OcppErrorCodes.InternalError,
                $"Transaction {ended.Session.TransactionId} has already ended."),
            MeterValuesRejected rejected => throw new OcppCallException(
                OcppErrorCodes.PropertyConstraintViolation,
                rejected.Message),
            _ => throw new InvalidOperationException("Unhandled meter values outcome.")
        };
    }

    private static async Task<object> StopTransactionAsync(
        IServiceProvider services,
        Station station,
        StopTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TransactionId is not { } transactionId)
        {
            throw new OcppCallException(OcppErrorCodes.FormationViolation, "StopTransaction needs the transaction id it ends.");
        }

        var outcome = await services.GetRequiredService<TransactionStop>().StopAsync(
            station,
            transactionId,
            OcppEnergy.WattHoursToKwh(request.MeterStop),
            cancellationToken);
        return outcome switch
        {
            TransactionStopped => new StopTransactionResponse(new IdTagInfo(OcppAuthorizationStatus.Accepted)),
            // A stop transaction for a session that is already ended is answered, not processed twice.
            TransactionStopAlreadyEnded => new StopTransactionResponse(new IdTagInfo(OcppAuthorizationStatus.Accepted)),
            TransactionStopMissing missing => throw MissingTransaction(missing.Station, missing.TransactionId),
            StopReadingRejected rejected => throw new OcppCallException(
                OcppErrorCodes.PropertyConstraintViolation,
                rejected.Message),
            StopOperationRejected rejected => throw new OcppCallException(
                OcppErrorCodes.InternalError,
                rejected.Message),
            _ => throw new InvalidOperationException("Unhandled stop transaction outcome.")
        };
    }

    private static OcppCallException MissingTransaction(Station station, int transactionId)
        => new(OcppErrorCodes.InternalError, $"Station '{station.Name}' has no transaction {transactionId}.");

    private static T Read<T>(OcppCall call) => OcppJson.ReadPayload<T>(call.Payload);

    private static Station Require(Station? station, string chargePointId)
        => station ?? throw NoStation(chargePointId);

    private static OcppCallException NoStation(string chargePointId)
        => new(
            OcppErrorCodes.InternalError,
            $"No station is registered for charge point '{chargePointId}'; register it over the API first.");

    private static OcppCallException OutsideTheStation(Station station, int connectorId)
        => new(
            OcppErrorCodes.PropertyConstraintViolation,
            $"Connector {connectorId} is outside station '{station.Name}', which has {station.ConnectorCount} connector(s).");
}
