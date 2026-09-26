namespace OpenCsms.Api;

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCsms.Api.Ocpp;
using OpenCsms.Data;
using OpenCsms.Domain;

/// <summary>
/// The read surface the signed-in dashboard consumes: every query is scoped to the session's tenant
/// claim, never to a tenant named in the request, so one operator cannot see another's rows. The
/// operator-only commands sit behind <see cref="CsmsPolicies.Operator"/> and scope first, so a station
/// of another tenant answers 404 exactly like an unknown one. The public status surface is separate and
/// anonymous, and carries no tenant or pricing detail.
/// </summary>
internal static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var dashboard = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization(CsmsPolicies.TenantUser);

        dashboard.MapGet("/stations", ListStationsAsync);
        dashboard.MapGet("/stations/{id:guid}", GetStationAsync);
        dashboard.MapGet("/stations/{id:guid}/sessions", ListStationSessionsAsync);
        dashboard.MapGet("/invoices", ListInvoicesAsync);
        dashboard.MapGet("/invoices/{id:guid}", GetInvoiceAsync);
        dashboard.MapGet("/tariffs", ListTariffsAsync);

        dashboard.MapPost("/stations/{id:guid}/remote-start", RemoteStartAsync)
            .RequireAuthorization(CsmsPolicies.Operator);
        dashboard.MapPost("/sessions/{id:guid}/remote-stop", RemoteStopAsync)
            .RequireAuthorization(CsmsPolicies.Operator);

        app.MapGet("/api/status/stations", ListPublicStationsAsync).WithTags("Status").AllowAnonymous();
    }

    private static async Task<IResult> ListStationsAsync(
        ClaimsPrincipal user,
        CsmsDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var stations = await db.Stations
            .Where(station => station.TenantId == tenantId)
            .OrderBy(station => station.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(stations.Select(DashboardStationResponse.From));
    }

    private static async Task<IResult> GetStationAsync(
        Guid id,
        ClaimsPrincipal user,
        CsmsDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        return await db.Stations.FirstOrDefaultAsync(
                station => station.Id == id && station.TenantId == tenantId,
                cancellationToken) is { } station
            ? Results.Ok(DashboardStationResponse.From(station))
            : Results.NotFound(new { message = $"No station '{id}'." });
    }

    private static async Task<IResult> ListStationSessionsAsync(
        Guid id,
        ClaimsPrincipal user,
        CsmsDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        if (!await db.Stations.AnyAsync(
                station => station.Id == id && station.TenantId == tenantId,
                cancellationToken))
        {
            return Results.NotFound(new { message = $"No station '{id}'." });
        }

        var sessions = await db.Sessions
            .Where(session => session.StationId == id)
            .OrderByDescending(session => session.StartedAtUtc)
            .ToListAsync(cancellationToken);
        return Results.Ok(sessions.Select(SessionResponse.From));
    }

    private static async Task<IResult> ListInvoicesAsync(
        ClaimsPrincipal user,
        CsmsDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var invoices = await db.Invoices
            .Where(invoice => invoice.TenantId == tenantId)
            .OrderByDescending(invoice => invoice.IssuedAtUtc)
            .ToListAsync(cancellationToken);
        return Results.Ok(invoices.Select(InvoiceResponse.From));
    }

    private static async Task<IResult> GetInvoiceAsync(
        Guid id,
        ClaimsPrincipal user,
        CsmsDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        return await db.Invoices.FirstOrDefaultAsync(
                invoice => invoice.Id == id && invoice.TenantId == tenantId,
                cancellationToken) is { } invoice
            ? Results.Ok(InvoiceResponse.From(invoice))
            : Results.NotFound(new { message = $"No invoice '{id}'." });
    }

    private static async Task<IResult> ListTariffsAsync(
        ClaimsPrincipal user,
        CsmsDbContext db,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var tariffs = await db.Tariffs
            .Where(tariff => tariff.TenantId == tenantId)
            .OrderBy(tariff => tariff.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(tariffs.Select(TariffResponse.From));
    }

    private static async Task<IResult> RemoteStartAsync(
        Guid id,
        RemoteStartRequest request,
        ClaimsPrincipal user,
        CsmsDbContext db,
        ChargePointConnections connections,
        IOptions<OcppGatewayOptions> gatewayOptions,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        if (!await db.Stations.AnyAsync(
                station => station.Id == id && station.TenantId == tenantId,
                cancellationToken))
        {
            return Results.NotFound(new { message = $"No station '{id}'." });
        }

        return await OperatorCommands.RemoteStartAsync(id, request, db, connections, gatewayOptions, cancellationToken);
    }

    private static async Task<IResult> RemoteStopAsync(
        Guid id,
        ClaimsPrincipal user,
        CsmsDbContext db,
        ChargePointConnections connections,
        IOptions<OcppGatewayOptions> gatewayOptions,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        if (!await db.Sessions.AnyAsync(
                session => session.Id == id && session.TenantId == tenantId,
                cancellationToken))
        {
            return Results.NotFound(new { message = $"No session '{id}'." });
        }

        return await OperatorCommands.RemoteStopAsync(id, db, connections, gatewayOptions, cancellationToken);
    }

    /// <summary>The public status page: every station's name, OCPP identity, last-seen stamp and connector statuses.</summary>
    private static async Task<IResult> ListPublicStationsAsync(CsmsDbContext db, CancellationToken cancellationToken)
    {
        var stations = await db.Stations
            .OrderBy(station => station.Name)
            .ToListAsync(cancellationToken);
        var connectors = await db.Connectors
            .OrderBy(connector => connector.ConnectorId)
            .ToListAsync(cancellationToken);
        var byStation = connectors
            .GroupBy(connector => connector.StationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConnectorResponse>)group.Select(ConnectorResponse.From).ToArray());

        return Results.Ok(stations.Select(station => PublicStatusStationResponse.From(
            station,
            byStation.TryGetValue(station.Id, out var stationConnectors) ? stationConnectors : [])));
    }

    private static string TenantId(ClaimsPrincipal user)
        => user.FindFirstValue(UserClaimTypes.TenantId)
           ?? throw new InvalidOperationException("The signed-in user carries no tenant claim.");
}
