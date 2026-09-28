namespace OpenCsms.Benchmarks;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Writes one harness run as the two files a reader can trust: <c>results.json</c> for tooling and
/// <c>results.md</c> for the pull request and the docs. The Markdown carries the method up front -
/// machine, runtime, commands, the pinned clock - so the numbers cannot drift away from their
/// context.
/// </summary>
public static class ResultsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(BenchmarkResults results)
    {
        var directory = results.Artifacts
            ?? throw new InvalidOperationException("The results have no artifact directory.");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "results.json"),
            JsonSerializer.Serialize(results, JsonOptions));

        var markdown = new StringBuilder();
        markdown.AppendLine("# OpenCSMS benchmark");
        markdown.AppendLine();
        markdown.AppendLine($"- Run at: {results.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC");
        markdown.AppendLine($"- Machine: {results.Machine}");
        markdown.AppendLine($"- .NET runtime: {results.DotnetVersion}");
        markdown.AppendLine($"- Command: `{results.Command}`");
        markdown.AppendLine("- Clock: pinned to 2030-06-15 12:00:00 UTC (the suite's run seed)");
        markdown.AppendLine("- App: the product's own API and billing worker; PostgreSQL and RabbitMQ from the environment");
        markdown.AppendLine();

        if (results.HealthCheck is { } health)
        {
            markdown.AppendLine("## Per-test overhead (one GET /healthz through the test cycle)");
            markdown.AppendLine();
            markdown.AppendLine("| Mode | Median | p95 | start | call | complete |");
            markdown.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
            Row(markdown, "ProtoTest, tracing on", health.ProtoTestWithTracing);
            Row(markdown, "ProtoTest, tracing off", health.ProtoTestWithoutTracing);
            Row(markdown, "Raw WebApplicationFactory", health.Raw);
            markdown.AppendLine();
            markdown.AppendLine(startupNote(health));
            markdown.AppendLine();
        }

        if (results.Startup is { } startup)
        {
            markdown.AppendLine("## Suite startup (build, start, first completed request)");
            markdown.AppendLine();
            markdown.AppendLine("| Mode | Median |");
            markdown.AppendLine("| --- | ---: |");
            markdown.AppendLine($"| ProtoTest, tracing on | {Ms(startup.ProtoTestWithTracingMs)} |");
            markdown.AppendLine($"| ProtoTest, tracing off | {Ms(startup.ProtoTestWithoutTracingMs)} |");
            markdown.AppendLine($"| Raw WebApplicationFactory | {Ms(startup.RawMs)} |");
            markdown.AppendLine();
            markdown.AppendLine($"Median of {startup.Rounds} rounds each; each round builds a fresh host.");
            markdown.AppendLine();
        }

        if (results.Journey is { } journey)
        {
            markdown.AppendLine("## Seeded charging journeys");
            markdown.AppendLine();
            markdown.AppendLine($"- Total: {journey.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");
            markdown.AppendLine($"- Per test: median {Ms(journey.Overhead.MedianMs)}, p95 {Ms(journey.Overhead.P95Ms)}, mean {Ms(journey.Overhead.MeanMs)}");
            markdown.AppendLine($"- Trace: `{Path.GetFileName(journey.TracePath)}` ({Bytes(journey.TraceBytes)})");
            markdown.AppendLine();
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "results.md"), markdown.ToString());
        Console.WriteLine($"[benchmark] wrote {Path.Combine(directory, "results.json")} and results.md");
    }

    private static void Row(StringBuilder markdown, string mode, Overhead overhead) =>
        markdown.AppendLine(
            $"| {mode} | {Ms(overhead.MedianMs)} | {Ms(overhead.P95Ms)} | {Ms(overhead.StartMedianMs)} | " +
            $"{Ms(overhead.CallMedianMs)} | {Ms(overhead.CompleteMedianMs)} |");

    private static string startupNote(HealthCheckMeasurement health)
    {
        var slower = health.ProtoTestWithTracing.MedianMs - health.Raw.MedianMs;
        var withoutTracing = health.ProtoTestWithoutTracing.MedianMs - health.Raw.MedianMs;
        return "ProtoTest is slower per test: " +
               $"+{Ms(withoutTracing)} without tracing and +{Ms(slower)} with tracing against the raw factory.";
    }

    private static string Ms(double value) => value.ToString("F3", CultureInfo.InvariantCulture) + " ms";

    private static string Bytes(long value) => value.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
}
