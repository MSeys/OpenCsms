namespace OpenCsms.Domain.Tests;

using NUnit.Framework;

/// <summary>
/// The tariff's repricing rules: what an operator admin may change, what stays fixed, and what the
/// domain refuses. Sessions already billed keep their stored invoices; only sessions billed after a
/// repricing use the new prices.
/// </summary>
[TestFixture]
public sealed class TariffPricingTests
{
    [Test]
    public void UpdatingThePricingReplacesAllFourPrices()
    {
        var tariff = Tariff.Create("op", "Standard", 0.40m, 1.50m, 2.00m, TimeSpan.FromMinutes(10));

        tariff.UpdatePricing(0.55m, 2.00m, 3.00m, TimeSpan.FromMinutes(30));

        Assert.Multiple(() =>
        {
            Assert.That(tariff.EnergyPricePerKwh, Is.EqualTo(0.55m));
            Assert.That(tariff.StartFee, Is.EqualTo(2.00m));
            Assert.That(tariff.IdleFeePerHour, Is.EqualTo(3.00m));
            Assert.That(tariff.IdleGracePeriod, Is.EqualTo(TimeSpan.FromMinutes(30)));
        });
    }

    [Test]
    public void UpdatingThePricingKeepsIdentityTenantNameAndCurrency()
    {
        var tariff = Tariff.Create("op", "Standard", 0.40m, 1.50m, 2.00m, TimeSpan.FromMinutes(10), "EUR");

        tariff.UpdatePricing(0.55m, 2.00m, 3.00m, TimeSpan.FromMinutes(30));

        Assert.Multiple(() =>
        {
            Assert.That(tariff.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(tariff.TenantId, Is.EqualTo("op"));
            Assert.That(tariff.Name, Is.EqualTo("Standard"));
            Assert.That(tariff.Currency, Is.EqualTo("EUR"));
        });
    }

    [TestCase(-0.01)]
    public void ANegativeEnergyPriceIsRefused(decimal energyPricePerKwh)
    {
        var tariff = Tariff.Create("op", "Standard", 0.40m);

        Assert.That(
            () => tariff.UpdatePricing(energyPricePerKwh, 0m, 0m, TimeSpan.Zero),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(tariff.EnergyPricePerKwh, Is.EqualTo(0.40m), "the refused update changed nothing");
    }

    [Test]
    public void NegativeFeesAndGraceAreRefused()
    {
        var tariff = Tariff.Create("op", "Standard", 0.40m, 1.50m, 2.00m, TimeSpan.FromMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(() => tariff.UpdatePricing(0.40m, -1m, 2.00m, TimeSpan.FromMinutes(10)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => tariff.UpdatePricing(0.40m, 1.50m, -1m, TimeSpan.FromMinutes(10)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => tariff.UpdatePricing(0.40m, 1.50m, 2.00m, TimeSpan.FromMinutes(-1)), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
        Assert.Multiple(() =>
        {
            Assert.That(tariff.StartFee, Is.EqualTo(1.50m), "the refused updates changed nothing");
            Assert.That(tariff.IdleFeePerHour, Is.EqualTo(2.00m));
            Assert.That(tariff.IdleGracePeriod, Is.EqualTo(TimeSpan.FromMinutes(10)));
        });
    }
}
