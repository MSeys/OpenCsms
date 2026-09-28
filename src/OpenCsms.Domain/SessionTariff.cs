namespace OpenCsms.Domain;

/// <summary>
/// The tariff terms a charging session bills under, copied from the tariff when the session started.
/// A repricing replaces the tariff's own prices; a session keeps its copy, so billing never reaches
/// back into a tariff that was edited while the car was plugged in.
/// </summary>
public sealed class SessionTariff
{
    private SessionTariff()
    {
        Currency = "EUR";
    }

    private SessionTariff(
        decimal energyPricePerKwh,
        decimal startFee,
        decimal idleFeePerHour,
        TimeSpan idleGracePeriod,
        string currency)
    {
        EnergyPricePerKwh = energyPricePerKwh;
        StartFee = startFee;
        IdleFeePerHour = idleFeePerHour;
        IdleGracePeriod = idleGracePeriod;
        Currency = currency;
    }

    public decimal EnergyPricePerKwh { get; private set; }

    public decimal StartFee { get; private set; }

    public decimal IdleFeePerHour { get; private set; }

    public TimeSpan IdleGracePeriod { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Takes the tariff's terms as the session's own; the tariff keeps evolving, the copy does not.</summary>
    internal static SessionTariff From(Tariff tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        return new SessionTariff(
            tariff.EnergyPricePerKwh,
            tariff.StartFee,
            tariff.IdleFeePerHour,
            tariff.IdleGracePeriod,
            tariff.Currency);
    }
}
