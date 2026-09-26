namespace OpenCsms.Protocol.Ocpp;

using System.Text.Json;

/// <summary>
/// The three message types of the OCPP 1.6J JSON framing: a call, its result, or an error answering it.
/// </summary>
public enum OcppMessageType
{
    /// <summary>A request: <c>[2, id, action, payload]</c>.</summary>
    Call = 2,

    /// <summary>A successful answer: <c>[3, id, payload]</c>.</summary>
    Result = 3,

    /// <summary>A failed answer: <c>[4, id, code, description, details]</c>.</summary>
    Error = 4
}

/// <summary>One parsed OCPP frame; the payload stays a <see cref="JsonElement"/> the caller reads typed.</summary>
public abstract record OcppMessage(string MessageId);

/// <summary>A charge point or the CSMS asks the other side to do something.</summary>
public sealed record OcppCall(string MessageId, string Action, JsonElement Payload) : OcppMessage(MessageId);

/// <summary>The receiving side accepted and executed a call.</summary>
public sealed record OcppCallResult(string MessageId, JsonElement Payload) : OcppMessage(MessageId);

/// <summary>The receiving side refused a call; the description is meant for a human reading the device log.</summary>
public sealed record OcppCallError(string MessageId, string ErrorCode, string Description, JsonElement Details) : OcppMessage(MessageId);

/// <summary>A frame that is not valid OCPP 1.6J: not JSON, not an array, or an unknown message type.</summary>
public sealed class OcppProtocolException : Exception
{
    public OcppProtocolException(string message)
        : base(message)
    {
    }

    public OcppProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
