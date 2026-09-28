namespace OpenCsms.Domain.Tests;

using OpenCsms.Domain;

/// <summary>
/// The meter arithmetic a connector's register needs: the session starts at the register it found and
/// bills only what it added, so the second session on one connector does not inherit the first
/// session's energy. A stop is at-most-once for the session itself: a resend changes nothing.
/// </summary>
[TestFixture]
public sealed class ChargingSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static Tariff Tariff() => Domain.Tariff.Create("acme", "Standard", 0.40m);

    [Test]
    public void AddMeterValue_ShouldBillOnlyTheEnergyAboveTheStartReading()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff(), meterStartKwh: 22m);

        session.AddMeterValue(Start + TimeSpan.FromMinutes(5), 27m);

        Assert.Multiple(() =>
        {
            Assert.That(session.MeterStartKwh, Is.EqualTo(22m));
            Assert.That(session.EnergyKwh, Is.EqualTo(5m), "27 kWh register minus the 22 kWh start reading");
            Assert.That(session.LastReadingKwh, Is.EqualTo(27m));
        });
    }

    [Test]
    public void AddMeterValue_ShouldRejectAReadingBelowTheStartReading()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff(), meterStartKwh: 22m);

        Assert.That(
            () => session.AddMeterValue(Start + TimeSpan.FromMinutes(5), 21m),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("totalKwh"));
        Assert.That(session.EnergyKwh, Is.Zero, "the refused reading changed nothing");
    }

    [Test]
    public void Stop_ShouldIgnoreADuplicateStop()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff());

        Assert.Multiple(() =>
        {
            Assert.That(session.Stop(Start + TimeSpan.FromMinutes(10)), Is.True, "the first stop ends the session");
            Assert.That(session.Stop(Start + TimeSpan.FromHours(1)), Is.False, "the resend changed nothing");
            Assert.That(session.EndedAtUtc, Is.EqualTo(Start + TimeSpan.FromMinutes(10)));
        });
    }

    [Test]
    public void Start_ShouldRejectANegativeMeterStart()
    {
        Assert.That(
            () => ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff(), meterStartKwh: -1m),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("meterStartKwh"));
    }

    [Test]
    public void Start_ShouldRejectATariffFromAnotherTenant()
    {
        var foreignTariff = Domain.Tariff.Create("other", "Foreign", 0.40m);

        Assert.That(
            () => ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, foreignTariff),
            Throws.InvalidOperationException.With.Message.Contains("tenant"));
    }

    [Test]
    public void Start_ShouldCopyTheTariffsTermsSoALaterRepriceDoesNotReachTheSession()
    {
        var tariff = Domain.Tariff.Create("acme", "Standard", 0.40m, 1.50m, 2.00m, TimeSpan.FromMinutes(10));

        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, tariff);
        tariff.UpdatePricing(0.55m, 2.00m, 0m, TimeSpan.FromMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(session.Tariff.EnergyPricePerKwh, Is.EqualTo(0.40m), "the session keeps the price it started under");
            Assert.That(session.Tariff.StartFee, Is.EqualTo(1.50m));
            Assert.That(session.Tariff.IdleFeePerHour, Is.EqualTo(2.00m));
            Assert.That(session.Tariff.IdleGracePeriod, Is.EqualTo(TimeSpan.FromMinutes(10)));
        });
    }
}
