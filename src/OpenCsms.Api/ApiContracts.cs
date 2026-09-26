namespace OpenCsms.Api;

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
