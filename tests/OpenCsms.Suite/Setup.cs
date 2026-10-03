namespace OpenCsms.Suite;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCsms.Api;
using OpenCsms.AppHost;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure;
using OpenCsms.Infrastructure.Notifications;
using OpenCsms.Suite.Devices;
using OpenCsms.Suite.Support;
using ProtoTest.AspNetCore;
using ProtoTest.Aspire;
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
using ProtoTest.OpenApi;
using ProtoTest.Rest;
using ProtoTest.Sheets;
using ProtoTest.Sql.Testcontainers;
using ProtoTest.Testcontainers;
using ProtoTest.Web;
using ProtoTest.WireMock;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using CsmsApi = OpenCsms.Api.Program;
using NotificationWorker = OpenCsms.Notification.Worker.Program;

/// <summary>
/// One setup for every mode: each target declares an ordered provider chain and the first provider
/// whose condition holds serves it, so containers, an in-process host and an environment's own
/// addresses all run the same journeys.
/// </summary>
[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        // The notification worker reads its targets once, when its host starts, so the fakes that
        // serve them must listen on ports reserved before the run.
        var invoiceReadyPort = LoopbackPort.Reserve();
        var billingFailurePort = LoopbackPort.Reserve();

        builder
            // Time is a setting here too: the whole run lives at one instant, so the worker's
            // timestamps can be asserted and a product that reads the machine clock fails.
            .ConfigureClock(SuiteClock.Seed())
            .ConfigureTracing(trace => trace.OutputPath = Path.Combine("TestResults", "OpenCsms", "opencsms.prototrace"))
            .ConfigureAppConfiguration(configuration => configuration
                // The fake addresses are suite-owned defaults an environment may override; the
                // environment's exported keys are what make a configured or published run distinct,
                // so they are added last and win.
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [NotificationSettings.InvoiceReadyBaseUrlKey] = $"http://127.0.0.1:{invoiceReadyPort}",
                    [NotificationSettings.BillingFailureBaseUrlKey] = $"http://127.0.0.1:{billingFailurePort}"
                })
                .AddEnvironmentVariables())
            // The topology, selected by ProtoTest:Aspire:Enabled, which the run script sets.
            .AddAspireAppHost<OpenCsmsAppHostAnchor>(
                options => options
                    .MapResource("api", CsmsTargets.Api)
                    .MapConnectionString("opencsms", CsmsInfrastructureExtensions.ConnectionStringKey)
                    .MapConnectionString("rabbitmq", RabbitMqOptions.ConnectionStringSetting),
                "api",
                "opencsms",
                "rabbitmq")
            // The store and the broker: a configured connection string serves the target, the
            // AppHost's resource serves it in topology mode, and the suite's own container starts
            // only when neither does.
            .AddInfrastructure(
                "CsmsDatabase",
                chain => chain
                    .UseConfigured()
                    .UseAspireResource<OpenCsmsAppHostAnchor>("opencsms")
                    .UseContainer(PostgresDatabase.Container()),
                CsmsInfrastructureExtensions.ConnectionStringKey)
            .AddInfrastructure(
                "CsmsBroker",
                chain => chain
                    .UseConfigured()
                    .UseAspireResource<OpenCsmsAppHostAnchor>("rabbitmq")
                    .UseContainer(RabbitMqBroker.Container()),
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString")
            .AddInfrastructure(
                "DashboardBuild",
                chain => chain
                    .UseConfigured()
                    .Use(new ProtoTargetProvider("dashboard-build", new DashboardBuildInfrastructure())),
                DashboardHosting.SettingKey)
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
            // configures the address - or the AppHost, when it is selected - serves the application
            // and runs the worker; otherwise the in-process test server wins and the run hosts the
            // worker's own entry point.
            .AddApplication(CsmsTargets.Api, app => app
                .UseConfigured()
                .UseAspireResource<OpenCsmsAppHostAnchor>("api")
                .UseInProcess<CsmsApi>(webHost => webHost
                    // The operator's remote commands wait for the device's own answer. The product's
                    // default is ten seconds; two keep the timeout journey fast while a charge point
                    // that answers at all answers in milliseconds.
                    .UseSetting("Ocpp:RemoteCallTimeoutSeconds", "2"))
                .AddWorkerHost<BillingWorker>("Billing")
                // The notification worker follows the API's winner like the billing worker: hosted
                // in this process when the application is, run by the environment otherwise.
                .AddWorkerHost<NotificationWorker>("Notifications")
                .AddDevices(devices => devices
                    .AddWebSocketClient(CsmsTargets.Chargers, path: "/ocpp/{deviceId}")
                        .AddDevice<AcCharger>()
                        .AddProtocol<OcppProtocol>())
                .AddRest(rest => rest
                    .AddClient(CsmsTargets.Api)
                    .AddCollector<RestCoverageCollector>()
                    // The response fields that arrived but that no shape assertion checked.
                    .AddCollector<RestTrafficCoverageCollector>()
                    // Coverage against the committed contract names every endpoint no test calls.
                    .AddCollector<OpenApiCoverageCollector>(Path.Combine(AppContext.BaseDirectory, "opencsms.openapi.json"))))
            .AddHttpReadiness(CsmsTargets.Api, "/healthz")
            // The dashboard application carries the browser. A browser needs a real address, so when
            // neither the environment nor the AppHost configures one the run starts the application
            // on its own loopback listener and the charge-point client reaches that same instance.
            .AddApplication(CsmsTargets.Dashboard, app => app
                .UseConfigured()
                .UseAspireResource<OpenCsmsAppHostAnchor>("api")
                .UseLoopback(CsmsApi.Create)
                .AddWeb(options => options.InstallBrowsers = true)
                .AddDevices(devices => devices
                    .AddWebSocketClient(CsmsTargets.DashboardChargers, path: "/ocpp/{deviceId}")
                        .AddDevice<AcCharger>()
                        .AddProtocol<OcppProtocol>()))
            .AddHttpReadiness(CsmsTargets.Dashboard, "/healthz")
            // The seeded busy month runs after the store and the readiness probes, so in topology
            // mode the API has migrated the AppHost's fresh database before the seed writes to it.
            .AddRunSetup("seeded-month", SeededMonth.SeedAsync)
            .AddMessaging(messaging => messaging
                .CaptureAttachments()
                // Pre-bind the test's taps before the system under test publishes: the worker can
                // publish invoice.issued before a test reaches its first AwaitAsync, and the
                // dead-letter journeys await the product's dead-letter exchanges, whose bindings carry
                // the copy into the dead-letter queues.
                .Tap(CsmsEvents.Exchange, CsmsEvents.DeadLetterExchange, CsmsEvents.NotificationsDeadLetterExchange)
                .UseRabbitMq())
            // The external notification targets: per-run fakes on the reserved ports, so the hosted
            // worker's configured addresses stay valid for every test while each journey reads its
            // own request out of the shared log. A per-run fake keeps its stubs for the run; the
            // journeys register the catch-all routes their notifications arrive on.
            .AddWireMock(CsmsTargets.InvoiceReadyTarget, fake => fake.PerRun().Port(invoiceReadyPort))
            .AddWireMock(CsmsTargets.BillingFailureTarget, fake => fake.PerRun().Port(billingFailurePort))
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "OpenCsms", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "OpenCsms", "report.html");
                sink.Title = "OpenCSMS · ProtoTest Reference Suite";
            })
            // Every OCPP message kind the protocol knows that no test expected, per charge-point client.
            .ConfigureServices(services => services
                .AddSingleton<IProtoCollector>(new DeviceCoverageCollector(CsmsTargets.Chargers, [new OcppProtocol()]))
                .AddSingleton<IProtoCollector>(new DeviceCoverageCollector(CsmsTargets.DashboardChargers, [new OcppProtocol()])));
    }
}
