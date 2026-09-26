namespace OpenCsms.Api;

using OpenCsms.Api.Ocpp;
using OpenCsms.Application;
using OpenCsms.Application.Billing;
using OpenCsms.Application.Catalog;
using OpenCsms.Application.Commands;
using OpenCsms.Application.Ports;
using OpenCsms.Application.Sessions;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure;

public sealed class Program
{
    public static void Main(string[] args) => Create(args).Run();

    /// <summary>
    /// Builds the application without running it, so a suite can host the same entry point on its own
    /// loopback listener for the browser journeys. <paramref name="configuration"/> is the run's
    /// collected configuration (database, broker, dashboard build); an environment that already
    /// provides the values in its own configuration passes none.
    /// </summary>
    public static WebApplication Create(string[] args, IConfiguration? configuration = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (configuration is not null)
        {
            builder.Configuration.AddConfiguration(configuration);
        }

        // The composition root: the store and its ports, then the use cases over them, then the
        // event publisher and the OCPP edge. The API itself only binds requests and maps answers.
        builder.Services.AddCsmsInfrastructure();
        builder.Services.AddCsmsApplication();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddCsmsAuthentication();
        // A standalone run has the system clock; the suite replaces this with the test's clock.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<OcppGatewayOptions>(builder.Configuration.GetSection(OcppGatewayOptions.SectionName));
        // The operator commands' timeout lives with the use case; the composition root binds it from
        // the same Ocpp section the gateway reads.
        builder.Services.Configure<RemoteCommandOptions>(builder.Configuration.GetSection(OcppGatewayOptions.SectionName));
        // The connection registry is the transport's; the application sees it through its own port.
        builder.Services.AddSingleton<ChargePointConnections>();
        builder.Services.AddSingleton<IChargePointConnections>(provider =>
            provider.GetRequiredService<ChargePointConnections>());
        builder.Services.AddSingleton<OcppGateway>();

        var app = builder.Build();

        // The suite starts PostgreSQL before the application, so migrating at boot is safe in every
        // mode. This is the only migrator: EF Core 8 does not serialize concurrent migrations, and
        // the suite starts the hosts sequentially. Revisit this if hosts ever boot concurrently
        // (a PostgreSQL advisory lock, or one designated migrator).
        app.Services.MigrateCsmsData();

        app.UseExceptionHandler();
        app.UseSwagger();
        app.UseWebSockets();
        app.UseDashboard();
        app.UseAuthentication();
        app.UseAuthorization();

        // One WebSocket per charge point, at the conventional OCPP path. The gateway is hosted here,
        // so the suite reaches it through the in-process application and a deployment through a socket.
        app.Map("/ocpp/{chargePointId}", app.Services.GetRequiredService<OcppGateway>().HandleAsync);

        app.MapGet("/healthz", async (CsmsStoreProbe probe, CancellationToken cancellationToken) =>
            await probe.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "healthy" })
                : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable));

        var api = app.MapGroup("/api").WithTags("Csms");

        api.MapPost("/tariffs", async (
            RegisterTariffRequest request,
            TariffRegistration registration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var tariff = await registration.RegisterAsync(
                    new RegisterTariffCommand(
                        request.TenantId,
                        request.Name,
                        request.EnergyPricePerKwh,
                        request.StartFee,
                        request.IdleFeePerHour,
                        request.IdleGracePeriod,
                        request.Currency),
                    cancellationToken);
                return Results.Created($"/api/tariffs/{tariff.Id}", ApiMappings.ToTariffResponse(tariff));
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
        });

        api.MapGet("/tariffs", async (string? tenantId, TariffReads tariffs, CancellationToken cancellationToken) =>
            Results.Ok((await tariffs.ListAsync(tenantId, cancellationToken)).Select(ApiMappings.ToTariffResponse)));

        api.MapPost("/stations", async (
            RegisterStationRequest request,
            StationRegistration registration,
            CancellationToken cancellationToken) =>
        {
            RegisterStationOutcome outcome;
            try
            {
                outcome = await registration.RegisterAsync(
                    new RegisterStationCommand(
                        request.TenantId,
                        request.ChargePointId,
                        request.Name,
                        request.ConnectorCount,
                        request.TariffId),
                    cancellationToken);
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }

            return outcome switch
            {
                StationRegistered registered => Results.Created(
                    $"/api/stations/{registered.Station.Id}",
                    ApiMappings.ToStationResponse(registered.Station)),
                StationTariffMissing missing => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["TariffId"] = [$"No tariff '{missing.TariffId}' is registered."]
                }),
                StationTariffOfAnotherTenant => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["TariffId"] = ["The tariff belongs to another tenant."]
                }),
                _ => throw new InvalidOperationException("Unhandled station registration outcome.")
            };
        });

        api.MapGet("/stations/{id:guid}", async (Guid id, StationReads stations, CancellationToken cancellationToken) =>
            await stations.FindAsync(id, cancellationToken) is { } station
                ? Results.Ok(ApiMappings.ToStationResponse(station))
                : Results.NotFound(new { message = $"No station '{id}'." }));

        api.MapGet("/stations/{id:guid}/connectors", async (
            Guid id,
            StationReads stations,
            ConnectorReads connectors,
            CancellationToken cancellationToken) =>
        {
            if (!await stations.ExistsAsync(id, cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            var rows = await connectors.ListByStationAsync(id, cancellationToken);
            return Results.Ok(rows.Select(ApiMappings.ToConnectorResponse));
        });

        api.MapGet("/stations/{id:guid}/sessions", async (
            Guid id,
            StationReads stations,
            SessionReads sessions,
            CancellationToken cancellationToken) =>
        {
            if (!await stations.ExistsAsync(id, cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            var rows = await sessions.ListByStationAsync(id, cancellationToken);
            return Results.Ok(rows.Select(ApiMappings.ToSessionResponse));
        });

        api.MapPost("/sessions", async (
            StartSessionRequest request,
            SessionStart start,
            CancellationToken cancellationToken) =>
        {
            var outcome = await start.StartAsync(
                new StartSessionCommand(request.StationId, request.ConnectorId),
                cancellationToken);
            return outcome switch
            {
                SessionStarted started => Results.Created(
                    $"/api/sessions/{started.Session.Id}",
                    ApiMappings.ToSessionResponse(started.Session)),
                SessionStationMissing missing => Results.NotFound(new { message = $"No station '{missing.StationId}'." }),
                SessionConnectorOutsideStation outside => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ConnectorId"] =
                        [$"Station '{outside.Station.Name}' has {outside.Station.ConnectorCount} connector(s)."]
                }),
                _ => throw new InvalidOperationException("Unhandled session start outcome.")
            };
        });

        api.MapGet("/sessions/{id:guid}", async (Guid id, SessionReads sessions, CancellationToken cancellationToken) =>
            await sessions.FindAsync(id, cancellationToken) is { } session
                ? Results.Ok(ApiMappings.ToSessionResponse(session))
                : Results.NotFound(new { message = $"No session '{id}'." }));

        api.MapPost("/sessions/{id:guid}/meter-values", async (
            Guid id,
            MeterValueRequest request,
            SessionMeterValues meterValues,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return await meterValues.RecordAsync(id, request.TotalKwh, cancellationToken) is { } session
                    ? Results.Ok(ApiMappings.ToSessionResponse(session))
                    : Results.NotFound(new { message = $"No session '{id}'." });
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }
        });

        api.MapPost("/sessions/{id:guid}/end", async (
            Guid id,
            SessionReads sessions,
            SessionEnding ending,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var session = await sessions.FindAsync(id, cancellationToken);
            if (session is null)
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

            try
            {
                await ending.EndAsync(session, clock.GetUtcNow(), cancellationToken);
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }

            await ending.PublishAsync(session, cancellationToken);
            return Results.Ok(ApiMappings.ToSessionResponse(session));
        });

        api.MapPost("/stations/{id:guid}/remote-start", async (
            Guid id,
            RemoteStartRequest request,
            OperatorCommands commands,
            CancellationToken cancellationToken) =>
            RemoteCommandResults.Map(await commands.RemoteStartAsync(id, request.IdTag, request.ConnectorId, cancellationToken)));

        api.MapPost("/sessions/{id:guid}/remote-stop", async (
            Guid id,
            OperatorCommands commands,
            CancellationToken cancellationToken) =>
            RemoteCommandResults.Map(await commands.RemoteStopAsync(id, cancellationToken)));

        api.MapGet("/sessions/{id:guid}/invoice", async (
            Guid id,
            InvoiceReads invoices,
            CancellationToken cancellationToken) =>
            await invoices.FindBySessionAsync(id, cancellationToken) is { } invoice
                ? Results.Ok(ApiMappings.ToInvoiceResponse(invoice))
                : Results.NotFound(new { message = $"Session '{id}' has no invoice yet." }));

        api.MapGet("/invoices/{id:guid}", async (Guid id, InvoiceReads invoices, CancellationToken cancellationToken) =>
            await invoices.FindAsync(id, cancellationToken) is { } invoice
                ? Results.Ok(ApiMappings.ToInvoiceResponse(invoice))
                : Results.NotFound(new { message = $"No invoice '{id}'." }));

        api.MapIdentityEndpoints();
        app.MapDashboardEndpoints();

        return app;
    }

    private static IResult Problem(ArgumentException exception)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message]
        });
}
