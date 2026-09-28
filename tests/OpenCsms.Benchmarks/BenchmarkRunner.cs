namespace OpenCsms.Benchmarks;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenCsms.Contracts;
using OpenCsms.Infrastructure;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Hosting;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Rest;
using BillingWorker = OpenCsms.Billing.Worker.Program;
using CsmsApi = OpenCsms.Api.Program;

/// <summary>
/// Runs the benchmark inside its harness host: the same entry point shape as the worker, so
/// ProtoTest's worker resolver can discover it. The arguments come from the process command line;
/// the harness host itself is built with an empty argument list, so the runner's own switches never
/// reach host configuration.
/// </summary>
public sealed class BenchmarkHostedService(IHostApplicationLifetime lifetime) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = StartCoreAsync();
        await Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task StartCoreAsync()
    {
        var exitCode = 1;
        try
        {
            exitCode = await BenchmarkRunner.RunAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[benchmark] failed: {exception}");
        }
        finally
        {
            Environment.ExitCode = exitCode;
            lifetime.StopApplication();
        }
    }
}

/// <summary>
/// The OpenCSMS benchmark: a seeded run of real charging journeys on the product, measured against
/// the same application behind a raw <see cref="WebApplicationFactory{TEntryPoint}"/>. One run
/// reports the per-test overhead (lifecycle, context and tracing), the suite startup through the
/// first completed request with tracing on and off, and the journey's own numbers; a journey run
/// with tracing on leaves the trace file the trace-size table reads. The numbers are written as
/// JSON and Markdown under <c>artifacts/benchmarks/&lt;timestamp&gt;/</c>.
///
/// The app is the product itself: the same API entry point, the billing worker host and the real
/// PostgreSQL and RabbitMQ addresses from the environment. Provisioning goes through the REST front
/// door like the suite's does, and nothing reaches into the store directly.
/// </summary>
public static class BenchmarkRunner
{
    /// <summary>The run's pinned instant, the suite's own seed, so the numbers describe the suite's environment.</summary>
    private static readonly DateTimeOffset PinnedInstant = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] EnvironmentKeys =
    [
        "ConnectionStrings__Csms",
        "Messaging__RabbitMq__ConnectionString",
        "ProtoTest__Messaging__RabbitMq__ConnectionString"
    ];

    private const string MachineHeader = "X-Api-Key";

    private const int StartupRounds = 5;

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var options = BenchmarkOptions.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 2;
        }

        foreach (var key in EnvironmentKeys)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Console.Error.WriteLine(
                    $"The benchmark needs the running opencsms-postgres/opencsms-rabbitmq stack; '{key}' is not set.");
                return 2;
            }
        }

        // The raw factory discovers a content root from MvcTestingAppManifest.json, which the build
        // copies beside the harness assemblies, and would otherwise land on a directory that does not
        // exist. The API's content root only serves the dashboard bundle, which the benchmark does not
        // need. The output path is resolved before the directory changes.
        var artifactRoot = Path.GetFullPath(options.OutputDirectory, RepositoryRoot());
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        var results = new BenchmarkResults
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Machine = DescribeMachine(),
            DotnetVersion = Environment.Version.ToString(),
            Command = "dotnet run -c Release --project tests/OpenCsms.Benchmarks -- " + string.Join(' ', args)
        };
        var artifactDirectory = CreateArtifactDirectory(artifactRoot);
        results.Artifacts = artifactDirectory;

        Console.WriteLine($"[benchmark] machine={results.Machine} dotnet={results.DotnetVersion}");
        Console.WriteLine($"[benchmark] artifacts={artifactDirectory}");

        if (options.Runs(BenchmarkMode.Overhead))
        {
            results.HealthCheck = await MeasureHealthCheckAsync(options, artifactDirectory);
        }

        if (options.Runs(BenchmarkMode.Startup))
        {
            results.Startup = await MeasureStartupAsync(artifactDirectory);
        }

        if (options.Runs(BenchmarkMode.Journeys))
        {
            results.Journey = await MeasureJourneysAsync(options, artifactDirectory);
        }

        await ResultsWriter.WriteAsync(results);
        Console.WriteLine("[benchmark] done");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage: dotnet run -c Release --project tests/OpenCsms.Benchmarks -- --mode overhead|startup|journeys|all");
        Console.WriteLine("       [--iterations 1000] [--warmup 100] [--journeys 1000] [--output artifacts/benchmarks]");
        Console.WriteLine();
        Console.WriteLine("The PostgreSQL and RabbitMQ addresses come from the environment:");
        Console.WriteLine("  ConnectionStrings__Csms, Messaging__RabbitMq__ConnectionString, ProtoTest__Messaging__RabbitMq__ConnectionString");
        Console.WriteLine();
        Console.WriteLine("Reuse the documented persistent rehearsal stack or any environment that provides the three keys.");
    }

    /// <summary>
    /// Group one: the same health request through a full ProtoTest test cycle - start, REST call,
    /// complete - with tracing on and off, against the raw factory equivalent. Both sides build the
    /// application per test, so the delta is the framework's lifecycle, context and trace machinery.
    /// </summary>
    private static async Task<HealthCheckMeasurement> MeasureHealthCheckAsync(
        BenchmarkOptions options,
        string artifactDirectory)
    {
        Console.WriteLine($"[benchmark] health-check: {options.Warmup} warmup + {options.Iterations} measured per mode");

        var protoWithTracing = await MeasureProtoHealthAsync(options, tracing: true, artifactDirectory);
        Report("health-check/prototest-tracing", protoWithTracing);

        var protoWithoutTracing = await MeasureProtoHealthAsync(options, tracing: false, artifactDirectory);
        Report("health-check/prototest-no-tracing", protoWithoutTracing);

        var raw = await MeasureRawHealthAsync(options);
        Report("health-check/raw-webapplicationfactory", raw);

        return new HealthCheckMeasurement
        {
            ProtoTestWithTracing = protoWithTracing,
            ProtoTestWithoutTracing = protoWithoutTracing,
            Raw = raw
        };
    }

    private static async Task<Overhead> MeasureProtoHealthAsync(
        BenchmarkOptions options,
        bool tracing,
        string artifactDirectory)
    {
        var tracePath = Path.Combine(artifactDirectory, $"health-check-{(tracing ? "tracing" : "no-tracing")}.prototrace");
        var builder = BuildProtoHost(tracing, tracePath, includeWorker: false);
        await using var host = builder.Build();
        await host.StartAsync();
        try
        {
            for (var index = 0; index < options.Warmup; index++)
            {
                await RunHealthCheckAsync(host, index, captureSample: false);
            }

            var samples = new List<Sample>(options.Iterations);
            for (var index = 0; index < options.Iterations; index++)
            {
                samples.Add(await RunHealthCheckAsync(host, options.Warmup + index, captureSample: true));
            }

            return Overhead.From(samples);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<Overhead> MeasureRawHealthAsync(BenchmarkOptions options)
    {
        // One factory for the loop, mirroring the ProtoTest side's per-run server: building the
        // application is a run cost on both sides, so each test pays for a client and the request.
        await using var server = new CsmsServer();
        for (var index = 0; index < options.Warmup; index++)
        {
            await RunRawHealthAsync(server);
        }

        var samples = new List<Sample>(options.Iterations);
        for (var index = 0; index < options.Iterations; index++)
        {
            samples.Add(await RunRawHealthAsync(server));
        }

        return Overhead.From(samples);
    }

    private static readonly ProtoAttribute[] ApplicationAttributes =
        [new ProtoTest.Core.ApplicationAttribute("Csms")];

    private static async Task<Sample> RunHealthCheckAsync(ProtoHost host, int sequence, bool captureSample)
    {
        var total = Stopwatch.StartNew();
        var context = await host.StartTestAsync(
            "health-check",
            sequence.ToString("D10", CultureInfo.InvariantCulture),
            HealthCheckMethod,
            ApplicationAttributes);
        var start = total.Elapsed.TotalMilliseconds;

        var call = Stopwatch.StartNew();
        using (var response = await context.Rest().GetAsync("/healthz"))
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"The health check answered {response.StatusCode}.");
            }
        }

        call.Stop();

        var complete = Stopwatch.StartNew();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        complete.Stop();
        total.Stop();

        return captureSample
            ? new Sample
            {
                TotalMs = total.Elapsed.TotalMilliseconds,
                StartMs = start,
                CallMs = call.Elapsed.TotalMilliseconds,
                CompleteMs = complete.Elapsed.TotalMilliseconds
            }
            : Sample.Empty;
    }

    private static async Task<Sample> RunRawHealthAsync(CsmsServer server)
    {
        var total = Stopwatch.StartNew();
        using var client = server.CreateClient();
        var start = total.Elapsed.TotalMilliseconds;

        var call = Stopwatch.StartNew();
        using (var response = await client.GetAsync("/healthz"))
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"The raw health check answered {response.StatusCode}.");
            }
        }

        call.Stop();
        total.Stop();

        return new Sample
        {
            TotalMs = total.Elapsed.TotalMilliseconds,
            StartMs = start,
            CallMs = call.Elapsed.TotalMilliseconds,
            CompleteMs = 0
        };
    }

    /// <summary>
    /// Group two: the whole journey - provision, charge, end, invoice - once per test on the product
    /// host, with the billing worker, the broker and the database in play. The run leaves the trace
    /// the trace-size table reads.
    /// </summary>
    private static async Task<JourneyMeasurement> MeasureJourneysAsync(
        BenchmarkOptions options,
        string artifactDirectory)
    {
        Console.WriteLine($"[benchmark] journeys: {options.Journeys} seeded charging journeys, tracing on");

        var tracePath = Path.Combine(artifactDirectory, $"journeys-{options.Journeys}.prototrace");
        var builder = BuildProtoHost(tracing: true, tracePath, includeWorker: true);
        await using var host = builder.Build();
        await host.StartAsync();
        var seed = await SeedJourneyOperatorAsync(host);
        Console.WriteLine($"[benchmark] journeys: provisioned tenant {seed.TenantId}");

        var samples = new List<Sample>(options.Journeys);
        for (var index = 0; index < options.Journeys; index++)
        {
            samples.Add(await RunJourneyAsync(host, seed, index));
        }

        // The trace is written when the run stops, so its size is a fact only afterwards.
        await host.StopAsync();

        var measurement = JourneyMeasurement.From(samples);
        measurement.TracePath = tracePath;
        measurement.TraceBytes = File.Exists(tracePath) ? new FileInfo(tracePath).Length : 0;
        return measurement;
    }

    private static async Task<Sample> RunJourneyAsync(ProtoHost host, SeedOperator seed, int index)
    {
        var total = Stopwatch.StartNew();
        var context = await host.StartTestAsync(
            "charging-journey",
            Sequence(1_000_000 + index),
            JourneyMethod,
            ApplicationAttributes);
        var start = total.Elapsed.TotalMilliseconds;

        var call = Stopwatch.StartNew();
        var energyKwh = 5m + (index % 21);

        Guid sessionId;
        using (var started = await context.Rest()
                   .Header(MachineHeader, seed.ApiKey)
                   .Body(new { stationId = seed.StationId, connectorId = 1 })
                   .PostAsync("/api/sessions"))
        {
            started.Should.HaveHttpStatus(HttpStatusCode.Created);
            sessionId = started.ReadAsJson<SessionResponse>()!.Id;
        }

        using (var meter = await context.Rest()
                   .Header(MachineHeader, seed.ApiKey)
                   .Body(new { totalKwh = energyKwh })
                   .PostAsync($"/api/sessions/{sessionId}/meter-values"))
        {
            meter.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        using (var ended = await context.Rest()
                   .Header(MachineHeader, seed.ApiKey)
                   .PostAsync($"/api/sessions/{sessionId}/end"))
        {
            ended.Should.HaveHttpStatus(HttpStatusCode.OK);
        }

        var invoice = await AwaitInvoiceAsync(context, seed.ApiKey, sessionId);
        if (invoice.SessionId != sessionId)
        {
            throw new InvalidOperationException($"The invoice names session '{invoice.SessionId}', not '{sessionId}'.");
        }

        call.Stop();

        var complete = Stopwatch.StartNew();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        complete.Stop();
        total.Stop();

        return new Sample
        {
            TotalMs = total.Elapsed.TotalMilliseconds,
            StartMs = start,
            CallMs = call.Elapsed.TotalMilliseconds,
            CompleteMs = complete.Elapsed.TotalMilliseconds
        };
    }

    private static async Task<InvoiceResponse> AwaitInvoiceAsync(
        ProtoExecutionContext context,
        string apiKey,
        Guid sessionId)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            using var response = await context.Rest()
                .Header(MachineHeader, apiKey)
                .GetAsync($"/api/sessions/{sessionId}/invoice");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response.ReadAsJson<InvoiceResponse>()!;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException($"No invoice arrived for session '{sessionId}' within 30 seconds.");
    }

    /// <summary>
    /// Group three: suite startup through the first completed request, with tracing on and off,
    /// against the raw factory equivalent. Each round builds, starts, runs one health check and stops.
    /// </summary>
    private static async Task<StartupMeasurement> MeasureStartupAsync(string artifactDirectory)
    {
        Console.WriteLine($"[benchmark] startup: {StartupRounds} rounds per mode");

        var withTracing = new List<double>(StartupRounds);
        var withoutTracing = new List<double>(StartupRounds);
        var raw = new List<double>(StartupRounds);

        for (var round = 0; round < StartupRounds; round++)
        {
            var total = Stopwatch.StartNew();
            var builder = BuildProtoHost(
                tracing: true,
                Path.Combine(artifactDirectory, $"startup-tracing-{round}.prototrace"),
                includeWorker: false);
            await using (var host = builder.Build())
            {
                await host.StartAsync();
                await RunHealthCheckAsync(host, 9_000_000 + round, captureSample: false);
                await host.StopAsync();
            }

            total.Stop();
            withTracing.Add(total.Elapsed.TotalMilliseconds);
        }

        for (var round = 0; round < StartupRounds; round++)
        {
            var total = Stopwatch.StartNew();
            var builder = BuildProtoHost(
                tracing: false,
                Path.Combine(artifactDirectory, $"startup-no-tracing-{round}.prototrace"),
                includeWorker: false);
            await using (var host = builder.Build())
            {
                await host.StartAsync();
                await RunHealthCheckAsync(host, 9_100_000 + round, captureSample: false);
                await host.StopAsync();
            }

            total.Stop();
            withoutTracing.Add(total.Elapsed.TotalMilliseconds);
        }

        for (var round = 0; round < StartupRounds; round++)
        {
            var total = Stopwatch.StartNew();
            await using (var server = new CsmsServer())
            using (var client = server.CreateClient())
            {
                using var response = await client.GetAsync("/healthz");
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new InvalidOperationException($"The raw startup health check answered {response.StatusCode}.");
                }
            }

            total.Stop();
            raw.Add(total.Elapsed.TotalMilliseconds);
        }

        var measurement = new StartupMeasurement
        {
            Rounds = StartupRounds,
            ProtoTestWithTracingMs = Median(withTracing),
            ProtoTestWithoutTracingMs = Median(withoutTracing),
            RawMs = Median(raw)
        };
        Console.WriteLine(
            $"[benchmark] startup: prototest-tracing={measurement.ProtoTestWithTracingMs:F0} ms " +
            $"prototest-no-tracing={measurement.ProtoTestWithoutTracingMs:F0} ms raw={measurement.RawMs:F0} ms");
        return measurement;
    }

    /// <summary>Registers the benchmark's own tenant, tariff and station through the product's REST front door.</summary>
    private static async Task<SeedOperator> SeedJourneyOperatorAsync(ProtoHost host)
    {
        var context = await host.StartTestAsync(
            "benchmark-seed",
            Sequence(9_500_000),
            SeedMethod,
            ApplicationAttributes);
        try
        {
            using var tenant = await context.Rest()
                .WithoutAuth()
                .Body(new { name = $"benchmark-{Guid.NewGuid():N}" })
                .PostAsync("/api/tenants");
            tenant.Should.HaveHttpStatus(HttpStatusCode.Created);
            var registered = tenant.ReadAsJson<TenantRegistrationResponse>()!;

            using var tariff = await context.Rest()
                .WithoutAuth()
                .Header(MachineHeader, registered.ApiKey)
                .Body(new
                {
                    name = $"benchmark-tariff-{Guid.NewGuid():N}",
                    energyPricePerKwh = 0.40m,
                    startFee = 1.50m,
                    idleFeePerHour = 2.00m,
                    idleGracePeriod = TimeSpan.FromMinutes(10)
                })
                .PostAsync("/api/tariffs");
            tariff.Should.HaveHttpStatus(HttpStatusCode.Created);
            var tariffId = tariff.ReadAsJson<TariffResponse>()!.Id;

            using var station = await context.Rest()
                .WithoutAuth()
                .Header(MachineHeader, registered.ApiKey)
                .Body(new
                {
                    chargePointId = $"benchmark-cp-{Guid.NewGuid():N}",
                    name = "Benchmark Station",
                    connectorCount = 1,
                    tariffId
                })
                .PostAsync("/api/stations");
            station.Should.HaveHttpStatus(HttpStatusCode.Created);
            var stationId = station.ReadAsJson<StationResponse>()!.Id;

            return new SeedOperator(registered.TenantId, registered.ApiKey, stationId);
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }
    }

    /// <summary>
    /// Builds the benchmark's ProtoTest host: the API with the billing worker, the suite's own
    /// registration chains but pointed at the environment's addresses, and no containers of its own.
    /// </summary>
    private static IProtoHostBuilder BuildProtoHost(bool tracing, string tracePath, bool includeWorker)
    {
        var builder = new ProtoHostBuilder()
            .ConfigureClock(new ProtoClock(PinnedInstant))
            .ConfigureTracing(options =>
            {
                options.Enabled = tracing;
                options.OutputPath = tracePath;
                options.EmbedSources = false;
                options.EmbedArtifacts = false;
            })
            .ConfigureAppConfiguration(configuration => configuration
                .AddInMemoryCollection(EnvironmentSettings()));

        builder.AddInfrastructure(
            "CsmsDatabase",
            chain => chain.UseConfigured(),
            CsmsInfrastructureExtensions.ConnectionStringKey);
        builder.AddInfrastructure(
            "CsmsBroker",
            chain => chain.UseConfigured(),
            RabbitMqOptions.ConnectionStringSetting,
            "Messaging:RabbitMq:ConnectionString");

        builder.AddMessaging(messaging => messaging
            .Tap(CsmsEvents.Exchange, CsmsEvents.DeadLetterExchange, CsmsEvents.NotificationsDeadLetterExchange)
            .UseRabbitMq());

        builder.AddApplication("Csms", app =>
        {
            app.AddAspNetCoreServer<CsmsApi>();
            if (includeWorker)
            {
                app.AddWorkerHost<BillingWorker>("Billing");
            }

            app.AddRest(rest => rest.AddClient("Csms"));
        });

        return builder;
    }

    /// <summary>Turns the environment's double-underscore keys into configuration entries, as a deployment host does.</summary>
    private static Dictionary<string, string?> EnvironmentSettings()
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.Contains("__", StringComparison.Ordinal))
            {
                settings[key.Replace("__", ":", StringComparison.Ordinal)] = (string?)entry.Value;
            }
        }

        return settings;
    }

    /// <summary>
    /// The raw baseline: the product's own entry point behind <see cref="WebApplicationFactory{TEntryPoint}"/>,
    /// with the environment's connection keys and nothing of ProtoTest. One instance per iteration,
    /// mirroring the per-test in-process server the ProtoTest side builds.
    /// </summary>
    private sealed class CsmsServer : IAsyncDisposable
    {
        private readonly WebApplicationFactory<CsmsApi> _factory;

        public CsmsServer()
        {
            // The harness is not a test project, so the factory's content-root discovery needs the
            // harness directory as the working directory: the API's own manifest is copied there and
            // names the API project directory. RunRawHealthAsync sets the directory for the loop.
            _factory = new WebApplicationFactory<CsmsApi>()
                .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration => configuration
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [CsmsInfrastructureExtensions.ConnectionStringKey] =
                            Environment.GetEnvironmentVariable("ConnectionStrings__Csms"),
                        ["Messaging:RabbitMq:ConnectionString"] =
                            Environment.GetEnvironmentVariable("Messaging__RabbitMq__ConnectionString")
                    })));
        }

        public HttpClient CreateClient() => _factory.CreateClient();

        public ValueTask DisposeAsync() => _factory.DisposeAsync();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenCsms.slnx")))
            {
                return directory.FullName;
            }
        }

        return AppContext.BaseDirectory;
    }

    private static void Report(string mode, Overhead overhead)
    {
        Console.WriteLine(
            $"[benchmark] {mode}: median={overhead.MedianMs:F3} ms p95={overhead.P95Ms:F3} ms " +
            $"start={overhead.StartMedianMs:F3} ms call={overhead.CallMedianMs:F3} ms " +
            $"complete={overhead.CompleteMedianMs:F3} ms");
    }

    private static double Median(List<double> values)
    {
        var sorted = new List<double>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    private static string Sequence(int number) => number.ToString("D10", CultureInfo.InvariantCulture);

    private static string DescribeMachine()
    {
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024L * 1024L);
        return $"{RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture} / " +
               $"{Environment.ProcessorCount} cores / {memory} GiB";
    }

    private static string CreateArtifactDirectory(string root)
    {
        var directory = Path.Combine(root, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static readonly System.Reflection.MethodInfo HealthCheckMethod =
        typeof(BenchmarkRunner).GetMethod(nameof(HealthCheckPlaceholder), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static readonly System.Reflection.MethodInfo JourneyMethod =
        typeof(BenchmarkRunner).GetMethod(nameof(JourneyPlaceholder), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static readonly System.Reflection.MethodInfo SeedMethod =
        typeof(BenchmarkRunner).GetMethod(nameof(SeedPlaceholder), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static void HealthCheckPlaceholder()
    {
    }

    private static void JourneyPlaceholder()
    {
    }

    private static void SeedPlaceholder()
    {
    }
}


