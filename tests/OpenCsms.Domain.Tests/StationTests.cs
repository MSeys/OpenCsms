namespace OpenCsms.Domain.Tests;

using OpenCsms.Domain;
using OpenCsms.Domain.Ocpp;

[TestFixture]
public sealed class StationTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Register_ShouldRequireAChargePointId()
    {
        Assert.Throws<ArgumentException>(() => Station.Register("acme", " ", "Depot A", 2, Guid.NewGuid()));
    }

    [Test]
    public void MarkSeen_ShouldStampTheLastSeenInstant()
    {
        var station = Station.Register("acme", "cp-1", "Depot A", 2, Guid.NewGuid());

        station.MarkSeen(Now);

        Assert.That(station.LastSeenAtUtc, Is.EqualTo(Now));
    }
}

[TestFixture]
public sealed class ConnectorTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Report_ShouldRejectANegativeConnectorId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Connector.Report(Guid.NewGuid(), -1, ConnectorStatus.Available, ChargePointErrorCode.NoError, Now));
    }

    [Test]
    public void Update_ShouldApplyALaterStatus()
    {
        var connector = Connector.Report(
            Guid.NewGuid(),
            1,
            ConnectorStatus.Preparing,
            ChargePointErrorCode.NoError,
            Now);

        connector.Update(ConnectorStatus.Charging, ChargePointErrorCode.OverVoltage, Now.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(connector.ConnectorId, Is.EqualTo(1));
            Assert.That(connector.Status, Is.EqualTo(ConnectorStatus.Charging));
            Assert.That(connector.ErrorCode, Is.EqualTo(ChargePointErrorCode.OverVoltage));
            Assert.That(connector.UpdatedAtUtc, Is.EqualTo(Now.AddMinutes(1)));
        });
    }
}
