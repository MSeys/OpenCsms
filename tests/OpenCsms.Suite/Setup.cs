namespace OpenCsms.Suite;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using OpenCsms.Api;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure;
using OpenCsms.Suite.Devices;
using OpenCsms.Suite.Support;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Devices;
using ProtoTest.Devices.WebSocket;
using ProtoTest.Devices.WebSocket.AspNetCore;
using ProtoTest.Hosting;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;
using ProtoTest.NUnit;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.Sheets;
using ProtoTest.Sql.Testcontainers;
using ProtoTest.Web;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using CsmsApi = OpenCsms.Api.Program;

/// <summary>
/// The suite's only environment-specific file, and it has no environment conditionals: the containers
/// are registered unconditionally and skip themselves when the environment already provides their
/// addresses, and the API steps aside when its address is configured. In-process Postgres + RabbitMQ
/// on a development machine, configured connections in CI, one Setup either way.
/// </summary>
[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder
            // Time is a setting here too: the whole run lives at one instant, so the worker's
            // timestamps can be asserted and a product that reads the machine clock fails.
            .ConfigureClock(SuiteClock.Seed())
            .ConfigureTracing(trace => trace.OutputPath = Path.Combine("TestResults", "OpenCsms", "opencsms.prototrace"))
            .ConfigureAppConfiguration(configuration => configuration
                // The mode switch: an environment that exports the declared keys (the README's recipe)
                // makes the containers below skip, so one Setup serves both modes.
                .AddEnvironmentVariables())
            .AddInfrastructure(PostgresDatabase.Container(), CsmsInfrastructureExtensions.ConnectionStringKey)
            .AddInfrastructure(
                RabbitMqBroker.Container(),
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString")
            .AddInfrastructure(new DashboardBuildInfrastructure(), DashboardHosting.SettingKey)
            // The seeded busy month both tenants share: volume for the export journey, composed from
            // the product's own application services once per run (idempotent across reruns), after
            // the containers whose connection string it reads.
            .AddRunSetup("seeded-month", SeededMonth.SeedAsync)
            // Per-test prerequisites ride the Data provisioners below: the route is the product's
            // front door (the REST API), so the creation rules stay in the product.
            .AddData()
            .AddDataProvisioner<RegisterTariffRequest, TariffResponse, TariffProvisioner>()
            .AddDataProvisioner<RegisterStationRequest, StationResponse, StationProvisioner>()
            .AddDataProvisioner<CreateUserRequest, UserResponse, UserProvisioner>()
            .AddSheets()
            .AddWorkerHost<BillingWorker>("Billing")
            // The OCPP charge points: one in-process transport per (program, application), so the same
            // client registration reaches the gateway through the TestServer here and over the socket
            // when the application's address is configured. No mode conditionals either way.
            .AddInProcessWebSocketDevices<CsmsApi>(CsmsTargets.Api)
            .AddApplication(CsmsTargets.Api, app => app
                .AddAspNetCoreServer<CsmsApi>(webHost => webHost
                    // The operator's remote commands wait for the device's own answer. The product's
                    // default is ten seconds; two keep the timeout journey fast while a charge point
                    // that answers at all answers in milliseconds.
                    .UseSetting("Ocpp:RemoteCallTimeoutSeconds", "2"))
                .AddDevices(devices => devices
                    .AddWebSocketClient(CsmsTargets.Chargers, path: "/ocpp/{deviceId}")
                        .AddDevice<AcCharger>()
                        .AddProtocol<OcppProtocol>())
                .AddRest(rest => rest
                    .AddClient(CsmsTargets.Api)
                    .AddCollector<RestCoverageCollector>()))
            // The dashboard application carries the browser, while the REST and OCPP clients above
            // keep resolving the in-process server. The browser follows the published loopback address,
            // and the dashboard's own charge-point client reaches the same instance over a real socket,
            // so the remote-stop click test drives a connected charge point through the UI.
            .AddApplication(CsmsTargets.Dashboard, app => app
                .AddWeb(options => options.InstallBrowsers = true)
                .AddDevices(devices => devices
                    .AddWebSocketClient(CsmsTargets.DashboardChargers, path: "/ocpp/{deviceId}")
                        .AddDevice<AcCharger>()
                        .AddProtocol<OcppProtocol>()))
            // The same API on its own loopback listener, because a browser needs a real address: the
            // documented api-then-browser recipe. The factory builds from the run's collected
            // configuration, which arrives as command-line arguments (suite configuration, then the
            // values the started pieces published: the database, the broker, the dashboard build),
            // with the loopback --urls pair last. Registered with the address key it fills, so an
            // environment that configures that key skips the listener and the browser talks to that
            // environment instead.
            .AddLoopbackApplication(CsmsTargets.Dashboard, CsmsApi.Create)
            .AddHttpReadiness(CsmsTargets.Dashboard, "/healthz")
            .AddMessaging(messaging => messaging
                .CaptureAttachments()
                // Pre-bind the test's tap before the system under test publishes: the worker can
                // publish invoice.issued before a test reaches its first AwaitAsync.
                .Tap(CsmsEvents.Exchange)
                .UseRabbitMq())
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "OpenCsms", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "OpenCsms", "report.html");
                sink.Title = "OpenCSMS · ProtoTest Reference Suite";
            });
    }
}
