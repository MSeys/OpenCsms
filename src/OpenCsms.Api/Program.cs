namespace OpenCsms.Api;

using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCsms.Api.Ocpp;
using OpenCsms.Application;
using OpenCsms.Application.Billing;
using OpenCsms.Application.Catalog;
using OpenCsms.Application.Commands;
using OpenCsms.Application.Ports;
using OpenCsms.Application.Sessions;
using OpenCsms.Contracts;
using OpenCsms.Domain;
using OpenCsms.Infrastructure;

public sealed class Program
{
    public static void Main(string[] args) => Create(args).Run();

    /// <summary>
    /// Builds the application without running it, so a suite can host the same entry point on its own
    /// loopback listener for the browser journeys: the suite passes this method to the host's
    /// loopback registration, and the run's collected configuration (database, broker, dashboard
    /// build) arrives as command-line arguments, which the builder below reads like any other host.
    /// </summary>
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // The composition root: the store and its ports, then the use cases over them, then the
        // event publisher and the OCPP edge. The API itself only binds requests and maps answers.
        builder.Services.AddCsmsInfrastructure();
        builder.Services.AddCsmsApplication();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddCsmsAuthentication();
        // The dashboard's cookie mutations (tariff repricing, remote commands) carry this header;
        // the SPA fetches the token from GET /api/auth/xsrf after sign-in.
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
        // A standalone run has the system clock; the suite replaces this with the test's clock.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<OcppGatewayOptions>(builder.Configuration.GetSection(OcppGatewayOptions.SectionName));
        // The operator commands' timeout lives with the use case; the composition root binds it from
        // the same Ocpp section the gateway reads.
        builder.Services.Configure<RemoteCommandOptions>(builder.Configuration.GetSection(OcppGatewayOptions.SectionName));
        // The connection registry is the transport's; the application sees it through its own port.
        // This host replaces the transport-less default the infrastructure registers with the
        // registry its own gateway fills while sockets live.
        builder.Services.AddSingleton<ChargePointConnections>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IChargePointConnections>(provider =>
            provider.GetRequiredService<ChargePointConnections>()));
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

        // The one anonymous management route: a tenant registers and receives its machine key once.
        api.MapTenantEndpoints();

        // The management surface. Every route requires a tenant's machine key, and the tenant the
        // call acts on is the credential's, never the request's: a foreign row answers 404 exactly
        // like an unknown one.
        var machine = app.MapGroup("/api")
            .WithTags("Csms")
            .RequireAuthorization(CsmsPolicies.Machine);

        machine.MapPost("/tariffs", async (
            RegisterTariffRequest request,
            ClaimsPrincipal user,
            TariffRegistration registration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var tariff = await registration.RegisterAsync(
                    new RegisterTariffCommand(
                        CsmsTenantClaims.TenantId(user),
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
        })
            .Produces<TariffResponse>(StatusCodes.Status201Created);

        machine.MapGet("/tariffs", async (
            ClaimsPrincipal user,
            TariffReads tariffs,
            CancellationToken cancellationToken) =>
            Results.Ok((await tariffs.ListAsync(
                CsmsTenantClaims.TenantId(user),
                cancellationToken)).Select(ApiMappings.ToTariffResponse)))
            .Produces<IEnumerable<TariffResponse>>(StatusCodes.Status200OK);

        machine.MapPost("/stations", async (
            RegisterStationRequest request,
            ClaimsPrincipal user,
            StationRegistration registration,
            CancellationToken cancellationToken) =>
        {
            RegisterStationOutcome outcome;
            try
            {
                outcome = await registration.RegisterAsync(
                    new RegisterStationCommand(
                        CsmsTenantClaims.TenantId(user),
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
        })
            .Produces<StationResponse>(StatusCodes.Status201Created);

        machine.MapGet("/stations/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            StationReads stations,
            CancellationToken cancellationToken) =>
            await stations.FindForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken) is { } station
                ? Results.Ok(ApiMappings.ToStationResponse(station))
                : Results.NotFound(new { message = $"No station '{id}'." }))
            .Produces<StationResponse>(StatusCodes.Status200OK);

        machine.MapGet("/stations/{id:guid}/summary", async (
            Guid id,
            ClaimsPrincipal user,
            StationReads stations,
            CancellationToken cancellationToken) =>
            await stations.FindForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken) is { } station
                ? Results.Ok(new { station.Id, station.Name, station.ConnectorCount, station.LastSeenAtUtc })
                : Results.NotFound(new { message = $"No station '{id}'." }));

        machine.MapGet("/stations/{id:guid}/connectors", async (
            Guid id,
            ClaimsPrincipal user,
            StationReads stations,
            ConnectorReads connectors,
            CancellationToken cancellationToken) =>
        {
            if (!await stations.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            var rows = await connectors.ListByStationAsync(id, cancellationToken);
            return Results.Ok(rows.Select(ApiMappings.ToConnectorResponse));
        })
            .Produces<IEnumerable<ConnectorResponse>>(StatusCodes.Status200OK);

        machine.MapGet("/stations/{id:guid}/sessions", async (
            Guid id,
            ClaimsPrincipal user,
            StationReads stations,
            SessionReads sessions,
            CancellationToken cancellationToken) =>
        {
            if (!await stations.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            var rows = await sessions.ListByStationAsync(id, cancellationToken);
            return Results.Ok(rows.Select(ApiMappings.ToSessionResponse));
        })
            .Produces<IEnumerable<SessionResponse>>(StatusCodes.Status200OK);

        machine.MapPost("/sessions", async (
            StartSessionRequest request,
            ClaimsPrincipal user,
            StationReads stations,
            SessionStart start,
            CancellationToken cancellationToken) =>
        {
            if (!await stations.ExistsForTenantAsync(
                    request.StationId,
                    CsmsTenantClaims.TenantId(user),
                    cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{request.StationId}'." });
            }

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
        })
            .Produces<SessionResponse>(StatusCodes.Status201Created);

        machine.MapGet("/sessions/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            SessionReads sessions,
            CancellationToken cancellationToken) =>
        {
            if (!await sessions.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

            return await sessions.FindAsync(id, cancellationToken) is { } session
                ? Results.Ok(ApiMappings.ToSessionResponse(session))
                : Results.NotFound(new { message = $"No session '{id}'." });
        })
            .Produces<SessionResponse>(StatusCodes.Status200OK);

        machine.MapPost("/sessions/{id:guid}/meter-values", async (
            Guid id,
            MeterValueRequest request,
            ClaimsPrincipal user,
            SessionReads sessions,
            SessionMeterValues meterValues,
            CancellationToken cancellationToken) =>
        {
            if (!await sessions.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

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
        })
            .Produces<SessionResponse>(StatusCodes.Status200OK);

        machine.MapPost("/sessions/{id:guid}/end", async (
            Guid id,
            ClaimsPrincipal user,
            SessionReads sessions,
            SessionEnding ending,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            if (!await sessions.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

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

            return Results.Ok(ApiMappings.ToSessionResponse(session));
        })
            .Produces<SessionResponse>(StatusCodes.Status200OK);

        machine.MapPost("/stations/{id:guid}/remote-start", async (
            Guid id,
            RemoteStartRequest request,
            ClaimsPrincipal user,
            StationReads stations,
            OperatorCommands commands,
            CancellationToken cancellationToken) =>
        {
            if (!await stations.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            return RemoteCommandResults.Map(await commands.RemoteStartAsync(
                id,
                request.IdTag,
                request.ConnectorId,
                cancellationToken));
        })
            .Produces<RemoteCommandResponse>(StatusCodes.Status200OK);

        machine.MapPost("/sessions/{id:guid}/remote-stop", async (
            Guid id,
            ClaimsPrincipal user,
            SessionReads sessions,
            OperatorCommands commands,
            CancellationToken cancellationToken) =>
        {
            if (!await sessions.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

            return RemoteCommandResults.Map(await commands.RemoteStopAsync(id, cancellationToken));
        })
            .Produces<RemoteCommandResponse>(StatusCodes.Status200OK);

        machine.MapGet("/sessions/{id:guid}/invoice", async (
            Guid id,
            ClaimsPrincipal user,
            SessionReads sessions,
            InvoiceReads invoices,
            CancellationToken cancellationToken) =>
        {
            if (!await sessions.ExistsForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken))
            {
                return Results.NotFound(new { message = $"Session '{id}' has no invoice yet." });
            }

            return await invoices.FindBySessionAsync(id, cancellationToken) is { } invoice
                ? Results.Ok(ApiMappings.ToInvoiceResponse(invoice))
                : Results.NotFound(new { message = $"Session '{id}' has no invoice yet." });
        })
            .Produces<InvoiceResponse>(StatusCodes.Status200OK);

        machine.MapGet("/invoices/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            InvoiceReads invoices,
            CancellationToken cancellationToken) =>
            await invoices.FindForTenantAsync(id, CsmsTenantClaims.TenantId(user), cancellationToken) is { } invoice
                ? Results.Ok(ApiMappings.ToInvoiceResponse(invoice))
                : Results.NotFound(new { message = $"No invoice '{id}'." }))
            .Produces<InvoiceResponse>(StatusCodes.Status200OK);

        // The only cookie-authenticated route on the machine surface: the file is tenant-scoped,
        // so the tenant comes from the session claim, never from the request.
        api.MapGet("/invoices/export", async (
            string? month,
            ClaimsPrincipal user,
            MonthlyInvoiceExport export,
            CancellationToken cancellationToken) =>
        {
            var tenantId = CsmsTenantClaims.TenantId(user);
            try
            {
                var file = await export.ExportAsync(tenantId, month, cancellationToken);
                return Results.File(
                    file.Content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    file.FileName);
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
        }).RequireAuthorization(CsmsPolicies.TenantUser);

        api.MapIdentityEndpoints(machine);
        app.MapDashboardEndpoints();

        return app;
    }

    private static IResult Problem(ArgumentException exception)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message]
        });
}
