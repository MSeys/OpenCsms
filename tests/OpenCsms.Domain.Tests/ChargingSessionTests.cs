namespace OpenCsms.Domain.Tests;

using OpenCsms.Domain;

/// <summary>
/// The meter arithmetic a connector's register needs: the session starts at the register it found and
/// bills only what it added, so the second session on one connector does not inherit the first
/// session's energy.
/// </summary>
[TestFixture]
public sealed class ChargingSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void RecordMeter_ShouldBillOnlyTheEnergyAboveTheStartReading()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, meterStartKwh: 22m);

        session.RecordMeter(Start + TimeSpan.FromMinutes(5), 27m);

        Assert.Multiple(() =>
        {
            Assert.That(session.MeterStartKwh, Is.EqualTo(22m));
            Assert.That(session.EnergyKwh, Is.EqualTo(5m), "27 kWh register minus the 22 kWh start reading");
            Assert.That(session.LastReadingKwh, Is.EqualTo(27m));
        });
    }

    [Test]
    public void RecordMeter_ShouldRejectAReadingBelowTheStartReading()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, meterStartKwh: 22m);

        Assert.That(
            () => session.RecordMeter(Start + TimeSpan.FromMinutes(5), 21m),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("totalKwh"));
        Assert.That(session.EnergyKwh, Is.Zero, "the refused reading changed nothing");
    }

    [Test]
    public void Start_ShouldRejectANegativeMeterStart()
    {
        Assert.That(
            () => ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, meterStartKwh: -1m),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("meterStartKwh"));
    }
}
