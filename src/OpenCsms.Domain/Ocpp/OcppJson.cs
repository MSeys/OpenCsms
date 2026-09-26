namespace OpenCsms.Domain.Ocpp;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The OCPP 1.6J JSON framing both sides of the device edge share: a call is
/// <c>[2, id, action, payload]</c>, a call result <c>[3, id, payload]</c>, and a call error
/// <c>[4, id, code, description, details]</c>. The gateway and the charge-point simulator go through
/// these types instead of each shaping JSON on its own.
/// </summary>
public static class OcppJson
{
    /// <summary>The serializer options every payload uses: web camelCase plus OCPP's string enums.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a call, the frame a charge point sends to the CSMS (and the CSMS to it).</summary>
    public static string SerializeCall(string messageId, string action, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(payload);

        return Write(writer =>
        {
            writer.WriteStartArray();
            writer.WriteNumberValue((int)OcppMessageType.Call);
            writer.WriteStringValue(messageId);
            writer.WriteStringValue(action);
            JsonSerializer.Serialize(writer, payload, Options);
            writer.WriteEndArray();
        });
    }

    /// <summary>Serializes a call result, the frame that answers a call.</summary>
    public static string SerializeCallResult(string messageId, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(payload);

        return Write(writer =>
        {
            writer.WriteStartArray();
            writer.WriteNumberValue((int)OcppMessageType.Result);
            writer.WriteStringValue(messageId);
            JsonSerializer.Serialize(writer, payload, Options);
            writer.WriteEndArray();
        });
    }

    /// <summary>Serializes a call error; details default to an empty object.</summary>
    public static string SerializeCallError(string messageId, string errorCode, string description, object? details = null)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return Write(writer =>
        {
            writer.WriteStartArray();
            writer.WriteNumberValue((int)OcppMessageType.Error);
            writer.WriteStringValue(messageId);
            writer.WriteStringValue(errorCode);
            writer.WriteStringValue(description);
            JsonSerializer.Serialize(writer, details ?? new { }, Options);
            writer.WriteEndArray();
        });
    }

    /// <summary>Parses a frame, throwing <see cref="OcppProtocolException"/> for anything malformed.</summary>
    public static OcppMessage Parse(string text)
    {
        if (!TryParse(text, out var message, out var error))
        {
            throw new OcppProtocolException(error!);
        }

        return message;
    }

    /// <summary>Tries to parse a frame; a frame that is not OCPP 1.6J is not an exception here.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out OcppMessage? message)
        => TryParse(text, out message, out _);

    /// <summary>Tries to parse a frame, reporting why it failed.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out OcppMessage? message, [NotNullWhen(false)] out string? error)
    {
        message = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The frame is empty.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            error = $"The frame is not JSON: {exception.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
            {
                error = "An OCPP frame is an array of at least three elements.";
                return false;
            }

            if (root[0].ValueKind != JsonValueKind.Number || !root[0].TryGetInt32(out var type))
            {
                error = $"The frame's first element is not a message type: {root[0].GetRawText()}.";
                return false;
            }

            if (root[1].ValueKind != JsonValueKind.String)
            {
                error = $"The frame's message id is not a string: {root[1].GetRawText()}.";
                return false;
            }

            var messageId = root[1].GetString()!;
            switch ((OcppMessageType)type)
            {
                case OcppMessageType.Call when root.GetArrayLength() == 4:
                    if (root[2].ValueKind != JsonValueKind.String)
                    {
                        error = $"A call's action is not a string: {root[2].GetRawText()}.";
                        return false;
                    }

                    message = new OcppCall(messageId, root[2].GetString()!, root[3].Clone());
                    return true;
                case OcppMessageType.Result when root.GetArrayLength() == 3:
                    message = new OcppCallResult(messageId, root[2].Clone());
                    return true;
                case OcppMessageType.Error when root.GetArrayLength() == 5:
                    if (root[2].ValueKind != JsonValueKind.String || root[3].ValueKind != JsonValueKind.String)
                    {
                        error = "A call error's code and description must both be strings.";
                        return false;
                    }

                    message = new OcppCallError(messageId, root[2].GetString()!, root[3].GetString()!, root[4].Clone());
                    return true;
                default:
                    error = type is 2 or 3 or 4
                        ? $"A {(OcppMessageType)type} frame has {root.GetArrayLength()} elements, which is not its shape."
                        : $"Unknown OCPP message type '{type}'.";
                    return false;
            }
        }
    }

    /// <summary>Reads a payload as its typed contract.</summary>
    public static T ReadPayload<T>(JsonElement payload)
        => payload.Deserialize<T>(Options)
            ?? throw new OcppProtocolException($"The payload of a {typeof(T).Name} was null.");

    private static string Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        // Optional OCPP fields are absent when they carry no value, not explicitly null.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // OCPP is a wire contract: an unknown member is a producer bug, and the reader should say so.
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        return options;
    }
}
