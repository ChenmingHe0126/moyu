using System.Collections.Generic;
using Moyu.Core;

namespace Moyu.ThreeD
{
  /// <summary>Immutable snapshot of the 3D optimiser state handed from the solver to the visualiser.</summary>
  public sealed class Result3D
  {
    public VoxelDomain3D Domain;
    public ElementKind[] Kinds;
    /// <summary>Physical density per voxel ((k·Ny + j)·Nx + i), 0 for outside voxels.</summary>
    public double[] Density;
    public int Iteration;
    public double Compliance;
    public double Volume;
    public double Change;
    public bool Converged;
    public double[] History;
    public List<LoadMark3D> Loads;
    public List<SupportMark3D> Supports;
    /// <summary>Changes whenever the optimiser state changes; lets the visualiser cache its meshes.</summary>
    public int Revision;

    public override string ToString() =>
      $"TopOpt 3D result ({Domain.Grid.Nx}×{Domain.Grid.Ny}×{Domain.Grid.Nz}, iteration {Iteration}, compliance {Compliance:G5}, volume {Volume:P1}{(Converged ? ", converged" : "")})";
  }
}
