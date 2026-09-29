namespace OpenCsms.Billing.Worker;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCsms.Application;
using OpenCsms.Infrastructure;

/// <summary>
/// The billing worker as its own process: it builds a host, composes the store and the application's
/// billing use case, and consumes <c>session.ended</c>. ProtoTest starts this same entry point
/// in-process through <c>AddWorkerHost&lt;Program&gt;()</c>, and a deployment runs it with
/// <c>dotnet run</c>.
/// </summary>
public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddCsmsInfrastructure();
        builder.Services.AddCsmsApplication();
        // A standalone run has the system clock; the suite replaces this with the test's clock.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<SessionEndedConsumer>();
        var host = builder.Build();

        // Migrations run from the API only: EF Core 8 does not serialize concurrent migrations, and
        // the suite starts the hosts sequentially.
        host.Run();
    }
}
