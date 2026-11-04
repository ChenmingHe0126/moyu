using Moyu.Games.Swift.Core;

namespace Moyu.Games.Swift
{
  /// <summary>What Storey Grid hands to Airflow Study: the course settings and an optional fixed seed.</summary>
  public sealed class FlightSpec
  {
    public readonly SwiftSettings Settings;
    /// <summary>0 draws a new course for every flight.</summary>
    public readonly int Seed;

    public FlightSpec(SwiftSettings settings, int seed)
    {
      Settings = settings;
      Seed = seed;
    }

    public static FlightSpec Default => new FlightSpec(new SwiftSettings(), 0);

    public override string ToString() =>
      $"{Settings.Exposure} exposure, {Settings.Mode}, opening ×{Settings.Opening:F2}, bay ×{Settings.Bay:F2}" + (Seed != 0 ? $", seed {Seed}" : "");
  }
}
