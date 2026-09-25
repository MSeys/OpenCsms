namespace OpenCsms.Domain;

/// <summary>The bill for one ended session: energy, start fee and idle fee, in the tariff's currency.</summary>
public sealed class Invoice
{
    private Invoice()
    {
        TenantId = string.Empty;
        Currency = "EUR";
    }

    private Invoice(
        Guid id,
        Guid sessionId,
        string tenantId,
        decimal energyKwh,
        decimal energyAmount,
        decimal startFeeAmount,
        int idleHours,
        decimal idleFeeAmount,
        decimal total,
        string currency,
        DateTimeOffset issuedAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        TenantId = tenantId;
        EnergyKwh = energyKwh;
        EnergyAmount = energyAmount;
        StartFeeAmount = startFeeAmount;
        IdleHours = idleHours;
        IdleFeeAmount = idleFeeAmount;
        Total = total;
        Currency = currency;
        IssuedAtUtc = issuedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public string TenantId { get; private set; }

    public decimal EnergyKwh { get; private set; }

    public decimal EnergyAmount { get; private set; }

    public decimal StartFeeAmount { get; private set; }

    /// <summary>Billable idle hours after the tariff's grace period, rounded up per started hour.</summary>
    public int IdleHours { get; private set; }

    public decimal IdleFeeAmount { get; private set; }

    public decimal Total { get; private set; }

    public string Currency { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    internal static Invoice Create(
        Guid sessionId,
        string tenantId,
        decimal energyKwh,
        decimal energyAmount,
        decimal startFeeAmount,
        int idleHours,
        decimal idleFeeAmount,
        decimal total,
        string currency,
        DateTimeOffset issuedAtUtc)
        => new(
            Guid.NewGuid(),
            sessionId,
            tenantId,
            energyKwh,
            energyAmount,
            startFeeAmount,
            idleHours,
            idleFeeAmount,
            total,
            currency,
            issuedAtUtc);
}
