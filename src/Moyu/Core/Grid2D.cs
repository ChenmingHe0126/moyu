using System;

namespace Moyu.Core
{
  /// <summary>
  /// Regular pixel grid of square bilinear (Q4) elements in grid units (1 element = 1 unit).
  /// Elements are indexed row-major, e = j * Nx + i, with i along +X and j along +Y.
  /// Nodes are numbered along the shorter side first to keep the stiffness bandwidth small.
  /// </summary>
  public sealed class Grid2D
  {
    public readonly int Nx;
    public readonly int Ny;
    readonly bool _columnMajor;

    public Grid2D(int nx, int ny)
    {
      if (nx < 1 || ny < 1) throw new ArgumentOutOfRangeException(nameof(nx), "Grid needs at least one element in each direction.");
      Nx = nx;
      Ny = ny;
      _columnMajor = ny <= nx;
    }

    public int ElementCount => Nx * Ny;
    public int NodeCount => (Nx + 1) * (Ny + 1);
    public int DofCount => 2 * NodeCount;

    public int Element(int i, int j) => j * Nx + i;

    public int Node(int i, int j) => _columnMajor ? i * (Ny + 1) + j : j * (Nx + 1) + i;

    public void NodeIJ(int node, out int i, out int j)
    {
      if (_columnMajor) { i = node / (Ny + 1); j = node % (Ny + 1); }
      else { j = node / (Nx + 1); i = node % (Nx + 1); }
    }

    /// <summary>Half bandwidth + 1 (diagonal included) of the assembled stiffness matrix.</summary>
    public int BandWidth => 2 * ((_columnMajor ? Ny : Nx) + 2) + 2;

    /// <summary>Element DOFs in the order LL, LR, UR, UL with (ux, uy) per node, matching <see cref="Q4Element"/>.</summary>
    public int[] BuildElementDofs()
    {
      var edof = new int[ElementCount * 8];
      for (int j = 0; j < Ny; j++)
        for (int i = 0; i < Nx; i++)
        {
          int e = Element(i, j) * 8;
          int n0 = Node(i, j), n1 = Node(i + 1, j), n2 = Node(i + 1, j + 1), n3 = Node(i, j + 1);
          edof[e + 0] = 2 * n0; edof[e + 1] = 2 * n0 + 1;
          edof[e + 2] = 2 * n1; edof[e + 3] = 2 * n1 + 1;
          edof[e + 4] = 2 * n2; edof[e + 5] = 2 * n2 + 1;
          edof[e + 6] = 2 * n3; edof[e + 7] = 2 * n3 + 1;
        }
      return edof;
    }
  }
}
