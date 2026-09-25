namespace OpenCsms.Billing.Worker;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCsms.Data;
using OpenCsms.Messaging;

/// <summary>
/// The billing worker as its own process: it builds a host, migrates the store and consumes
/// <c>session.ended</c>. ProtoTest starts this same entry point in-process through
/// <c>AddWorkerHost&lt;Program&gt;()</c>, and a deployment runs it with <c>dotnet run</c>.
/// </summary>
public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddCsmsData();
        builder.Services.AddRabbitMqEventPublisher();
        builder.Services.AddHostedService<SessionEndedConsumer>();
        var host = builder.Build();

        // Migrations run from the API only (R1a-07): EF Core 8 does not serialize concurrent
        // migrations, the suite starts the hosts sequentially today, and M4's concurrent boot
        // decision will revisit this (a PostgreSQL advisory lock, or one designated migrator).
        host.Run();
    }
}
