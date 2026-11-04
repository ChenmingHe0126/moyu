namespace Moyu.Core
{
  /// <summary>Rhino-free description of a 2D compliance problem on a pixel grid.</summary>
  public sealed class Problem2D : ProblemBase
  {
    public readonly Grid2D Grid;

    public Problem2D(Grid2D grid) : base(grid.ElementCount, grid.DofCount)
    {
      Grid = grid;
    }

    /// <summary>True for nodes touching at least one pixel that can hold material (design or solid).</summary>
    public bool[] ActiveNodes()
    {
      var active = new bool[Grid.NodeCount];
      for (int j = 0; j < Grid.Ny; j++)
        for (int i = 0; i < Grid.Nx; i++)
        {
          var kind = Kinds[Grid.Element(i, j)];
          if (kind != ElementKind.Design && kind != ElementKind.Solid) continue;
          active[Grid.Node(i, j)] = true;
          active[Grid.Node(i + 1, j)] = true;
          active[Grid.Node(i + 1, j + 1)] = true;
          active[Grid.Node(i, j + 1)] = true;
        }
      return active;
    }
  }
}
