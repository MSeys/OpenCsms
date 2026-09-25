namespace OpenCsms.Suite.Support;

using ProtoTest.Core;

/// <summary>
/// The run's clock is pinned to one instant, far from any real run, so every timestamp the system
/// under test stamps is predictable. A product that ignores the injected <see cref="TimeProvider"/>
/// and reads the machine clock fails the journey's timestamp assertion instead of passing silently
/// (R1a-04).
/// </summary>
public static class SuiteClock
{
    /// <summary>The instant every test's clock is seeded from; fixed for the whole run.</summary>
    public static readonly DateTimeOffset Instant = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Creates the run's clock at <see cref="Instant"/>.</summary>
    public static ProtoClock Seed() => new(Instant);
}
