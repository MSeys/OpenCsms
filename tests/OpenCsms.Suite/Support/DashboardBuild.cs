namespace OpenCsms.Suite.Support;

using ProtoTest.Core;

/// <summary>
/// Finds the built operator dashboard inside the repository, so the API the suite hosts serves the
/// bundle <c>eng/build-dashboard.ps1</c> produced no matter which directory the test run started in.
/// The repository root is the folder holding <c>OpenCsms.slnx</c>, discovered by walking up from the
/// test assembly's base directory.
/// </summary>
public static class DashboardBuild
{
    public static string? RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The absolute <c>src/OpenCsms.Dashboard</c> folder, the SPA's source.</summary>
    public static string? SourceFolder { get; } = RepositoryRoot is { } root
        ? Path.Combine(root, "src", "OpenCsms.Dashboard")
        : null;

    /// <summary>The absolute <c>src/OpenCsms.Dashboard/dist</c> folder the API serves.</summary>
    public static string? DistFolder { get; } = SourceFolder is { } source
        ? Path.Combine(source, "dist")
        : null;

    /// <summary>Whether the dashboard was built; without it the API answers with its not-built page.</summary>
    public static bool IsBuilt => DistFolder is { } dist && File.Exists(Path.Combine(dist, "index.html"));

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenCsms.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}

/// <summary>
/// Publishes the dashboard build path (<c>Csms:Ui:Path</c>) to every application the run starts, so
/// the loopback instance serves exactly the bundle the gate built. Registered with the key it fills:
/// an environment that configures that key skips this piece and serves its own build instead.
/// </summary>
internal sealed class DashboardBuildInfrastructure : IProtoSettingsInfrastructure
{
    public string Id => "application:dashboard-build";

    public string Kind => "application";

    public string Description => "The operator dashboard build";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings => DashboardBuild.DistFolder is { } dist
        ? new Dictionary<string, string>(StringComparer.Ordinal) { [OpenCsms.Api.DashboardHosting.SettingKey] = dist }
        : new Dictionary<string, string>(StringComparer.Ordinal);

    public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// Skips the browser journeys with a clear reason when the built dashboard is missing, so a machine
/// without Node or without a completed build reports skipped rather than looking at the not-built page.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequiresDashboardBuildAttribute : ProtoAttribute, IProtoSkipCondition
{
    public string? GetSkipReason(ProtoHost host)
        => DashboardBuild.IsBuilt
            ? null
            : $"The operator dashboard is not built; run 'pwsh eng/build-dashboard.ps1' or 'npm run build' in '{DashboardBuild.SourceFolder ?? "src/OpenCsms.Dashboard"}'.";
}
