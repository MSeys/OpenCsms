namespace OpenCsms.Suite.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCsms.Application;
using OpenCsms.Application.Billing;
using OpenCsms.Application.Catalog;
using OpenCsms.Application.Identity;
using OpenCsms.Application.Ports;
using OpenCsms.Application.Sessions;
using OpenCsms.Contracts;
using OpenCsms.Domain;
using OpenCsms.Infrastructure;
using ProtoTest.Core;

/// <summary>
/// The run's seeded busy month: two tenants sharing one May 2030, each with a tariff, a station, an
/// operator admin and <see cref="SessionsPerTenant"/> billed sessions, so the export journey reads
/// hundreds of rows without arranging them through REST one by one. This is volume, not
/// prerequisites: per-test tenants, tariffs and stations still go through the API in
/// <see cref="CsmsOperatorAttribute"/>, and only this seeder composes the product's application
/// services in-process. It runs once per run as a run setup step and is idempotent: a rerun against
/// a database that already holds the seeded tariff finds the marker and stores nothing.
/// </summary>
public static class SeededMonth
{
    /// <summary>The exported month, on every seeded invoice's <c>IssuedAtUtc</c>.</summary>
    public const string MonthText = "2030-05";

    /// <summary>Sessions (and invoices) seeded per tenant; two tenants share the month.</summary>
    public const int SessionsPerTenant = 120;

    /// <summary>The seeded tariff's name: the idempotency marker a rerun looks for.</summary>
    public const string TariffName = "Seeded busy month";

    /// <summary>
    /// The product's switch: set to <c>off</c>, the run leaves the target's data alone - no migration
    /// and no seeding - and the seeded journeys skip.
    /// </summary>
    public const string SeedSettingKey = "ProtoTest:Seed";

    /// <summary>
    /// Whether this run ensured the seeded month: the seed step sets it once the marker is present,
    /// so the journeys that read the seeded rows gate on it instead of failing without the data.
    /// </summary>
    public static bool IsSeeded { get; private set; }

    public static readonly SeededTenant TenantA = new(
        "seeded-a",
        "Seeded Station A",
        "seeded-cp-a",
        "seeded-a@opencsms.test",
        "seeded-operator-a-pass");

    public static readonly SeededTenant TenantB = new(
        "seeded-b",
        "Seeded Station B",
        "seeded-cp-b",
        "seeded-b@opencsms.test",
        "seeded-operator-b-pass");

    /// <summary>Seeds the month when the marker tariff is missing; a no-op otherwise.</summary>
    public static async ValueTask SeedAsync(ProtoRunSetupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // An environment that owns its data sets ProtoTest:Seed=off: the step does neither the
        // migration nor the seeding, and the journeys that read the seeded month stay skipped.
        if (string.Equals(context.Configuration[SeedSettingKey], "off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = ResolveConnectionString(context);
        var clock = new SeedClock(new DateTimeOffset(2030, 5, 1, 8, 0, 0, TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CsmsInfrastructureExtensions.ConnectionStringKey] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCsmsInfrastructure();
        services.AddCsmsApplication();
        // Last registration wins: the seed bills through the issuance use case but drops its
        // events instead of reaching the broker, so seeding needs the database only.
        services.AddSingleton<IEventPublisher, DroppedEvents>();
        await using var provider = services.BuildServiceProvider();

        // The API migrates at boot too; migrating here as well makes the seed independent of which
        // host started first, and applying migrations twice is a no-op.
        provider.MigrateCsmsData();

        await using var scope = provider.CreateAsyncScope();
        var tariffs = scope.ServiceProvider.GetRequiredService<TariffReads>();
        var existing = await tariffs.ListAsync(TenantA.TenantId, context.CancellationToken);
        if (existing.Any(tariff => string.Equals(tariff.Name, TariffName, StringComparison.Ordinal)))
        {
            IsSeeded = true;
            return;
        }

        await SeedTenantAsync(scope.ServiceProvider, TenantA, clock, context.CancellationToken);
        await SeedTenantAsync(scope.ServiceProvider, TenantB, clock, context.CancellationToken);
        IsSeeded = true;
    }

    private static async Task SeedTenantAsync(
        IServiceProvider services,
        SeededTenant tenant,
        SeedClock clock,
        CancellationToken cancellationToken)
    {
        var tariffRegistration = services.GetRequiredService<TariffRegistration>();
        var tariff = await tariffRegistration.RegisterAsync(
            new RegisterTariffCommand(
                tenant.TenantId,
                TariffName,
                0.40m,
                1.50m,
                2.00m,
                TimeSpan.FromMinutes(10),
                "EUR"),
            cancellationToken);

        var stationRegistration = services.GetRequiredService<StationRegistration>();
        var stationOutcome = await stationRegistration.RegisterAsync(
            new RegisterStationCommand(
                tenant.TenantId,
                tenant.ChargePointId,
                tenant.StationName,
                2,
                tariff.Id),
            cancellationToken);
        if (stationOutcome is not StationRegistered registered)
        {
            throw new InvalidOperationException($"Seeding {tenant.TenantId} stopped at its station: {stationOutcome.GetType().Name}.");
        }

        var userRegistration = services.GetRequiredService<UserRegistration>();
        var userOutcome = await userRegistration.RegisterAsync(
            new RegisterUserCommand(
                tenant.TenantId,
                tenant.LoginEmail,
                tenant.StationName,
                tenant.LoginPassword,
                UserRoles.Operator),
            cancellationToken);
        if (userOutcome is not UserRegistered)
        {
            throw new InvalidOperationException($"Seeding {tenant.TenantId} stopped at its operator: {userOutcome.GetType().Name}.");
        }

        var starts = services.GetRequiredService<SessionStart>();
        var meters = services.GetRequiredService<SessionMeterValues>();
        var ending = services.GetRequiredService<SessionEnding>();
        var issuance = services.GetRequiredService<InvoiceIssuance>();
        for (var index = 0; index < SessionsPerTenant; index++)
        {
            var energyKwh = 5m + (index % 21);
            var start = await starts.StartAsync(
                new StartSessionCommand(registered.Station.Id, 1 + (index % 2)),
                cancellationToken);
            if (start is not SessionStarted started)
            {
                throw new InvalidOperationException($"Seeding {tenant.TenantId} stopped at session {index}: {start.GetType().Name}.");
            }

            clock.Advance(TimeSpan.FromMinutes(5));
            await meters.RecordAsync(started.Session.Id, energyKwh, cancellationToken);

            // The unplug follows the last meter value by a minute, inside the ten-minute grace, so
            // every seeded invoice bills energy plus the start fee and no idle fee.
            clock.Advance(TimeSpan.FromMinutes(1));
            var session = started.Session;
            await ending.EndAsync(session, clock.GetUtcNow(), cancellationToken);
            await issuance.IssueAsync(
                new SessionEnded(
                    session.Id,
                    session.TenantId,
                    session.StationId,
                    session.ConnectorId,
                    session.StartedAtUtc,
                    session.EndedAtUtc!.Value,
                    session.EnergyKwh),
                cancellationToken);
            clock.Advance(TimeSpan.FromMinutes(41));
        }
    }

    private static string ResolveConnectionString(ProtoRunSetupContext context)
    {
        if (context.Settings.Values.TryGetValue(
                CsmsInfrastructureExtensions.ConnectionStringKey,
                out var provided)
            && !string.IsNullOrWhiteSpace(provided))
        {
            return provided;
        }

        var configured = context.Configuration[CsmsInfrastructureExtensions.ConnectionStringKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        throw new InvalidOperationException(
            $"No database address is available. Set '{CsmsInfrastructureExtensions.ConnectionStringKey}' " +
            "(a started PostgreSQL container does this).");
    }

    /// <summary>The seed's clock: May 2030, advanced as sessions are stored so invoices spread over the month.</summary>
    private sealed class SeedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>The seed bills through the issuance use case but drops its events: no broker, no worker.</summary>
    private sealed class DroppedEvents : IEventPublisher
    {
        public ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}

/// <summary>A seeded tenant: its fixed ids, its station and the operator login the journeys sign in with.</summary>
public sealed record SeededTenant(
    string TenantId,
    string StationName,
    string ChargePointId,
    string LoginEmail,
    string LoginPassword);

/// <summary>
/// Skips the journeys that read the seeded busy month when the run did not ensure it, so a staging
/// run that leaves the target's data alone (<c>ProtoTest:Seed=off</c>) reports those journeys as
/// skipped rather than failing on missing seed data.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequiresSeededMonthAttribute : ProtoAttribute, IProtoSkipCondition
{
    public string? GetSkipReason(ProtoHost host)
        => SeededMonth.IsSeeded
            ? null
            : "The seeded busy month is absent: this run did not seed the target " +
              $"('{SeededMonth.SeedSettingKey}' is off), so there are no seeded rows to read.";
}
