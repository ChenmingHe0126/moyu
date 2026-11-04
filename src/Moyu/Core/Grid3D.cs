using System;

namespace Moyu.Core
{
  /// <summary>
  /// Regular voxel grid of unit-cube trilinear (H8) elements. Elements are indexed (k * Ny + j) * Nx + i.
  /// Nodes are numbered with the shortest axis varying fastest, which keeps a direct solve's bandwidth small.
  /// </summary>
  public sealed class Grid3D
  {
    public readonly int Nx;
    public readonly int Ny;
    public readonly int Nz;
    readonly int _sx, _sy, _sz;
    readonly int[] _order;

    /// <summary>Local node order of an element: bottom face counter-clockwise, then top face.</summary>
    public static readonly int[,] Corner =
    {
      { 0, 0, 0 }, { 1, 0, 0 }, { 1, 1, 0 }, { 0, 1, 0 },
      { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 },
    };

    public Grid3D(int nx, int ny, int nz)
    {
      if (nx < 1 || ny < 1 || nz < 1) throw new ArgumentOutOfRangeException(nameof(nx), "Grid needs at least one element in each direction.");
      Nx = nx;
      Ny = ny;
      Nz = nz;
      int[] n = { nx + 1, ny + 1, nz + 1 };
      _order = new[] { 0, 1, 2 };
      Array.Sort(new[] { n[0] * 3 + 0, n[1] * 3 + 1, n[2] * 3 + 2 }, _order);
      var stride = new int[3];
      stride[_order[0]] = 1;
      stride[_order[1]] = n[_order[0]];
      stride[_order[2]] = n[_order[0]] * n[_order[1]];
      _sx = stride[0];
      _sy = stride[1];
      _sz = stride[2];
    }

    public int ElementCount => Nx * Ny * Nz;
    public int NodeCount => (Nx + 1) * (Ny + 1) * (Nz + 1);
    public int DofCount => 3 * NodeCount;

    public int Element(int i, int j, int k) => (k * Ny + j) * Nx + i;

    public int Node(int i, int j, int k) => i * _sx + j * _sy + k * _sz;

    public void NodeIJK(int node, out int i, out int j, out int k)
    {
      var c = new int[3];
      var s = new[] { _sx, _sy, _sz };
      int rest = node;
      for (int a = 2; a >= 0; a--)
      {
        int axis = _order[a];
        c[axis] = rest / s[axis];
        rest %= s[axis];
      }
      i = c[0];
      j = c[1];
      k = c[2];
    }

    /// <summary>Half bandwidth + 1 (diagonal included) of the assembled stiffness matrix.</summary>
    public int BandWidth => 3 * (_sx + _sy + _sz) + 3;

    /// <summary>Element DOFs (24 per element, ux uy uz per node) in <see cref="Corner"/> order.</summary>
    public int[] BuildElementDofs()
    {
      var edof = new int[ElementCount * 24];
      for (int k = 0; k < Nz; k++)
        for (int j = 0; j < Ny; j++)
          for (int i = 0; i < Nx; i++)
          {
            int o = Element(i, j, k) * 24;
            for (int n = 0; n < 8; n++)
            {
              int node = Node(i + Corner[n, 0], j + Corner[n, 1], k + Corner[n, 2]);
              edof[o + 3 * n] = 3 * node;
              edof[o + 3 * n + 1] = 3 * node + 1;
              edof[o + 3 * n + 2] = 3 * node + 2;
            }
          }
      return edof;
    }
  }
}
