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
}
