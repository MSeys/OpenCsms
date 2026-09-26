namespace OpenCsms.Api;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Api.Ocpp;
using OpenCsms.Data;
using OpenCsms.Domain;
using OpenCsms.Messaging;

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

        builder.Services.AddCsmsData();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddRabbitMqEventPublisher();
        builder.Services.AddCsmsAuthentication();
        // A standalone run has the system clock; the suite replaces this with the test's clock.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<OcppGatewayOptions>(builder.Configuration.GetSection(OcppGatewayOptions.SectionName));
        builder.Services.AddSingleton<ChargePointConnections>();
        builder.Services.AddSingleton<OcppGateway>();

        var app = builder.Build();

        // The suite starts PostgreSQL before the application, so migrating at boot is safe in every
        // mode. This is the only migrator (R1a-07): EF Core 8 does not serialize concurrent
        // migrations, the suite starts the hosts sequentially today, and M4's concurrent boot
        // decision will revisit this (a PostgreSQL advisory lock, or one designated migrator).
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

        app.MapGet("/healthz", async (CsmsDbContext db, CancellationToken cancellationToken) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "healthy" })
                : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable));

        var api = app.MapGroup("/api").WithTags("Csms");

        api.MapPost("/tariffs", async (
            RegisterTariffRequest request,
            CsmsDbContext db,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var tariff = Tariff.Create(
                    request.TenantId,
                    request.Name,
                    request.EnergyPricePerKwh,
                    request.StartFee,
                    request.IdleFeePerHour,
                    request.IdleGracePeriod,
                    request.Currency ?? "EUR");
                db.Tariffs.Add(tariff);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/tariffs/{tariff.Id}", TariffResponse.From(tariff));
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
        });

        api.MapGet("/tariffs", async (string? tenantId, CsmsDbContext db, CancellationToken cancellationToken) =>
        {
            var tariffs = await db.Tariffs
                .Where(tariff => tenantId == null || tariff.TenantId == tenantId)
                .OrderBy(tariff => tariff.Name)
                .ToListAsync(cancellationToken);
            return Results.Ok(tariffs.Select(TariffResponse.From));
        });

        api.MapPost("/stations", async (
            RegisterStationRequest request,
            CsmsDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tariff = await db.Tariffs.FirstOrDefaultAsync(candidate => candidate.Id == request.TariffId, cancellationToken);
            if (tariff is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["TariffId"] = [$"No tariff '{request.TariffId}' is registered."]
                });
            }

            if (!string.Equals(tariff.TenantId, request.TenantId, StringComparison.Ordinal))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["TariffId"] = ["The tariff belongs to another tenant."]
                });
            }

            try
            {
                var station = Station.Register(
                    request.TenantId,
                    request.ChargePointId,
                    request.Name,
                    request.ConnectorCount,
                    request.TariffId);
                db.Stations.Add(station);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/stations/{station.Id}", StationResponse.From(station));
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
        });

        api.MapGet("/stations/{id:guid}", async (Guid id, CsmsDbContext db, CancellationToken cancellationToken) =>
            await db.Stations.FirstOrDefaultAsync(station => station.Id == id, cancellationToken) is { } station
                ? Results.Ok(StationResponse.From(station))
                : Results.NotFound(new { message = $"No station '{id}'." }));

        api.MapGet("/stations/{id:guid}/connectors", async (Guid id, CsmsDbContext db, CancellationToken cancellationToken) =>
        {
            if (!await db.Stations.AnyAsync(station => station.Id == id, cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            var connectors = await db.Connectors
                .Where(connector => connector.StationId == id)
                .OrderBy(connector => connector.ConnectorId)
                .ToListAsync(cancellationToken);
            return Results.Ok(connectors.Select(ConnectorResponse.From));
        });

        api.MapGet("/stations/{id:guid}/sessions", async (Guid id, CsmsDbContext db, CancellationToken cancellationToken) =>
        {
            if (!await db.Stations.AnyAsync(station => station.Id == id, cancellationToken))
            {
                return Results.NotFound(new { message = $"No station '{id}'." });
            }

            var sessions = await db.Sessions
                .Where(session => session.StationId == id)
                .OrderByDescending(session => session.StartedAtUtc)
                .ToListAsync(cancellationToken);
            return Results.Ok(sessions.Select(SessionResponse.From));
        });

        api.MapPost("/sessions", async (
            StartSessionRequest request,
            CsmsDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var station = await db.Stations.FirstOrDefaultAsync(candidate => candidate.Id == request.StationId, cancellationToken);
            if (station is null)
            {
                return Results.NotFound(new { message = $"No station '{request.StationId}'." });
            }

            if (request.ConnectorId < 1 || request.ConnectorId > station.ConnectorCount)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ConnectorId"] = [$"Station '{station.Name}' has {station.ConnectorCount} connector(s)."]
                });
            }

            var session = ChargingSession.Start(station.TenantId, station.Id, request.ConnectorId, clock.GetUtcNow());
            db.Sessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/sessions/{session.Id}", SessionResponse.From(session));
        });

        api.MapGet("/sessions/{id:guid}", async (Guid id, CsmsDbContext db, CancellationToken cancellationToken) =>
            await db.Sessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken) is { } session
                ? Results.Ok(SessionResponse.From(session))
                : Results.NotFound(new { message = $"No session '{id}'." }));

        api.MapPost("/sessions/{id:guid}/meter-values", async (
            Guid id,
            MeterValueRequest request,
            CsmsDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var session = await db.Sessions.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (session is null)
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

            try
            {
                session.RecordMeter(clock.GetUtcNow(), request.TotalKwh);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(SessionResponse.From(session));
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
            CsmsDbContext db,
            TimeProvider clock,
            IEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var session = await db.Sessions.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (session is null)
            {
                return Results.NotFound(new { message = $"No session '{id}'." });
            }

            try
            {
                await SessionEnding.EndAsync(db, session, clock.GetUtcNow(), cancellationToken);
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }

            await SessionEnding.PublishAsync(publisher, session, cancellationToken);
            return Results.Ok(SessionResponse.From(session));
        });

        api.MapPost("/stations/{id:guid}/remote-start", OperatorCommands.RemoteStartAsync);
        api.MapPost("/sessions/{id:guid}/remote-stop", OperatorCommands.RemoteStopAsync);

        api.MapGet("/sessions/{id:guid}/invoice", async (
            Guid id,
            CsmsDbContext db,
            CancellationToken cancellationToken) =>
            await db.Invoices.FirstOrDefaultAsync(invoice => invoice.SessionId == id, cancellationToken) is { } invoice
                ? Results.Ok(InvoiceResponse.From(invoice))
                : Results.NotFound(new { message = $"Session '{id}' has no invoice yet." }));

        api.MapGet("/invoices/{id:guid}", async (Guid id, CsmsDbContext db, CancellationToken cancellationToken) =>
            await db.Invoices.FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken) is { } invoice
                ? Results.Ok(InvoiceResponse.From(invoice))
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
