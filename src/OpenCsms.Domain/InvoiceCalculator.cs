namespace OpenCsms.Domain;

/// <summary>
/// Turns an ended session and its tariff into an invoice. The rule is deliberately small enough to
/// read in one place:
/// <list type="bullet">
/// <item>energy = the session's last meter reading times the tariff's price per kWh,</item>
/// <item>the start fee is billed once per session,</item>
/// <item>idle time runs from the last meter value to the stop transaction; every started hour beyond
/// the tariff's grace period is billed as an idle hour.</item>
/// </list>
/// Money is rounded to two decimals, away from zero, at each component and on the total.
/// </summary>
public static class InvoiceCalculator
{
    public static Invoice Calculate(ChargingSession session, Tariff tariff, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tariff);
        if (session.EndedAtUtc is null)
        {
            throw new InvalidOperationException("An invoice needs an ended session.");
        }

        if (!string.Equals(session.TenantId, tariff.TenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Session '{session.Id}' belongs to tenant '{session.TenantId}', not to tariff '{tariff.Id}'.");
        }

        var energyAmount = RoundMoney(session.EnergyKwh * tariff.EnergyPricePerKwh);
        var startFeeAmount = RoundMoney(tariff.StartFee);
        var idleHours = BillableIdleHours(session, tariff);
        var idleFeeAmount = RoundMoney(idleHours * tariff.IdleFeePerHour);
        var total = RoundMoney(energyAmount + startFeeAmount + idleFeeAmount);

        return Invoice.Create(
            session.Id,
            session.TenantId,
            session.EnergyKwh,
            energyAmount,
            startFeeAmount,
            idleHours,
            idleFeeAmount,
            total,
            tariff.Currency,
            issuedAtUtc);
    }

    /// <summary>The idle hours the tariff bills for: started hours after the grace period, or zero.</summary>
    public static int BillableIdleHours(ChargingSession session, Tariff tariff)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tariff);
        var endedAt = session.EndedAtUtc
            ?? throw new InvalidOperationException("Idle time is only known once the session has ended.");
        var idle = endedAt - session.LastMeterAtUtc - tariff.IdleGracePeriod;
        return idle <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(idle.TotalHours);
    }

    private static decimal RoundMoney(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
