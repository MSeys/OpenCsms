namespace OpenCsms.Api;

using System.Security.Claims;
using OpenCsms.Application.Ports;
using OpenCsms.Contracts;
using OpenCsms.Domain;

/// <summary>
/// The presentation layer's one mapping from a domain or application value to the DTO the wire
/// carries. The DTOs themselves live in <c>OpenCsms.Contracts</c> with no dependencies; the mapping
/// reads the domain and the application ports, so it stays here in the thin API composition root.
/// </summary>
internal static class ApiMappings
{
    public static TariffResponse ToTariffResponse(Tariff tariff)
        => new(
            tariff.Id,
            tariff.TenantId,
            tariff.Name,
            tariff.EnergyPricePerKwh,
            tariff.StartFee,
            tariff.IdleFeePerHour,
            tariff.IdleGracePeriod,
            tariff.Currency);

    public static StationResponse ToStationResponse(Station station)
        => new(
            station.Id,
            station.TenantId,
            station.ChargePointId,
            station.Name,
            station.ConnectorCount,
            station.TariffId,
            station.LastSeenAtUtc);

    public static ConnectorResponse ToConnectorResponse(Connector connector)
        => new(connector.ConnectorId, connector.Status.ToString(), connector.ErrorCode.ToString(), connector.UpdatedAtUtc);

    public static SessionResponse ToSessionResponse(ChargingSession session)
        => new(
            session.Id,
            session.TransactionId,
            session.TenantId,
            session.StationId,
            session.ConnectorId,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.EnergyKwh,
            session.IsOpen);

    public static RemoteCommandResponse ToRemoteCommandResponse(AuthorizationStatus status)
        => new(status.ToString());

    public static InvoiceResponse ToInvoiceResponse(Invoice invoice)
        => new(
            invoice.Id,
            invoice.SessionId,
            invoice.TenantId,
            invoice.EnergyKwh,
            invoice.EnergyAmount,
            invoice.StartFeeAmount,
            invoice.IdleHours,
            invoice.IdleFeeAmount,
            invoice.Total,
            invoice.Currency,
            invoice.IssuedAtUtc);

    public static UserResponse ToUserResponse(User user)
        => new(user.Id, user.TenantId, user.Email, user.DisplayName, UserRoles.From(user.Role), user.CreatedAtUtc);

    public static UserSessionResponse ToUserSessionResponse(User user)
        => new(user.Id, user.Email, user.DisplayName, user.TenantId, UserRoles.From(user.Role));

    public static UserSessionResponse ToUserSessionResponse(ClaimsPrincipal principal)
        => new(
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? id
                : Guid.Empty,
            principal.FindFirstValue(UserClaimTypes.Email) ?? string.Empty,
            principal.Identity?.Name ?? string.Empty,
            principal.FindFirstValue(UserClaimTypes.TenantId) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty);

    public static DashboardStationResponse ToDashboardStationResponse(Station station)
        => new(
            station.Id,
            station.Name,
            station.ChargePointId,
            station.ConnectorCount,
            station.TariffId,
            station.LastSeenAtUtc);

    public static PublicStatusStationResponse ToPublicStatusStationResponse(
        Station station,
        IReadOnlyList<ConnectorResponse> connectors)
        => new(station.Id, station.Name, station.ChargePointId, station.LastSeenAtUtc, connectors);
}
