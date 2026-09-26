namespace OpenCsms.Api;

using System.Security.Claims;
using OpenCsms.Application.Billing;
using OpenCsms.Application.Catalog;
using OpenCsms.Application.Commands;
using OpenCsms.Application.Sessions;
using OpenCsms.Contracts;
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
            .RequireAuthorization(CsmsPolicies.Operator)
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
        dashboard.MapPost("/sessions/{id:guid}/remote-stop", RemoteStopAsync)
            .RequireAuthorization(CsmsPolicies.Operator)
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
        dashboard.MapPut("/tariffs/{id:guid}", UpdateTariffAsync)
            .RequireAuthorization(CsmsPolicies.Operator)
            .AddEndpointFilter<ValidateAntiforgeryFilter>();

        app.MapGet("/api/status/stations", ListPublicStationsAsync).WithTags("Status").AllowAnonymous();
    }

    private static async Task<IResult> ListStationsAsync(
        ClaimsPrincipal user,
        StationReads stations,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var rows = await stations.ListAsync(tenantId, cancellationToken);
        return Results.Ok(rows.Select(ApiMappings.ToDashboardStationResponse));
    }

    private static async Task<IResult> GetStationAsync(
        Guid id,
        ClaimsPrincipal user,
        StationReads stations,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        return await stations.FindForTenantAsync(id, tenantId, cancellationToken) is { } station
            ? Results.Ok(ApiMappings.ToDashboardStationResponse(station))
            : Results.NotFound(new { message = $"No station '{id}'." });
    }

    private static async Task<IResult> ListStationSessionsAsync(
        Guid id,
        ClaimsPrincipal user,
        StationReads stations,
        SessionReads sessions,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        if (!await stations.ExistsForTenantAsync(id, tenantId, cancellationToken))
        {
            return Results.NotFound(new { message = $"No station '{id}'." });
        }

        var rows = await sessions.ListByStationAsync(id, cancellationToken);
        return Results.Ok(rows.Select(ApiMappings.ToSessionResponse));
    }

    private static async Task<IResult> ListInvoicesAsync(
        ClaimsPrincipal user,
        InvoiceReads invoices,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var rows = await invoices.ListByTenantAsync(tenantId, cancellationToken);
        return Results.Ok(rows.Select(ApiMappings.ToInvoiceResponse));
    }

    private static async Task<IResult> GetInvoiceAsync(
        Guid id,
        ClaimsPrincipal user,
        InvoiceReads invoices,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        return await invoices.FindForTenantAsync(id, tenantId, cancellationToken) is { } invoice
            ? Results.Ok(ApiMappings.ToInvoiceResponse(invoice))
            : Results.NotFound(new { message = $"No invoice '{id}'." });
    }

    private static async Task<IResult> ListTariffsAsync(
        ClaimsPrincipal user,
        TariffReads tariffs,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        var rows = await tariffs.ListAsync(tenantId, cancellationToken);
        return Results.Ok(rows.Select(ApiMappings.ToTariffResponse));
    }

    private static async Task<IResult> UpdateTariffAsync(
        Guid id,
        UpdateTariffRequest request,
        ClaimsPrincipal user,
        TariffEditing editing,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        UpdateTariffOutcome outcome;
        try
        {
            outcome = await editing.UpdateAsync(
                id,
                tenantId,
                new UpdateTariffCommand(
                    request.EnergyPricePerKwh,
                    request.StartFee,
                    request.IdleFeePerHour,
                    request.IdleGracePeriod),
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            });
        }

        return outcome switch
        {
            TariffUpdated updated => Results.Ok(ApiMappings.ToTariffResponse(updated.Tariff)),
            TariffUpdateMissing missing => Results.NotFound(new { message = $"No tariff '{missing.TariffId}'." }),
            _ => throw new InvalidOperationException("Unhandled tariff update outcome.")
        };
    }

    private static async Task<IResult> RemoteStartAsync(
        Guid id,
        RemoteStartRequest request,
        ClaimsPrincipal user,
        StationReads stations,
        OperatorCommands commands,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        if (!await stations.ExistsForTenantAsync(id, tenantId, cancellationToken))
        {
            return Results.NotFound(new { message = $"No station '{id}'." });
        }

        return RemoteCommandResults.Map(
            await commands.RemoteStartAsync(id, request.IdTag, request.ConnectorId, cancellationToken));
    }

    private static async Task<IResult> RemoteStopAsync(
        Guid id,
        ClaimsPrincipal user,
        SessionReads sessions,
        OperatorCommands commands,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId(user);
        if (!await sessions.ExistsForTenantAsync(id, tenantId, cancellationToken))
        {
            return Results.NotFound(new { message = $"No session '{id}'." });
        }

        return RemoteCommandResults.Map(await commands.RemoteStopAsync(id, cancellationToken));
    }

    /// <summary>The public status page: every station's name, OCPP identity, last-seen stamp and connector statuses.</summary>
    private static async Task<IResult> ListPublicStationsAsync(
        StationReads stations,
        ConnectorReads connectors,
        CancellationToken cancellationToken)
    {
        var rows = await stations.ListAsync(tenantId: null, cancellationToken);
        var connectorRows = await connectors.ListAllAsync(cancellationToken);
        var byStation = connectorRows
            .GroupBy(connector => connector.StationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConnectorResponse>)group.Select(ApiMappings.ToConnectorResponse).ToArray());

        return Results.Ok(rows.Select(station => ApiMappings.ToPublicStatusStationResponse(
            station,
            byStation.TryGetValue(station.Id, out var stationConnectors) ? stationConnectors : [])));
    }

    private static string TenantId(ClaimsPrincipal user)
        => user.FindFirstValue(UserClaimTypes.TenantId)
           ?? throw new InvalidOperationException("The signed-in user carries no tenant claim.");
}
