namespace OpenCsms.Domain.Ocpp;

/// <summary>
/// The OCPP 1.6J actions this CSMS implements. The list is the documented subset: a message outside it
/// is answered with <see cref="OcppErrorCodes.NotImplemented"/> instead of being guessed at.
/// </summary>
public static class OcppActions
{
    public const string BootNotification = nameof(BootNotification);
    public const string Heartbeat = nameof(Heartbeat);
    public const string StatusNotification = nameof(StatusNotification);
    public const string StartTransaction = nameof(StartTransaction);
    public const string MeterValues = nameof(MeterValues);
    public const string StopTransaction = nameof(StopTransaction);
    public const string RemoteStartTransaction = nameof(RemoteStartTransaction);
    public const string RemoteStopTransaction = nameof(RemoteStopTransaction);
}

/// <summary>The error codes OCPP 1.6J defines for a call error's third element.</summary>
public static class OcppErrorCodes
{
    /// <summary>The action is known but not implemented by this receiver.</summary>
    public const string NotImplemented = nameof(NotImplemented);

    /// <summary>The action is not known by this receiver.</summary>
    public const string NotSupported = nameof(NotSupported);

    /// <summary>An internal error prevented the receiver from answering; the server log has the detail.</summary>
    public const string InternalError = nameof(InternalError);

    /// <summary>The frame violates the protocol; the description names the shape that was expected.</summary>
    public const string FormationViolation = nameof(FormationViolation);

    /// <summary>A field is outside its allowed range, for example a connector the station does not have.</summary>
    public const string PropertyConstraintViolation = nameof(PropertyConstraintViolation);

    /// <summary>A field has the wrong JSON type.</summary>
    public const string TypeConstraintViolation = nameof(TypeConstraintViolation);

    /// <summary>The requested action is not supported for this charge point in its current state.</summary>
    public const string GenericError = nameof(GenericError);
}
