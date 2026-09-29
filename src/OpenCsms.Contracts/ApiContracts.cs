namespace OpenCsms.Contracts;

/// <summary>The pricing terms a new tariff is registered with; the defaults make a plain energy price.</summary>
public sealed record RegisterTariffRequest(
    string Name,
    decimal EnergyPricePerKwh,
    decimal StartFee = 0m,
    decimal IdleFeePerHour = 0m,
    TimeSpan? IdleGracePeriod = null,
    string? Currency = null);

/// <summary>The charge point identity, connector count and tariff a new station is registered under.</summary>
public sealed record RegisterStationRequest(
    string ChargePointId,
    string Name,
    int ConnectorCount,
    Guid TariffId);

/// <summary>The operator starts a session on one of a station's connectors.</summary>
public sealed record StartSessionRequest(Guid StationId, int ConnectorId);

/// <summary>The connector's cumulative meter reading; the session bills what rose above its start.</summary>
public sealed record MeterValueRequest(decimal TotalKwh);

/// <summary>The operator asks a connected charge point to start a transaction.</summary>
public sealed record RemoteStartRequest(string IdTag, int? ConnectorId = null);

/// <summary>A tariff as the operator's dashboard shows it.</summary>
public sealed record TariffResponse(
    Guid Id,
    string TenantId,
    string Name,
    decimal EnergyPricePerKwh,
    decimal StartFee,
    decimal IdleFeePerHour,
    TimeSpan IdleGracePeriod,
    string Currency);

/// <summary>A station as the operator's API returns it, with the last time its charge point was seen.</summary>
public sealed record StationResponse(
    Guid Id,
    string TenantId,
    string ChargePointId,
    string Name,
    int ConnectorCount,
    Guid TariffId,
    DateTimeOffset? LastSeenAtUtc);

/// <summary>The last status the charge point reported for one connector, for the operator view.</summary>
public sealed record ConnectorResponse(
    int ConnectorId,
    string Status,
    string ErrorCode,
    DateTimeOffset UpdatedAtUtc);

/// <summary>A charging session and where it stands; an open one reports no end and no energy yet.</summary>
public sealed record SessionResponse(
    Guid Id,
    int TransactionId,
    string TenantId,
    Guid StationId,
    int ConnectorId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    decimal EnergyKwh,
    bool IsOpen);

/// <summary>The device's authorization decision for a server-initiated call.</summary>
public sealed record RemoteCommandResponse(string Status);

/// <summary>A billed session: what the tariff terms charged, and when the invoice was issued.</summary>
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
    DateTimeOffset IssuedAtUtc);

/// <summary>A sign-in attempt against the dashboard's own account store.</summary>
public sealed record SignInRequest(string? Email, string? Password);

/// <summary>Provisions a dashboard account for the credential's tenant; the bootstrap path until user management has a screen.</summary>
public sealed record CreateUserRequest(
    string? Email,
    string? DisplayName,
    string? Password,
    string? Role);

/// <summary>Reprices a tariff; stored invoices keep the prices they were billed at.</summary>
public sealed record UpdateTariffRequest(
    decimal EnergyPricePerKwh,
    decimal StartFee,
    decimal IdleFeePerHour,
    TimeSpan IdleGracePeriod);

/// <summary>A dashboard account, as the operator's API returns it.</summary>
public sealed record UserResponse(
    Guid Id,
    string TenantId,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAtUtc);

/// <summary>Registers a tenant; the tenant id and the machine key are the product's to choose.</summary>
public sealed record RegisterTenantRequest(string? Name);

/// <summary>The registered tenant and its machine key, shown once; only the key's hash is stored.</summary>
public sealed record TenantRegistrationResponse(string TenantId, string Name, string ApiKey);

/// <summary>The signed-in user the SPA loads once per page; the cookie itself stays HttpOnly.</summary>
public sealed record UserSessionResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string TenantId,
    string Role);

/// <summary>A station as the signed-in operator's dashboard sees it; never another tenant's row.</summary>
public sealed record DashboardStationResponse(
    Guid Id,
    string Name,
    string ChargePointId,
    int ConnectorCount,
    Guid TariffId,
    DateTimeOffset? LastSeenAtUtc);

/// <summary>A station as the public status page shows it: no tenant, no tariff, no session detail.</summary>
public sealed record PublicStatusStationResponse(
    Guid Id,
    string Name,
    string ChargePointId,
    DateTimeOffset? LastSeenAtUtc,
    IReadOnlyList<ConnectorResponse> Connectors);
