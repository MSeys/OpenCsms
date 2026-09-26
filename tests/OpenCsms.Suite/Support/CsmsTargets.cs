namespace OpenCsms.Suite.Support;

/// <summary>
/// The suite's target names, in one place. Setup registers the application under
/// <see cref="Api"/> and the test classes refer to it through their <c>[Application]</c>
/// attributes, so a rename cannot leave the registration and the attributes disagreeing.
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

    /// <summary>
    /// The charge-point client bound to the dashboard application: the same simulator over a real
    /// socket to the loopback listener, so a browser journey can drive a connected charge point
    /// through the dashboard's own remote commands. The in-process journeys keep using
    /// <see cref="Chargers"/>, whose transport reaches the test server with the test's clock.
    /// </summary>
    public const string DashboardChargers = "DashboardChargers";
}
