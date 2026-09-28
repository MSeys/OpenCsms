namespace OpenCsms.Suite.Composition;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCsms.Application;
using OpenCsms.Application.Ports;
using OpenCsms.Infrastructure;

/// <summary>
/// The workers' composition roots build their own hosts from the same infrastructure and application
/// extensions the API calls, and an environment that validates the service graph - Aspire runs the
/// project resources in Development, where the billing and notification workers start - must resolve
/// every use case. The OCPP edge lives only in the API, so the application's graph has to build
/// without it: a worker host answers "no charge point is connected" through the transport-less
/// registry instead of failing at Build.
/// </summary>
public sealed class WorkerCompositionTests
{
    [Test]
    public void AWorkerCompositionBuildsWithoutTheOcppEdge()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            Args = []
        });
        // The workers' composition roots register exactly these; the Development environment makes
        // the host validate the service graph when it builds, and the store and broker addresses are
        // read when their services first do work, not while the graph is validated.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddCsmsInfrastructure();
        builder.Services.AddCsmsApplication();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        var connections = scope.ServiceProvider.GetRequiredService<IChargePointConnections>();
        Assert.That(
            connections.TryGet("CP-unknown", out _),
            Is.False,
            "a worker host has no connected charge points");
    }
}
