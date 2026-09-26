namespace OpenCsms.Domain;

/// <summary>
/// The states a connector can report, in the product's own vocabulary. Connector 0 is the charge
/// point itself, the OCPP convention. The protocol layer maps the OCPP values to these and back, so
/// the domain never carries a wire type.
/// </summary>
public enum ConnectorStatus
{
    Available,
    Preparing,
    Charging,
    SuspendedEVSE,
    SuspendedEV,
    Finishing,
    Reserved,
    Unavailable,
    Faulted
}
