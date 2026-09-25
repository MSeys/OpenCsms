namespace OpenCsms.Domain;

/// <summary>
/// What an operator charges for energy, a session start, and occupying a connector after the last
/// meter value. The idle fee is billed per started hour after <see cref="IdleGracePeriod"/> - the
/// rule the M2 idle-fee journey advances the clock to prove.
/// </summary>
public sealed class Tariff
{
    private Tariff()
    {
        TenantId = string.Empty;
        Name = string.Empty;
        Currency = "EUR";
    }

    private Tariff(
        Guid id,
        string tenantId,
        string name,
        decimal energyPricePerKwh,
        decimal startFee,
        decimal idleFeePerHour,
        TimeSpan idleGracePeriod,
        string currency)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        EnergyPricePerKwh = energyPricePerKwh;
        StartFee = startFee;
        IdleFeePerHour = idleFeePerHour;
        IdleGracePeriod = idleGracePeriod;
        Currency = currency;
    }

    public Guid Id { get; private set; }

    public string TenantId { get; private set; }

    public string Name { get; private set; }

    public decimal EnergyPricePerKwh { get; private set; }

    public decimal StartFee { get; private set; }

    public decimal IdleFeePerHour { get; private set; }

    public TimeSpan IdleGracePeriod { get; private set; }

    public string Currency { get; private set; }

    public static Tariff Create(
        string tenantId,
        string name,
        decimal energyPricePerKwh,
        decimal startFee = 0m,
        decimal idleFeePerHour = 0m,
        TimeSpan? idleGracePeriod = null,
        string currency = "EUR")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (energyPricePerKwh < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(energyPricePerKwh), energyPricePerKwh, "Energy has no negative price.");
        }

        if (startFee < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(startFee), startFee, "A start fee cannot be negative.");
        }

        if (idleFeePerHour < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(idleFeePerHour), idleFeePerHour, "An idle fee cannot be negative.");
        }

        var grace = idleGracePeriod ?? TimeSpan.Zero;
        if (grace < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleGracePeriod), grace, "An idle grace period cannot be negative.");
        }

        if (currency.Length != 3)
        {
            throw new ArgumentException("A currency is a three-letter code.", nameof(currency));
        }

        return new Tariff(Guid.NewGuid(), tenantId, name, energyPricePerKwh, startFee, idleFeePerHour, grace, currency.ToUpperInvariant());
    }
}
