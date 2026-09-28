namespace OpenCsms.Benchmarks;

using System.Globalization;

/// <summary>The measurement groups a run can execute.</summary>
public enum BenchmarkMode
{
    /// <summary>Per-test overhead through one health request, tracing on and off, against the raw factory.</summary>
    Overhead,

    /// <summary>Suite startup through the first completed request, tracing on and off, against the raw factory.</summary>
    Startup,

    /// <summary>Seeded charging journeys on the product host, leaving the trace file the size table reads.</summary>
    Journeys
}

/// <summary>The parsed command line. A null result from <see cref="Parse"/> is a usage error.</summary>
public sealed record BenchmarkOptions
{
    public IReadOnlyList<BenchmarkMode> Modes { get; init; } = [];

    public int Iterations { get; init; } = 1000;

    public int Warmup { get; init; } = 100;

    public int Journeys { get; init; } = 1000;

    public string OutputDirectory { get; init; } = Path.Combine("artifacts", "benchmarks");

    public bool Runs(BenchmarkMode mode) => Modes.Contains(mode);

    public static BenchmarkOptions? Parse(string[] args)
    {
        string? mode = null;
        var iterations = 1000;
        var warmup = 100;
        var journeys = 1000;
        var output = Path.Combine("artifacts", "benchmarks");

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode" when index + 1 < args.Length:
                    mode = args[++index];
                    break;
                case "--iterations" when index + 1 < args.Length:
                    iterations = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--warmup" when index + 1 < args.Length:
                    warmup = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--journeys" when index + 1 < args.Length:
                    journeys = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                default:
                    return null;
            }
        }

        var modes = mode?.ToLowerInvariant() switch
        {
            "overhead" => new[] { BenchmarkMode.Overhead },
            "startup" => new[] { BenchmarkMode.Startup },
            "journeys" => new[] { BenchmarkMode.Journeys },
            "all" => new[] { BenchmarkMode.Overhead, BenchmarkMode.Startup, BenchmarkMode.Journeys },
            _ => null
        };

        if (modes is null)
        {
            return null;
        }

        return new BenchmarkOptions
        {
            Modes = modes,
            Iterations = iterations,
            Warmup = warmup,
            Journeys = journeys,
            OutputDirectory = output
        };
    }
}

/// <summary>The journey's provisioned operator: the tenant, its machine key and its station.</summary>
public sealed record SeedOperator(string TenantId, string ApiKey, Guid StationId);

/// <summary>The health-check group: the same request under three modes.</summary>
public sealed record HealthCheckMeasurement
{
    public required Overhead ProtoTestWithTracing { get; init; }

    public required Overhead ProtoTestWithoutTracing { get; init; }

    public required Overhead Raw { get; init; }
}

/// <summary>The startup group: build, start and first completed request, by mode.</summary>
public sealed record StartupMeasurement
{
    public required int Rounds { get; init; }

    public required double ProtoTestWithTracingMs { get; init; }

    public required double ProtoTestWithoutTracingMs { get; init; }

    public required double RawMs { get; init; }
}

/// <summary>The journey group: the whole run's distribution plus the trace it left.</summary>
public sealed record JourneyMeasurement
{
    public required Overhead Overhead { get; init; }

    public required double TotalSeconds { get; init; }

    public string? TracePath { get; set; }

    public long TraceBytes { get; set; }

    public static JourneyMeasurement From(List<Sample> samples)
    {
        return new JourneyMeasurement
        {
            Overhead = Overhead.From(samples),
            TotalSeconds = samples.Sum(sample => sample.TotalMs) / 1000.0
        };
    }
}

/// <summary>Everything one harness process measured, written as JSON and Markdown.</summary>
public sealed record BenchmarkResults
{
    public required DateTimeOffset TimestampUtc { get; init; }

    public required string Machine { get; init; }

    public required string DotnetVersion { get; init; }

    public required string Command { get; init; }

    public string? Artifacts { get; set; }

    public HealthCheckMeasurement? HealthCheck { get; set; }

    public StartupMeasurement? Startup { get; set; }

    public JourneyMeasurement? Journey { get; set; }
}
