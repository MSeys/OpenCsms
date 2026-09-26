namespace OpenCsms.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Gives <c>dotnet ef migrations</c> a context without starting the API: the same connection key,
/// falling back to the docker-compose defaults in the repository.
/// </summary>
public sealed class CsmsDbContextFactory : IDesignTimeDbContextFactory<CsmsDbContext>
{
    public CsmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Csms")
            ?? "Host=localhost;Port=5432;Database=opencsms;Username=opencsms;Password=opencsms";
        var options = new DbContextOptionsBuilder<CsmsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CsmsDbContext(options);
    }
}
