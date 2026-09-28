namespace OpenCsms.Domain;

/// <summary>
/// Turns an ended session and the tariff terms it started under into an invoice. The rule is
/// deliberately small enough to read in one place:
/// <list type="bullet">
/// <item>energy = the session's last meter reading times the session tariff's price per kWh,</item>
/// <item>the start fee is billed once per session,</item>
/// <item>idle time runs from the last meter value to the stop transaction; every started hour beyond
/// the session tariff's grace period is billed as an idle hour.</item>
/// </list>
/// The terms come from the session's own snapshot, never from the tariff row, so a repricing while
/// the session was open cannot change its bill. Money is rounded to two decimals, away from zero, at
/// each component and on the total.
/// </summary>
public static class InvoiceCalculator
{
    public static Invoice Calculate(ChargingSession session, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.EndedAtUtc is null)
        {
            throw new InvalidOperationException("An invoice needs an ended session.");
        }

        var tariff = session.Tariff;
        var energyAmount = RoundMoney(session.EnergyKwh * tariff.EnergyPricePerKwh);
        var startFeeAmount = RoundMoney(tariff.StartFee);
        var idleHours = BillableIdleHours(session);
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

    /// <summary>The idle hours the session tariff bills for: started hours after the grace period, or zero.</summary>
    public static int BillableIdleHours(ChargingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var endedAt = session.EndedAtUtc
            ?? throw new InvalidOperationException("Idle time is only known once the session has ended.");
        var idle = endedAt - session.LastMeterAtUtc - session.Tariff.IdleGracePeriod;
        return idle <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(idle.TotalHours);
    }

    private static decimal RoundMoney(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
