namespace OpenCsms.Domain.Tests;

using System.Text.Json;
using OpenCsms.Domain.Ocpp;

[TestFixture]
public sealed class OcppProtocolTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void SerializeCall_ShouldWriteTheCallFrame()
    {
        var json = OcppJson.SerializeCall("1", OcppActions.BootNotification, new BootNotificationRequest("ACME", "M1"));

        Assert.That(
            json,
            Is.EqualTo("[2,\"1\",\"BootNotification\",{\"chargePointVendor\":\"ACME\",\"chargePointModel\":\"M1\"}]"));
    }

    [Test]
    public void SerializeCallError_ShouldWriteTheErrorFrame()
    {
        var json = OcppJson.SerializeCallError("7", OcppErrorCodes.NotImplemented, "the CSMS does not implement it");

        Assert.That(json, Is.EqualTo("[4,\"7\",\"NotImplemented\",\"the CSMS does not implement it\",{}]"));
    }

    [Test]
    public void Parse_ShouldReadACallAndItsTypedPayload()
    {
        var message = OcppJson.Parse(
            "[2,\"9\",\"StartTransaction\",{\"connectorId\":1,\"idTag\":\"card-42\",\"meterStart\":0,\"timestamp\":\"2030-06-15T12:00:00+00:00\"}]");

        var call = (OcppCall)message;
        var request = OcppJson.ReadPayload<StartTransactionRequest>(call.Payload);
        Assert.Multiple(() =>
        {
            Assert.That(call.Action, Is.EqualTo(OcppActions.StartTransaction));
            Assert.That(call.MessageId, Is.EqualTo("9"));
            Assert.That(request.ConnectorId, Is.EqualTo(1));
            Assert.That(request.IdTag, Is.EqualTo("card-42"));
            Assert.That(request.MeterStart, Is.Zero);
        });
    }

    [Test]
    public void Parse_ShouldReadAResultAndAnError()
    {
        var result = (OcppCallResult)OcppJson.Parse(
            "[3,\"4\",{\"transactionId\":12,\"idTagInfo\":{\"status\":\"Accepted\"}}]");
        var response = OcppJson.ReadPayload<StartTransactionResponse>(result.Payload);

        var error = (OcppCallError)OcppJson.Parse("[4,\"4\",\"PropertyConstraintViolation\",\"no connector 9\",{}]");
        Assert.Multiple(() =>
        {
            Assert.That(response.TransactionId, Is.EqualTo(12));
            Assert.That(response.IdTagInfo.Status, Is.EqualTo(OcppAuthorizationStatus.Accepted));
            Assert.That(error.ErrorCode, Is.EqualTo(OcppErrorCodes.PropertyConstraintViolation));
            Assert.That(error.Description, Is.EqualTo("no connector 9"));
        });
    }

    [TestCase("not json")]
    [TestCase("{\"type\":2}")]
    [TestCase("[1,\"1\",{}]")]
    [TestCase("[2,\"1\"]")]
    [TestCase("[2,\"1\",\"Heartbeat\",{\"vendor\":true},")]
    [TestCase("[2,7,\"Heartbeat\",{}]")]
    public void Parse_ShouldRejectFramesThatAreNotOcpp(string frame)
    {
        Assert.Multiple(() =>
        {
            Assert.That(OcppJson.TryParse(frame, out _), Is.False, "TryParse reports the malformed frame");
            Assert.Throws<OcppProtocolException>(() => OcppJson.Parse(frame), "Parse throws instead of half-reading");
        });
    }

    [Test]
    public void ReadPayload_ShouldUseTheOcppWireNames()
    {
        var json = OcppJson.SerializeCall(
            "1",
            OcppActions.StatusNotification,
            new StatusNotificationRequest(2, ConnectorStatus.SuspendedEVSE, ChargePointErrorCode.OverVoltage, Timestamp: Now));

        var request = OcppJson.ReadPayload<StatusNotificationRequest>(((OcppCall)OcppJson.Parse(json)).Payload);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"status\":\"SuspendedEVSE\""));
            Assert.That(json, Does.Contain("\"errorCode\":\"OverVoltage\""));
            Assert.That(request.Status, Is.EqualTo(ConnectorStatus.SuspendedEVSE));
            Assert.That(request.ErrorCode, Is.EqualTo(ChargePointErrorCode.OverVoltage));
            Assert.That(request.Timestamp, Is.EqualTo(Now));
        });
    }

    [Test]
    public void ReadPayload_ShouldRejectAMemberOutsideTheContract()
    {
        var payload = ((OcppCall)OcppJson.Parse("[2,\"1\",\"Heartbeat\",{\"vendorExtension\":true}]")).Payload;

        Assert.Throws<JsonException>(() => OcppJson.ReadPayload<HeartbeatRequest>(payload));
    }

    [Test]
    public void OcppEnergy_ShouldReadWattHoursAndKilowattHours()
    {
        var wattHours = OcppEnergy.ReadKwh(
            [new MeterValueSample(Now, [new SampledValue("22000", Measurand: OcppEnergy.ActiveImportRegister, Unit: "Wh")])]);
        var kilowattHours = OcppEnergy.ReadKwh(
            [new MeterValueSample(Now, [new SampledValue("22", Unit: "kWh")])]);
        var defaulted = OcppEnergy.ReadKwh(
            [new MeterValueSample(Now, [new SampledValue("1500")])]);

        Assert.Multiple(() =>
        {
            Assert.That(wattHours, Is.EqualTo(22m));
            Assert.That(kilowattHours, Is.EqualTo(22m));
            Assert.That(defaulted, Is.EqualTo(1.5m), "the OCPP defaults are the active import register in Wh");
        });
    }

    [TestCase("nope")]
    [TestCase("-1")]
    [TestCase("")]
    public void OcppEnergy_ShouldRejectAnUnreadableReading(string value)
    {
        var meterValues = new[]
        {
            new MeterValueSample(Now, [new SampledValue(value, Measurand: OcppEnergy.ActiveImportRegister, Unit: "Wh")])
        };

        Assert.Throws<OcppProtocolException>(() => OcppEnergy.ReadKwh(meterValues));
    }

    [Test]
    public void OcppEnergy_ShouldRejectValuesWithoutAnEnergySample()
    {
        var meterValues = new[]
        {
            new MeterValueSample(Now, [new SampledValue("7", Measurand: "Power.Active.Import", Unit: "W")])
        };

        Assert.Throws<OcppProtocolException>(() => OcppEnergy.ReadKwh(meterValues));
    }
}
