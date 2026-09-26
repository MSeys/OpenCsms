namespace OpenCsms.Suite.Support;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Run infrastructure that hosts the CSMS API on its own loopback listener inside this test process -
/// the documented api-then-browser recipe - and publishes the address the listener bound as the
/// application's <c>BaseUrl</c>, so the browser session and the dashboard it serves resolve that one
/// running application. Unlike <c>AddAspNetCoreServer</c>, a hand-built <see cref="WebApplication"/>
/// receives no infrastructure settings from the host, so this piece implements
/// <see cref="IProtoConfiguredInfrastructure"/> and passes the run's collected configuration (suite
/// configuration, then the values pieces published: the database, the broker, the dashboard build)
/// into the application's own configuration. Registered with the address key it fills, so an
/// environment that configures that key skips the listener and the browser talks to that environment.
/// </summary>
internal sealed class LoopbackApplication : IProtoConfiguredInfrastructure, IProtoSettingsInfrastructure
{
    private readonly Func<string[], IConfiguration, WebApplication> _create;
    private WebApplication? _application;
    private string _baseUrl = string.Empty;

    internal LoopbackApplication(string applicationName, Func<string[], IConfiguration, WebApplication> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(create);
        ApplicationName = applicationName;
        _create = create;
    }

    internal string ApplicationName { get; }

    internal string BaseUrlKey => $"{ProtoApplication.SectionPath}:{ApplicationName}:BaseUrl";

    public string Id => $"application:loopback:{ApplicationName}";

    public string Kind => "application";

    public string Description => $"{ApplicationName} on a loopback listener";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BaseUrlKey] = _baseUrl
    };

    public async ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
    {
        // The application reads the same configuration a running test would: the suite's own
        // configuration first, then what started pieces published (a container address wins).
        var values = context.Configuration
            .AsEnumerable()
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
        foreach (var (key, value) in context.Settings.Values)
        {
            values[key] = value;
        }

        // Port 0 lets the OS pick a free port, and the bound address is read back from the listener
        // rather than guessed, so suites never collide over a fixed port.
        var application = _create(
            ["--urls", "http://127.0.0.1:0"],
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
                .Build());
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _application = application;
        _baseUrl = application.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .First();
    }

    public async ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        if (_application is not { } application)
        {
            return;
        }

        _application = null;
        await application.DisposeAsync().ConfigureAwait(false);
    }
}
