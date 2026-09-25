namespace OpenCsms.Domain.Tests;

using OpenCsms.Domain;

[TestFixture]
public sealed class InvoiceCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static Tariff Tariff(
        decimal energyPrice = 0.40m,
        decimal startFee = 0m,
        decimal idleFee = 0m,
        TimeSpan? grace = null)
        => Domain.Tariff.Create("acme", "Standard", energyPrice, startFee, idleFee, grace);

    private static ChargingSession EndedSession(decimal kwh, TimeSpan duration, TimeSpan? idleTail = null)
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start);
        var lastMeterAt = idleTail is null ? Start + duration : Start + duration - idleTail.Value;
        session.RecordMeter(lastMeterAt, kwh);
        session.End(Start + duration);
        return session;
    }

    [Test]
    public void Calculate_ShouldBillEnergyAtTheTariffPrice()
    {
        var invoice = InvoiceCalculator.Calculate(EndedSession(22m, TimeSpan.FromHours(2)), Tariff(), Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.EnergyKwh, Is.EqualTo(22m));
            Assert.That(invoice.EnergyAmount, Is.EqualTo(8.80m));
            Assert.That(invoice.Total, Is.EqualTo(8.80m));
            Assert.That(invoice.Currency, Is.EqualTo("EUR"));
        });
    }

    [Test]
    public void Calculate_ShouldRoundEachComponentAwayFromZero()
    {
        // 3.333 kWh * 0.40 = 1.3332 -> 1.33; a start fee of 0.005 -> 0.01.
        var session = EndedSession(3.333m, TimeSpan.FromHours(1));
        var invoice = InvoiceCalculator.Calculate(session, Tariff(startFee: 0.005m), Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.EnergyAmount, Is.EqualTo(1.33m));
            Assert.That(invoice.StartFeeAmount, Is.EqualTo(0.01m));
            Assert.That(invoice.Total, Is.EqualTo(1.34m));
        });
    }

    [Test]
    public void Calculate_ShouldBillTheStartFeeOncePerSession()
    {
        var invoice = InvoiceCalculator.Calculate(EndedSession(10m, TimeSpan.FromMinutes(30)), Tariff(startFee: 1.50m), Start);

        Assert.That(invoice.StartFeeAmount, Is.EqualTo(1.50m));
        Assert.That(invoice.Total, Is.EqualTo(5.50m));
    }

    [Test]
    public void Calculate_ShouldNotBillIdleTimeWithinTheGracePeriod()
    {
        var session = EndedSession(10m, TimeSpan.FromMinutes(30), idleTail: TimeSpan.FromMinutes(9));
        var invoice = InvoiceCalculator.Calculate(session, Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)), Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.Zero);
            Assert.That(invoice.IdleFeeAmount, Is.Zero);
        });
    }

    [Test]
    public void Calculate_ShouldBillEveryStartedIdleHourBeyondTheGracePeriod()
    {
        // The last meter value is 35 minutes before the stop; 25 minutes are billable -> one started hour.
        var session = EndedSession(10m, TimeSpan.FromHours(2), idleTail: TimeSpan.FromMinutes(35));
        var invoice = InvoiceCalculator.Calculate(
            session,
            Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)),
            Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.EqualTo(1));
            Assert.That(invoice.IdleFeeAmount, Is.EqualTo(2m));
            Assert.That(invoice.Total, Is.EqualTo(6m));
        });
    }

    [Test]
    public void Calculate_ShouldRejectAnOpenSession()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start);

        Assert.That(
            () => InvoiceCalculator.Calculate(session, Tariff(), Start),
            Throws.InvalidOperationException.With.Message.Contains("ended session"));
    }

    [Test]
    public void Calculate_ShouldRejectATariffFromAnotherTenant()
    {
        var session = EndedSession(10m, TimeSpan.FromHours(1));
        var foreignTariff = Domain.Tariff.Create("other", "Foreign", 0.40m);

        Assert.That(
            () => InvoiceCalculator.Calculate(session, foreignTariff, Start),
            Throws.InvalidOperationException.With.Message.Contains("tenant"));
    }

    [Test]
    public void RecordMeter_ShouldRejectARegressingReading()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start);
        session.RecordMeter(Start + TimeSpan.FromMinutes(5), 5m);

        Assert.That(
            () => session.RecordMeter(Start + TimeSpan.FromMinutes(10), 4.9m),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("totalKwh"));
    }

    [Test]
    public void RecordMeter_ShouldRejectAReadingAfterTheSessionEnded()
    {
        var session = EndedSession(5m, TimeSpan.FromHours(1));

        Assert.That(
            () => session.RecordMeter(Start + TimeSpan.FromHours(2), 6m),
            Throws.InvalidOperationException.With.Message.Contains("stop transaction"));
    }

    [Test]
    public void End_ShouldRejectAnEndBeforeTheLastMeterValue()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start);
        session.RecordMeter(Start + TimeSpan.FromMinutes(30), 5m);

        Assert.That(
            () => session.End(Start + TimeSpan.FromMinutes(20)),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("atUtc"));
    }
}
