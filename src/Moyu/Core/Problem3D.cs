namespace Moyu.Core
{
  /// <summary>Rhino-free description of a 3D compliance problem on a voxel grid.</summary>
  public sealed class Problem3D : ProblemBase
  {
    public readonly Grid3D Grid;

    public Problem3D(Grid3D grid) : base(grid.ElementCount, grid.DofCount)
    {
      Grid = grid;
    }

    /// <summary>True for nodes touching at least one voxel that can hold material (design or solid).</summary>
    public bool[] ActiveNodes()
    {
      var active = new bool[Grid.NodeCount];
      for (int k = 0; k < Grid.Nz; k++)
        for (int j = 0; j < Grid.Ny; j++)
          for (int i = 0; i < Grid.Nx; i++)
          {
            var kind = Kinds[Grid.Element(i, j, k)];
            if (kind != ElementKind.Design && kind != ElementKind.Solid) continue;
            for (int n = 0; n < 8; n++)
              active[Grid.Node(i + Grid3D.Corner[n, 0], j + Grid3D.Corner[n, 1], k + Grid3D.Corner[n, 2])] = true;
          }
      return active;
    }
  }
}
