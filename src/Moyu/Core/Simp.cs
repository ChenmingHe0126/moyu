using System;

namespace Moyu.Core
{
  public enum ElementKind : byte
  {
    /// <summary>Free design element.</summary>
    Design = 0,
    /// <summary>Passive element that always holds material.</summary>
    Solid = 1,
    /// <summary>Passive element inside the boundary that is always empty.</summary>
    Void = 2,
    /// <summary>Element outside the design boundary; empty and excluded from the volume.</summary>
    Outside = 3,
  }

  public enum FilterKind
  {
    Density,
    Sensitivity,
  }

  /// <summary>Element states, loads, supports and material of a compliance problem, independent of dimension.</summary>
  public abstract class ProblemBase
  {
    public readonly ElementKind[] Kinds;
    public readonly double[] Force;
    public readonly bool[] Fixed;

    public double YoungsModulus = 1.0;
    public double PoissonRatio = 0.3;
    /// <summary>Stiffness of empty elements relative to <see cref="YoungsModulus"/>.</summary>
    public double MinStiffnessRatio = 1e-9;

    protected ProblemBase(int elementCount, int dofCount)
    {
      Kinds = new ElementKind[elementCount];
      Force = new double[dofCount];
      Fixed = new bool[dofCount];
    }

    public int ElementCount => Kinds.Length;
  }

  public sealed class SimpSettings
  {
    public double VolumeFraction = 0.4;
    public double Penalty = 3.0;
    /// <summary>Filter radius in elements.</summary>
    public double FilterRadius = 2.0;
    public FilterKind Filter = FilterKind.Density;
    public double MoveLimit = 0.2;
    /// <summary>Stop when the largest density change of an iteration drops below this.</summary>
    public double Tolerance = 0.01;
    /// <summary>Also stop when compliance changed less than this (relative) in each of the last 10 iterations.</summary>
    public double ObjectiveTolerance = 1e-4;
    /// <summary>Iteration budget, counted from the last change of the problem or settings.</summary>
    public int MaxIterations = 200;

    public SimpSettings Clone() => (SimpSettings)MemberwiseClone();

    public bool SameAs(SimpSettings o) =>
      o != null && VolumeFraction == o.VolumeFraction && Penalty == o.Penalty && FilterRadius == o.FilterRadius &&
      Filter == o.Filter && MoveLimit == o.MoveLimit && Tolerance == o.Tolerance &&
      ObjectiveTolerance == o.ObjectiveTolerance && MaxIterations == o.MaxIterations;
  }
}
