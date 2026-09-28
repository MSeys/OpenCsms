namespace OpenCsms.Benchmarks;

/// <summary>One measured test cycle: the total plus the start, call and complete phases.</summary>
public sealed record Sample
{
    public static readonly Sample Empty = new();

    public double TotalMs { get; init; }

    public double StartMs { get; init; }

    public double CallMs { get; init; }

    public double CompleteMs { get; init; }
}

/// <summary>The distribution of a mode's samples.</summary>
public sealed record Overhead
{
    public required double MedianMs { get; init; }

    public required double MeanMs { get; init; }

    public required double P95Ms { get; init; }

    public required double StartMedianMs { get; init; }

    public required double CallMedianMs { get; init; }

    public required double CompleteMedianMs { get; init; }

    public static Overhead From(List<Sample> samples)
    {
        var sorted = samples.Select(sample => sample.TotalMs).Order().ToList();
        return new Overhead
        {
            MedianMs = sorted[sorted.Count / 2],
            MeanMs = sorted.Average(),
            P95Ms = sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1],
            StartMedianMs = Median(samples.Select(sample => sample.StartMs)),
            CallMedianMs = Median(samples.Select(sample => sample.CallMs)),
            CompleteMedianMs = Median(samples.Select(sample => sample.CompleteMs))
        };
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }
}
