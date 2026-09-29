using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// The product reads its store from <c>ConnectionStrings:Csms</c> and its broker from
// <c>Messaging:RabbitMq:ConnectionString</c> - the same keys the suite's targets declare. A run that
// already provides either one keeps it: the graph declares its own resource only when the key is
// absent and injects the provided value into the projects either way, so an AppHost application
// can run against a store the environment started.
var databaseConnectionString = builder.Configuration["ConnectionStrings:Csms"];
var brokerConnectionString = builder.Configuration["Messaging:RabbitMq:ConnectionString"];

var database = string.IsNullOrWhiteSpace(databaseConnectionString)
    ? builder.AddPostgres("postgres")
        .WithImageTag("16-alpine")
        .AddDatabase("opencsms")
    : null;
var broker = string.IsNullOrWhiteSpace(brokerConnectionString)
    ? builder.AddRabbitMQ("rabbitmq").WithImageTag("3-alpine")
    : null;

var api = builder.AddProject<Projects.OpenCsms_Api>("api");
var billing = builder.AddProject<Projects.OpenCsms_Billing_Worker>("billing");
var notifications = builder.AddProject<Projects.OpenCsms_Notification_Worker>("notifications");

Connect(api);
Connect(billing);
// The notification worker reads its external target addresses from its own configuration. A
// staging topology provides them; a local run leaves them unset and the worker idles with the
// product's own log line, so the topology never points it at an address nobody owns.
Connect(notifications);

// The API serves the built dashboard; the suite's gate builds it before the run. An absolute path
// keeps the SPA reachable whatever working directory DCP gives the project.
if (DashboardDistFolder() is { } dashboardDist)
{
    api.WithEnvironment("Csms__Ui__Path", dashboardDist);
}

builder.Build().Run();

void Connect<T>(IResourceBuilder<T> resource)
    where T : IResourceWithEnvironment, IResourceWithWaitSupport
{
    if (database is not null)
    {
        resource.WithEnvironment("ConnectionStrings__Csms", database).WaitFor(database);
    }
    else
    {
        resource.WithEnvironment("ConnectionStrings__Csms", databaseConnectionString!);
    }

    if (broker is not null)
    {
        resource.WithEnvironment("Messaging__RabbitMq__ConnectionString", broker).WaitFor(broker);
    }
    else
    {
        resource.WithEnvironment("Messaging__RabbitMq__ConnectionString", brokerConnectionString!);
    }
}

static string? DashboardDistFolder()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "OpenCsms.slnx")))
        {
            var dist = Path.Combine(directory.FullName, "src", "OpenCsms.Dashboard", "dist");
            return Directory.Exists(dist) ? dist : null;
        }
    }

    return null;
}
