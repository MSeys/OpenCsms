namespace OpenCsms.Api;

using System.Security.Claims;
using OpenCsms.Domain;
using OpenCsms.Domain.Ocpp;

public sealed record RegisterTariffRequest(
    string TenantId,
    string Name,
    decimal EnergyPricePerKwh,
    decimal StartFee = 0m,
    decimal IdleFeePerHour = 0m,
    TimeSpan? IdleGracePeriod = null,
    string? Currency = null);

public sealed record RegisterStationRequest(
    string TenantId,
    string ChargePointId,
    string Name,
    int ConnectorCount,
    Guid TariffId);

public sealed record StartSessionRequest(Guid StationId, int ConnectorId);

public sealed record MeterValueRequest(decimal TotalKwh);

/// <summary>The operator asks a connected charge point to start a transaction.</summary>
public sealed record RemoteStartRequest(string IdTag, int? ConnectorId = null);

public sealed record TariffResponse(
    Guid Id,
    string TenantId,
    string Name,
    decimal EnergyPricePerKwh,
    decimal StartFee,
    decimal IdleFeePerHour,
    TimeSpan IdleGracePeriod,
    string Currency)
{
    public static TariffResponse From(Tariff tariff)
        => new(
            tariff.Id,
            tariff.TenantId,
            tariff.Name,
            tariff.EnergyPricePerKwh,
            tariff.StartFee,
            tariff.IdleFeePerHour,
            tariff.IdleGracePeriod,
            tariff.Currency);
}

public sealed record StationResponse(
    Guid Id,
    string TenantId,
    string ChargePointId,
    string Name,
    int ConnectorCount,
    Guid TariffId,
    DateTimeOffset? LastSeenAtUtc)
{
    public static StationResponse From(Station station)
        => new(
            station.Id,
            station.TenantId,
            station.ChargePointId,
            station.Name,
            station.ConnectorCount,
            station.TariffId,
            station.LastSeenAtUtc);
}

/// <summary>The last status the charge point reported for one connector, for the operator view.</summary>
public sealed record ConnectorResponse(
    int ConnectorId,
    string Status,
    string ErrorCode,
    DateTimeOffset UpdatedAtUtc)
{
    public static ConnectorResponse From(Connector connector)
        => new(connector.ConnectorId, connector.Status.ToString(), connector.ErrorCode.ToString(), connector.UpdatedAtUtc);
}

public sealed record SessionResponse(
    Guid Id,
    int TransactionId,
    string TenantId,
    Guid StationId,
    int ConnectorId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    decimal EnergyKwh,
    bool IsOpen)
{
    public static SessionResponse From(ChargingSession session)
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
}

/// <summary>The device's authorization decision for a server-initiated call.</summary>
public sealed record RemoteCommandResponse(string Status)
{
    public static RemoteCommandResponse From(IdTagInfo info)
        => new(info.Status.ToString());
}

public sealed record InvoiceResponse(
    Guid Id,
    Guid SessionId,
    string TenantId,
    decimal EnergyKwh,
    decimal EnergyAmount,
    decimal StartFeeAmount,
    int IdleHours,
    decimal IdleFeeAmount,
    decimal Total,
    string Currency,
    DateTimeOffset IssuedAtUtc)
{
    public static InvoiceResponse From(Invoice invoice)
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
}

/// <summary>A sign-in attempt against the dashboard's own account store.</summary>
public sealed record SignInRequest(string? Email, string? Password);

/// <summary>Provisions a dashboard account for a tenant; the bootstrap path until user management has a screen.</summary>
public sealed record CreateUserRequest(
    string? TenantId,
    string? Email,
    string? DisplayName,
    string? Password,
    string? Role);

public sealed record UserResponse(
    Guid Id,
    string TenantId,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAtUtc)
{
    public static UserResponse From(User user)
        => new(user.Id, user.TenantId, user.Email, user.DisplayName, UserRoles.From(user.Role), user.CreatedAtUtc);
}

/// <summary>The signed-in user the SPA loads once per page; the cookie itself stays HttpOnly.</summary>
public sealed record UserSessionResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string TenantId,
    string Role)
{
    public static UserSessionResponse From(User user)
        => new(user.Id, user.Email, user.DisplayName, user.TenantId, UserRoles.From(user.Role));

    public static UserSessionResponse FromPrincipal(ClaimsPrincipal principal)
        => new(
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? id
                : Guid.Empty,
            principal.FindFirstValue(UserClaimTypes.Email) ?? string.Empty,
            principal.Identity?.Name ?? string.Empty,
            principal.FindFirstValue(UserClaimTypes.TenantId) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty);
}

/// <summary>A station as the signed-in operator's dashboard sees it; never another tenant's row.</summary>
public sealed record DashboardStationResponse(
    Guid Id,
    string Name,
    string ChargePointId,
    int ConnectorCount,
    Guid TariffId,
    DateTimeOffset? LastSeenAtUtc)
{
    public static DashboardStationResponse From(Station station)
        => new(
            station.Id,
            station.Name,
            station.ChargePointId,
            station.ConnectorCount,
            station.TariffId,
            station.LastSeenAtUtc);
}

/// <summary>A station as the public status page shows it: no tenant, no tariff, no session detail.</summary>
public sealed record PublicStatusStationResponse(
    Guid Id,
    string Name,
    string ChargePointId,
    DateTimeOffset? LastSeenAtUtc,
    IReadOnlyList<ConnectorResponse> Connectors)
{
    public static PublicStatusStationResponse From(Station station, IReadOnlyList<ConnectorResponse> connectors)
        => new(station.Id, station.Name, station.ChargePointId, station.LastSeenAtUtc, connectors);
}
