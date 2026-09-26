namespace OpenCsms.Application.Commands;

/// <summary>
/// The operator commands' settings: how long a server-initiated call waits for the charge point's
/// answer. The composition root binds this from the product's OCPP section.
/// </summary>
public sealed class RemoteCommandOptions
{
    /// <summary>How long a server-initiated call waits for the charge point's answer.</summary>
    public int RemoteCallTimeoutSeconds { get; set; } = 10;
}
