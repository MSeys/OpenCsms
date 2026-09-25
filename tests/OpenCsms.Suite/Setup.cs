namespace OpenCsms.Suite;

using Microsoft.Extensions.Configuration;
using OpenCsms.Contracts;
using OpenCsms.Data;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Hosting;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;
using ProtoTest.NUnit;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.Sql.Testcontainers;
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
            .ConfigureTracing(trace => trace.OutputPath = Path.Combine("TestResults", "OpenCsms", "opencsms.prototrace"))
            .ConfigureAppConfiguration(configuration => configuration
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Bind the test's tap before the system under test publishes: the worker can publish
                    // invoice.issued before a test reaches its first AwaitAsync.
                    ["ProtoTest:Messaging:Destinations:0"] = CsmsEvents.Exchange
                })
                // The mode switch: an environment that exports the declared keys (the README's recipe)
                // makes the containers below skip, so one Setup serves both modes.
                .AddEnvironmentVariables())
            .AddInfrastructure(PostgresDatabase.Container(), CsmsDataExtensions.ConnectionStringKey)
            .AddInfrastructure(
                RabbitMqBroker.Container(),
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString")
            .AddWorkerHost<BillingWorker>("Billing")
            .AddApplication("Csms", app => app
                .AddAspNetCoreServer<CsmsApi>()
                .AddRest(rest => rest
                    .AddClient("Csms")
                    .AddCollector<RestCoverageCollector>()))
            .AddMessaging(messaging => messaging.CaptureAttachments().UseRabbitMq())
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "OpenCsms", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "OpenCsms", "report.html");
                sink.Title = "OpenCSMS · ProtoTest Reference Suite";
            });
    }
}
