namespace OpenCsms.Api;

using Microsoft.EntityFrameworkCore;
using OpenCsms.Contracts;
using OpenCsms.Data;
using OpenCsms.Domain;
using OpenCsms.Messaging;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddCsmsData();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddRabbitMqEventPublisher();

        var app = builder.Build();

        // The suite starts PostgreSQL before the application, so migrating at boot is safe in every
        // mode. This is the only migrator (R1a-07): EF Core 8 does not serialize concurrent
        // migrations, the suite starts the hosts sequentially today, and M4's concurrent boot
        // decision will revisit this (a PostgreSQL advisory lock, or one designated migrator).
        app.Services.MigrateCsmsData();

        app.UseExceptionHandler();
        app.UseSwagger();

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
                var station = Station.Register(request.TenantId, request.Name, request.ConnectorCount, request.TariffId);
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
                session.End(clock.GetUtcNow());
            }
            catch (ArgumentException exception)
            {
                return Problem(exception);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }

            await db.SaveChangesAsync(cancellationToken);
            await publisher.PublishAsync(
                CsmsEvents.SessionEndedRoutingKey,
                new SessionEnded(
                    session.Id,
                    session.TenantId,
                    session.StationId,
                    session.ConnectorId,
                    session.StartedAtUtc,
                    session.EndedAtUtc!.Value,
                    session.EnergyKwh),
                cancellationToken);
            return Results.Ok(SessionResponse.From(session));
        });

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

        await app.RunAsync();
    }

    private static IResult Problem(ArgumentException exception)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message]
        });
}
