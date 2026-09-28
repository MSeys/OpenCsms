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

    private static ChargingSession EndedSession(
        decimal kwh,
        TimeSpan duration,
        TimeSpan? idleTail = null,
        Tariff? tariff = null)
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, tariff ?? Tariff());
        var lastMeterAt = idleTail is null ? Start + duration : Start + duration - idleTail.Value;
        session.AddMeterValue(lastMeterAt, kwh);
        session.Stop(Start + duration);
        return session;
    }

    [Test]
    public void Calculate_ShouldBillEnergyAtTheTariffPrice()
    {
        var invoice = InvoiceCalculator.Calculate(EndedSession(22m, TimeSpan.FromHours(2)), Start);

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
        var session = EndedSession(3.333m, TimeSpan.FromHours(1), tariff: Tariff(startFee: 0.005m));
        var invoice = InvoiceCalculator.Calculate(session, Start);

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
        var session = EndedSession(10m, TimeSpan.FromMinutes(30), tariff: Tariff(startFee: 1.50m));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.That(invoice.StartFeeAmount, Is.EqualTo(1.50m));
        Assert.That(invoice.Total, Is.EqualTo(5.50m));
    }

    [Test]
    public void Calculate_ShouldNotBillIdleTimeWithinTheGracePeriod()
    {
        var session = EndedSession(
            10m,
            TimeSpan.FromMinutes(30),
            idleTail: TimeSpan.FromMinutes(9),
            tariff: Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)));
        var invoice = InvoiceCalculator.Calculate(session, Start);

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
        var session = EndedSession(
            10m,
            TimeSpan.FromHours(2),
            idleTail: TimeSpan.FromMinutes(35),
            tariff: Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.EqualTo(1));
            Assert.That(invoice.IdleFeeAmount, Is.EqualTo(2m));
            Assert.That(invoice.Total, Is.EqualTo(6m));
        });
    }

    [Test]
    public void Calculate_ShouldNotBillIdleTimeExactlyAtTheGracePeriod()
    {
        // The stop lands exactly on the grace boundary: only time past it is billable.
        var session = EndedSession(
            10m,
            TimeSpan.FromHours(2),
            idleTail: TimeSpan.FromMinutes(10),
            tariff: Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.Zero);
            Assert.That(invoice.IdleFeeAmount, Is.Zero);
        });
    }

    [Test]
    public void Calculate_ShouldBillOneStartedHourOneSecondPastTheGracePeriod()
    {
        // The boundary is exclusive: a second past the grace starts the first billable hour.
        var session = EndedSession(
            10m,
            TimeSpan.FromHours(2),
            idleTail: TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1),
            tariff: Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.EqualTo(1));
            Assert.That(invoice.IdleFeeAmount, Is.EqualTo(2m));
        });
    }

    [Test]
    public void Calculate_ShouldRoundBillableIdleUpToStartedHours()
    {
        // 2h05m past the grace period is three started hours, not two.
        var session = EndedSession(
            10m,
            TimeSpan.FromHours(4),
            idleTail: TimeSpan.FromMinutes(10) + TimeSpan.FromMinutes(125),
            tariff: Tariff(idleFee: 2m, grace: TimeSpan.FromMinutes(10)));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.EqualTo(3));
            Assert.That(invoice.IdleFeeAmount, Is.EqualTo(6m));
        });
    }

    [Test]
    public void Calculate_ShouldRoundTheIdleFeeAwayFromZero()
    {
        // One started hour at 2.005 rounds the component to 2.01 like every other money amount.
        var session = EndedSession(
            0m,
            TimeSpan.FromHours(1),
            idleTail: TimeSpan.FromMinutes(35),
            tariff: Tariff(idleFee: 2.005m, grace: TimeSpan.FromMinutes(10)));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.IdleHours, Is.EqualTo(1));
            Assert.That(invoice.IdleFeeAmount, Is.EqualTo(2.01m));
            Assert.That(invoice.Total, Is.EqualTo(2.01m));
        });
    }

    [Test]
    public void Calculate_ShouldBillThePricesTheSessionStartedUnderWhenTheTariffIsRepriced()
    {
        var tariff = Tariff(energyPrice: 0.40m, startFee: 1.50m, idleFee: 2.00m, grace: TimeSpan.FromMinutes(10));
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, tariff);
        session.AddMeterValue(Start + TimeSpan.FromMinutes(5), 22m);

        // The operator drops the idle fee while the session is open; the session keeps its own terms.
        tariff.UpdatePricing(0.55m, 3.00m, 0.00m, TimeSpan.FromMinutes(1));

        session.Stop(Start + TimeSpan.FromMinutes(5) + TimeSpan.FromHours(5) + TimeSpan.FromMinutes(1));
        var invoice = InvoiceCalculator.Calculate(session, Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.EnergyAmount, Is.EqualTo(8.80m), "22 kWh at the price the session started under");
            Assert.That(invoice.StartFeeAmount, Is.EqualTo(1.50m), "the start fee the session started under");
            Assert.That(invoice.IdleHours, Is.EqualTo(5), "five started hours past the ten-minute grace");
            Assert.That(invoice.IdleFeeAmount, Is.EqualTo(10.00m), "the idle fee the session started under");
            Assert.That(invoice.Total, Is.EqualTo(20.30m));
        });
    }

    [Test]
    public void Calculate_ShouldBillASessionStartedAfterARepriceAtTheNewPrices()
    {
        var tariff = Tariff(energyPrice: 0.40m, startFee: 1.50m, idleFee: 2.00m, grace: TimeSpan.FromMinutes(10));
        tariff.UpdatePricing(0.55m, 1.50m, 0.00m, TimeSpan.FromMinutes(10));

        var invoice = InvoiceCalculator.Calculate(EndedSession(22m, TimeSpan.FromHours(2), tariff: tariff), Start);

        Assert.Multiple(() =>
        {
            Assert.That(invoice.EnergyAmount, Is.EqualTo(12.10m), "22 kWh at the repriced 0.55");
            Assert.That(invoice.StartFeeAmount, Is.EqualTo(1.50m));
            Assert.That(invoice.Total, Is.EqualTo(13.60m));
        });
    }

    [Test]
    public void Calculate_ShouldLeaveAnIssuedInvoiceUntouchedByALaterReprice()
    {
        var tariff = Tariff(energyPrice: 0.40m, startFee: 1.50m);
        var invoice = InvoiceCalculator.Calculate(EndedSession(22m, TimeSpan.FromHours(2), tariff: tariff), Start);

        tariff.UpdatePricing(0.55m, 2.00m, 2.00m, TimeSpan.FromMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(invoice.EnergyAmount, Is.EqualTo(8.80m), "the issued invoice keeps the prices it was billed at");
            Assert.That(invoice.StartFeeAmount, Is.EqualTo(1.50m));
            Assert.That(invoice.Total, Is.EqualTo(10.30m));
        });
    }

    [Test]
    public void Calculate_ShouldRejectAnOpenSession()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff());

        Assert.That(
            () => InvoiceCalculator.Calculate(session, Start),
            Throws.InvalidOperationException.With.Message.Contains("ended session"));
    }

    [Test]
    public void AddMeterValue_ShouldRejectARegressingReading()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff());
        session.AddMeterValue(Start + TimeSpan.FromMinutes(5), 5m);

        Assert.That(
            () => session.AddMeterValue(Start + TimeSpan.FromMinutes(10), 4.9m),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("totalKwh"));
    }

    [Test]
    public void AddMeterValue_ShouldRejectAReadingAfterTheSessionEnded()
    {
        var session = EndedSession(5m, TimeSpan.FromHours(1));

        Assert.That(
            () => session.AddMeterValue(Start + TimeSpan.FromHours(2), 6m),
            Throws.InvalidOperationException.With.Message.Contains("stop transaction"));
    }

    [Test]
    public void Stop_ShouldRejectAnEndBeforeTheLastMeterValue()
    {
        var session = ChargingSession.Start("acme", Guid.NewGuid(), 1, Start, Tariff());
        session.AddMeterValue(Start + TimeSpan.FromMinutes(30), 5m);

        Assert.That(
            () => session.Stop(Start + TimeSpan.FromMinutes(20)),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("atUtc"));
    }
}
