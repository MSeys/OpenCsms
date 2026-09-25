namespace OpenCsms.Billing.Worker;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCsms.Data;
using OpenCsms.Messaging;

/// <summary>
/// The billing worker as its own process: it builds a host, migrates the store and consumes
/// <c>session.ended</c>. ProtoTest starts this same entry point in-process through
/// <c>AddWorkerHost&lt;Program&gt;()</c>, and a deployment runs it with <c>dotnet run</c>.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddCsmsData(builder.Configuration);
        builder.Services.AddRabbitMqEventPublisher(builder.Configuration);
        builder.Services.AddHostedService<SessionEndedConsumer>();
        var host = builder.Build();

        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CsmsDbContext>().Database.Migrate();
        }

        host.Run();
    }
}
