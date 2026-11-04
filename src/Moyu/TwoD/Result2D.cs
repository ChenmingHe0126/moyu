using System.Collections.Generic;
using Moyu.Core;

namespace Moyu.TwoD
{
  /// <summary>Immutable snapshot of the optimiser state handed from the solver to the visualiser.</summary>
  public sealed class Result2D
  {
    public PixelDomain2D Domain;
    public ElementKind[] Kinds;
    /// <summary>Physical density per pixel, row-major (j * Nx + i), 0 for outside pixels.</summary>
    public double[] Density;
    public int Iteration;
    public double Compliance;
    public double Volume;
    public double Change;
    public bool Converged;
    public double[] History;
    public List<LoadMark> Loads;
    public List<SupportMark> Supports;
    /// <summary>Changes whenever the optimiser state changes; lets the visualiser cache its meshes.</summary>
    public int Revision;

    public override string ToString() =>
      $"TopOpt 2D result ({Domain.Grid.Nx}×{Domain.Grid.Ny}, iteration {Iteration}, compliance {Compliance:G5}, volume {Volume:P1}{(Converged ? ", converged" : "")})";
  }
}
