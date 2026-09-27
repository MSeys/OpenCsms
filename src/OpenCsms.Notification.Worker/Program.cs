namespace OpenCsms.Notification.Worker;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCsms.Application;
using OpenCsms.Infrastructure;

/// <summary>
/// The notification worker as its own process: it builds a host, composes the outbound notification
/// adapter and consumes <c>invoice.issued</c> and <c>billing.failed</c>. ProtoTest starts this same
/// entry point in-process through <c>AddWorkerHost&lt;Program&gt;()</c>, and a deployment runs it with
/// <c>dotnet run</c>. A kind whose target base address is not configured stays idle and says so; no
/// notification is silently dropped for a configured target.
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
        builder.Services.AddHostedService<InvoiceIssuedNotificationConsumer>();
        builder.Services.AddHostedService<BillingFailedNotificationConsumer>();
        var host = builder.Build();

        // Migrations run from the API only: EF Core 8 does not serialize concurrent migrations, and
        // the suite starts the hosts sequentially. Revisit this if hosts ever boot concurrently
        // (a PostgreSQL advisory lock, or one designated migrator).
        host.Run();
    }
}
