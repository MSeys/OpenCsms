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
using ProtoTest.Testcontainers;
using ProtoTest.Web;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using CsmsApi = OpenCsms.Api.Program;

/// <summary>
/// The suite's only environment-specific file, and it has no environment conditionals: every target -
/// the store, the broker, the API application with its billing worker, the dashboard application -
/// declares an ordered provider chain, and the first provider whose condition holds serves it. In a
/// development run the suite's containers start and the application and its worker run in-process; an
/// environment that exports the declared keys serves the store, the broker and the applications
/// instead, and runs the worker itself; one Setup in every mode.
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
                // The environment's exported keys are what make a configured or published run
                // distinct; the chains below read them and step aside when they are present.
                .AddEnvironmentVariables())
            // The store and the broker: a configured connection string serves the target, and the
            // suite's own container starts only when the environment does not provide one.
            .AddInfrastructure(
                "CsmsDatabase",
                chain => chain
                    .UseConfigured()
                    .UseContainer(PostgresDatabase.Container()),
                CsmsInfrastructureExtensions.ConnectionStringKey)
            .AddInfrastructure(
                "CsmsBroker",
                chain => chain
                    .UseConfigured()
                    .UseContainer(RabbitMqBroker.Container()),
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString")
            .AddInfrastructure(new DashboardBuildInfrastructure(), DashboardHosting.SettingKey)
            // The seeded busy month both tenants share: volume for the export journey, composed from
            // the product's own application services once per run (idempotent across reruns), after
            // the store whose connection string it reads. ProtoTest:Seed=off leaves the target
            // alone, and the seeded journeys skip with it.
            .AddRunSetup("seeded-month", SeededMonth.SeedAsync)
            // Per-test prerequisites ride the Data provisioners below: the route is the product's
            // front door (the REST API), so the creation rules stay in the product.
            .AddData()
            .AddDataProvisioner<RegisterTenantRequest, TenantRegistrationResponse, TenantProvisioner>()
            .AddDataProvisioner<RegisterTariffRequest, TariffResponse, TariffProvisioner>()
            .AddDataProvisioner<RegisterStationRequest, StationResponse, StationProvisioner>()
            .AddDataProvisioner<CreateUserRequest, UserResponse, UserProvisioner>()
            .AddSheets()
            // The OCPP charge points: one in-process transport per (program, application), so the same
            // client registration reaches the gateway through the TestServer while the application
            // runs in-process and over the socket when its address belongs to an environment.
            .AddInProcessWebSocketDevices<CsmsApi>(CsmsTargets.Api)
            // The API application, with the billing worker that belongs to it: an environment that
            // configures the address serves the application and runs the worker; otherwise the
            // in-process test server wins and the run hosts the worker's own entry point.
            .AddApplication(CsmsTargets.Api, app => app
                .UseConfigured()
                .UseInProcess<CsmsApi>(webHost => webHost
                    // The operator's remote commands wait for the device's own answer. The product's
                    // default is ten seconds; two keep the timeout journey fast while a charge point
                    // that answers at all answers in milliseconds.
                    .UseSetting("Ocpp:RemoteCallTimeoutSeconds", "2"))
                .AddWorkerHost<BillingWorker>("Billing")
                .AddDevices(devices => devices
                    .AddWebSocketClient(CsmsTargets.Chargers, path: "/ocpp/{deviceId}")
                        .AddDevice<AcCharger>()
                        .AddProtocol<OcppProtocol>())
                .AddRest(rest => rest
                    .AddClient(CsmsTargets.Api)
                    .AddCollector<RestCoverageCollector>()))
            .AddHttpReadiness(CsmsTargets.Api, "/healthz")
            // The dashboard application carries the browser, while the REST and OCPP clients above
            // keep resolving the API's winner. A browser needs a real address, so when the
            // environment configures none the run starts the hand-built application on its own
            // loopback listener; the dashboard's charge-point client then reaches that same running
            // instance over a real socket, so the remote-stop click test drives a connected charge
            // point through the UI.
            .AddApplication(CsmsTargets.Dashboard, app => app
                .UseConfigured()
                .UseLoopback(CsmsApi.Create)
                .AddWeb(options => options.InstallBrowsers = true)
                .AddDevices(devices => devices
                    .AddWebSocketClient(CsmsTargets.DashboardChargers, path: "/ocpp/{deviceId}")
                        .AddDevice<AcCharger>()
                        .AddProtocol<OcppProtocol>()))
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
