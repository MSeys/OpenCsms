namespace OpenCsms.Protocol.Ocpp;

using System.Globalization;

/// <summary>
/// Reads the energy register a charge point sent in its meter values. OCPP carries sample values as
/// strings, the default measurand is the active energy imported, and the default unit is watt-hours;
/// anything else is a protocol error instead of a guessed number.
/// </summary>
public static class OcppEnergy
{
    /// <summary>The measurand that means "the meter's cumulative energy register".</summary>
    public const string ActiveImportRegister = "Energy.Active.Import.Register";

    /// <summary>Converts a wire reading in watt-hours to the kilowatt-hours the product speaks.</summary>
    public static decimal WattHoursToKwh(int wattHours) => wattHours / 1000m;

    /// <summary>Gets the last energy reading in kilowatt-hours, or throws for meter values that carry none.</summary>
    public static decimal ReadKwh(IReadOnlyList<MeterValueSample>? meterValues)
    {
        var sample = meterValues?
            .SelectMany(batch => batch.SampledValue)
            .LastOrDefault(candidate => candidate.Measurand is null or ActiveImportRegister);
        if (sample is null)
        {
            throw new OcppProtocolException($"The meter values carry no '{ActiveImportRegister}' sample.");
        }

        if (!decimal.TryParse(sample.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new OcppProtocolException($"The energy sample '{sample.Value}' is not a number.");
        }

        if (value < 0m)
        {
            throw new OcppProtocolException($"The energy sample '{sample.Value}' is negative.");
        }

        return sample.Unit switch
        {
            null or "Wh" => value / 1000m,
            "kWh" => value,
            _ => throw new OcppProtocolException($"The energy sample's unit '{sample.Unit}' is neither Wh nor kWh.")
        };
    }
}
