using System;
using System.Collections.Generic;

namespace Moyu.Games.Snake.Core
{
  /// <summary>A cell of the play field, by its column (u) and row (v).</summary>
  public readonly struct Cell : IEquatable<Cell>
  {
    public readonly int U, V;

    public Cell(int u, int v)
    {
      U = u;
      V = v;
    }

    public bool Equals(Cell other) => U == other.U && V == other.V;
    public override bool Equals(object obj) => obj is Cell c && Equals(c);
    public override int GetHashCode() => unchecked(U * 7919 + V);
    public static bool operator ==(Cell a, Cell b) => a.Equals(b);
    public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
    public override string ToString() => $"({U}, {V})";
  }

  /// <summary>
  /// The play field: a surface's UV grid of Nu × Nv cells with each cell's centre in 3D. A closed direction wraps
  /// around (cylinder, torus); open edges are walls; blocked cells (trimmed away, degenerate) are obstacles.
  /// </summary>
  public sealed class SurfaceGrid
  {
    public readonly int Nu, Nv;
    public readonly bool WrapU, WrapV;
    readonly double[] _x, _y, _z;
    readonly bool[] _blocked;

    public SurfaceGrid(int nu, int nv, bool wrapU, bool wrapV, double[] x, double[] y, double[] z, bool[] blocked = null)
    {
      if (nu < 3 || nv < 3) throw new ArgumentOutOfRangeException(nameof(nu), "The field needs at least 3 × 3 cells.");
      int n = nu * nv;
      if (x.Length != n || y.Length != n || z.Length != n) throw new ArgumentException("One centre per cell is needed.");
      Nu = nu;
      Nv = nv;
      WrapU = wrapU;
      WrapV = wrapV;
      _x = x;
      _y = y;
      _z = z;
      _blocked = blocked ?? new bool[n];
    }

    /// <summary>A flat field in the XY plane, one unit per cell, z from <paramref name="height"/> (for tests and previews).</summary>
    public static SurfaceGrid Flat(int nu, int nv, bool wrapU = false, bool wrapV = false, Func<int, int, double> height = null, bool[] blocked = null)
    {
      int n = nu * nv;
      var x = new double[n];
      var y = new double[n];
      var z = new double[n];
      for (int v = 0; v < nv; v++)
        for (int u = 0; u < nu; u++)
        {
          int i = v * nu + u;
          x[i] = u + 0.5;
          y[i] = v + 0.5;
          z[i] = height?.Invoke(u, v) ?? 0;
        }
      return new SurfaceGrid(nu, nv, wrapU, wrapV, x, y, z, blocked);
    }

    public int Count => Nu * Nv;
    public int Index(Cell c) => c.V * Nu + c.U;
    public Cell CellAt(int index) => new Cell(index % Nu, index / Nu);
    public bool Contains(Cell c) => c.U >= 0 && c.U < Nu && c.V >= 0 && c.V < Nv;
    public bool IsBlocked(Cell c) => !Contains(c) || _blocked[Index(c)];
    public (double x, double y, double z) Centre(Cell c) { int i = Index(c); return (_x[i], _y[i], _z[i]); }
    public double Height(Cell c) => _z[Index(c)];

    /// <summary>The neighbour one step in (du, dv), wrapping across closed sides. False at a wall or an obstacle.</summary>
    public bool TryStep(Cell c, int du, int dv, out Cell next)
    {
      int u = c.U + du, v = c.V + dv;
      if (WrapU) u = ((u % Nu) + Nu) % Nu;
      if (WrapV) v = ((v % Nv) + Nv) % Nv;
      next = new Cell(u, v);
      return Contains(next) && !_blocked[Index(next)];
    }

    public double Distance(Cell a, Cell b)
    {
      var (ax, ay, az) = Centre(a);
      var (bx, by, bz) = Centre(b);
      return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));
    }

    /// <summary>Rise over run between two cells (positive uphill), measured along the surface step.</summary>
    public double Slope(Cell from, Cell to)
    {
      double dz = Height(to) - Height(from);
      double d = Distance(from, to);
      double run = Math.Sqrt(Math.Max(1e-12, d * d - dz * dz));
      return dz / run;
    }

    /// <summary>Steps between two cells along the grid, counting the short way round on closed sides.</summary>
    public int GridDistance(Cell a, Cell b)
    {
      int du = Math.Abs(a.U - b.U), dv = Math.Abs(a.V - b.V);
      if (WrapU) du = Math.Min(du, Nu - du);
      if (WrapV) dv = Math.Min(dv, Nv - dv);
      return du + dv;
    }

    /// <summary>Typical step length, for the renderer's "did it wrap?" test and sizing.</summary>
    public double MeanStep()
    {
      double sum = 0;
      int n = 0;
      for (int v = 0; v < Nv; v++)
        for (int u = 0; u + 1 < Nu; u++)
        {
          sum += Distance(new Cell(u, v), new Cell(u + 1, v));
          n++;
        }
      return n > 0 ? sum / n : 1;
    }

    public IEnumerable<Cell> FreeCells()
    {
      for (int i = 0; i < Count; i++)
        if (!_blocked[i]) yield return CellAt(i);
    }
  }
}
