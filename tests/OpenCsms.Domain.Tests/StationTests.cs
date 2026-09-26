namespace OpenCsms.Domain.Tests;

using OpenCsms.Domain;

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

    [Test]
    public void CanServeTransaction_ShouldRefuseTheChargePointItself()
    {
        var station = Station.Register("acme", "cp-1", "Depot A", 2, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(station.CanServeTransaction(0), Is.False, "connector 0 is the charge point itself");
            Assert.That(station.CanServeTransaction(1), Is.True);
            Assert.That(station.CanServeTransaction(2), Is.True);
            Assert.That(station.CanServeTransaction(3), Is.False);
        });
    }

    [Test]
    public void UpsertConnector_ShouldCreateOnFirstReportAndUpdateAfterwards()
    {
        var station = Station.Register("acme", "cp-1", "Depot A", 2, Guid.NewGuid());

        var created = station.UpsertConnector(null, 1, ConnectorStatus.Preparing, ConnectorErrorCode.NoError, Now);
        var updated = station.UpsertConnector(
            created,
            1,
            ConnectorStatus.Charging,
            ConnectorErrorCode.OverVoltage,
            Now.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(created.StationId, Is.EqualTo(station.Id));
            Assert.That(created.ConnectorId, Is.EqualTo(1));
            Assert.That(ReferenceEquals(updated, created), Is.True, "an existing connector is updated in place");
            Assert.That(updated.Status, Is.EqualTo(ConnectorStatus.Charging));
            Assert.That(updated.ErrorCode, Is.EqualTo(ConnectorErrorCode.OverVoltage));
            Assert.That(updated.UpdatedAtUtc, Is.EqualTo(Now.AddMinutes(1)));
        });
    }

    [Test]
    public void UpsertConnector_ShouldRejectAConnectorOutsideTheStation()
    {
        var station = Station.Register("acme", "cp-1", "Depot A", 2, Guid.NewGuid());

        Assert.That(
            () => station.UpsertConnector(null, 3, ConnectorStatus.Available, ConnectorErrorCode.NoError, Now),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("connectorId"));
    }

    [Test]
    public void UpsertConnector_ShouldRejectAnotherStationsConnector()
    {
        var station = Station.Register("acme", "cp-1", "Depot A", 2, Guid.NewGuid());
        var foreign = Connector.Report(Guid.NewGuid(), 1, ConnectorStatus.Available, ConnectorErrorCode.NoError, Now);

        Assert.That(
            () => station.UpsertConnector(foreign, 1, ConnectorStatus.Charging, ConnectorErrorCode.NoError, Now),
            Throws.ArgumentException);
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
            () => Connector.Report(Guid.NewGuid(), -1, ConnectorStatus.Available, ConnectorErrorCode.NoError, Now));
    }

    [Test]
    public void Update_ShouldApplyALaterStatus()
    {
        var connector = Connector.Report(
            Guid.NewGuid(),
            1,
            ConnectorStatus.Preparing,
            ConnectorErrorCode.NoError,
            Now);

        connector.Update(ConnectorStatus.Charging, ConnectorErrorCode.OverVoltage, Now.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(connector.ConnectorId, Is.EqualTo(1));
            Assert.That(connector.Status, Is.EqualTo(ConnectorStatus.Charging));
            Assert.That(connector.ErrorCode, Is.EqualTo(ConnectorErrorCode.OverVoltage));
            Assert.That(connector.UpdatedAtUtc, Is.EqualTo(Now.AddMinutes(1)));
        });
    }
}
