namespace OpenCsms.Suite.Support;

/// <summary>
/// The suite's target names, in one place. Setup registers the application under
/// <see cref="Api"/> and the test classes refer to it through their <c>[Application]</c>
/// attributes, so a rename cannot leave the registration and the attributes disagreeing
/// (R1a-11; the demo's target-name constant).
/// </summary>
public static class CsmsTargets
{
    public const string Api = "Csms";

    /// <summary>
    /// The dashboard application: the same API hosted on its own loopback listener so the browser has a
    /// real address, while <see cref="Api"/> stays the in-process test server for REST and OCPP.
    /// </summary>
    public const string Dashboard = "Dashboard";

    /// <summary>The device client the OCPP charge-point simulator hangs off.</summary>
    public const string Chargers = "Chargers";
}
