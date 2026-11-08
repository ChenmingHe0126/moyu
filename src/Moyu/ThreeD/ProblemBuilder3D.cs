using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Core;
using Rhino.Geometry;

namespace Moyu.ThreeD
{
  /// <summary>Nodes a load acts on, in world space, for display.</summary>
  public sealed class LoadMark3D
  {
    public Point3d[] Nodes;
    public Vector3d Force;
  }

  public sealed class SupportMark3D
  {
    public Point3d[] Nodes;
    public bool FixX, FixY, FixZ;
  }

  /// <summary>Maps Rhino geometry conditions onto the voxel grid as a Rhino-free <see cref="Problem3D"/>.</summary>
  public static class ProblemBuilder3D
  {
    public sealed class Output
    {
      public Problem3D Problem;
      public readonly List<LoadMark3D> Loads = new List<LoadMark3D>();
      public readonly List<SupportMark3D> Supports = new List<SupportMark3D>();
      public readonly List<string> Warnings = new List<string>();
    }

    public static Output Build(VoxelDomain3D domain, IList<Load3D> loads, IList<Support3D> supports,
      IList<Area3D> areas, Material3D material, out string error)
    {
      error = null;
      var grid = domain.Grid;
      var result = new Output();
      var p = new Problem3D(grid)
      {
        // The solver's H8 stiffness is a unit cube's and a cube of edge h is h times as stiff: scaling E by the voxel
        // size keeps the compliance in world units at every resolution. The layout does not depend on it.
        YoungsModulus = material.YoungsModulus * domain.VoxelSize,
        PoissonRatio = material.PoissonRatio,
      };
      result.Problem = p;

      for (int e = 0; e < grid.ElementCount; e++)
        p.Kinds[e] = domain.Inside[e] ? ElementKind.Design : ElementKind.Outside;

      // Areas in input order; later ones win where they overlap. They never extend the boundary.
      foreach (var area in areas)
      {
        var mask = domain.Rasterize(new[] { area.Region });
        int hits = 0;
        for (int e = 0; e < mask.Length; e++)
          if (mask[e] && p.Kinds[e] != ElementKind.Outside) { p.Kinds[e] = area.Kind; hits++; }
        if (hits == 0) result.Warnings.Add("An area does not cover any voxel inside the boundary.");
      }
      if (!p.Kinds.Any(k => k == ElementKind.Design || k == ElementKind.Solid))
      {
        error = "Void areas cover the whole boundary.";
        return null;
      }

      var active = p.ActiveNodes();
      var plane = domain.Plane;

      foreach (var load in loads)
      {
        double fx = load.Force * plane.XAxis, fy = load.Force * plane.YAxis, fz = load.Force * plane.ZAxis;
        if (fx == 0.0 && fy == 0.0 && fz == 0.0) continue;
        var nodes = Snap(domain, active, load.Anchor);
        if (nodes.Count == 0)
        {
          result.Warnings.Add("A load lies at NaN, infinite or overflowing coordinates and was ignored.");
          continue;
        }
        foreach (int n in nodes)
        {
          p.Force[3 * n] += fx / nodes.Count;
          p.Force[3 * n + 1] += fy / nodes.Count;
          p.Force[3 * n + 2] += fz / nodes.Count;
        }
        result.Loads.Add(new LoadMark3D { Nodes = nodes.Select(n => NodeWorld(domain, n)).ToArray(), Force = load.Force });
      }

      foreach (var support in supports)
      {
        if (!support.FixX && !support.FixY && !support.FixZ) continue;
        var nodes = Snap(domain, active, support.Anchor);
        if (nodes.Count == 0)
        {
          result.Warnings.Add("A support lies at NaN, infinite or overflowing coordinates and was ignored.");
          continue;
        }
        foreach (int n in nodes)
        {
          if (support.FixX) p.Fixed[3 * n] = true;
          if (support.FixY) p.Fixed[3 * n + 1] = true;
          if (support.FixZ) p.Fixed[3 * n + 2] = true;
        }
        result.Supports.Add(new SupportMark3D
        {
          Nodes = nodes.Select(n => NodeWorld(domain, n)).ToArray(),
          FixX = support.FixX, FixY = support.FixY, FixZ = support.FixZ,
        });
      }

      bool anyForce = false;
      for (int d = 0; d < p.Force.Length; d++)
        if (p.Force[d] != 0.0 && !p.Fixed[d]) anyForce = true;
      if (!anyForce) { error = "No usable load: add a load that is not on a fixed node."; return null; }
      if (!p.Fixed.Any(f => f)) { error = "No supports: the structure needs at least one support."; return null; }

      return result;
    }

    static Point3d NodeWorld(VoxelDomain3D domain, int node)
    {
      domain.Grid.NodeIJK(node, out int i, out int j, out int k);
      return domain.ToWorld(i, j, k);
    }

    /// <summary>
    /// Points → nearest active node. Curves → nearest active node of samples every half voxel.
    /// Surfaces → active nodes within 0.6 voxel of the surface (or, if none, the nodes nearest to its vertices).
    /// </summary>
    static List<int> Snap(VoxelDomain3D domain, bool[] active, Anchor3D anchor)
    {
      var set = new HashSet<int>();
      double h = domain.VoxelSize;
      if (anchor.Point.HasValue) set.Add(Nearest(domain, active, domain.ToGrid(anchor.Point.Value)));
      else if (anchor.Curve != null)
      {
        var c = anchor.Curve;
        int n = Math.Max(1, (int)Math.Ceiling(c.GetLength() / (h * 0.5)));
        var ts = c.DivideByCount(n, true) ?? new[] { c.Domain.Min, c.Domain.Max };
        foreach (var t in ts) set.Add(Nearest(domain, active, domain.ToGrid(c.PointAt(t))));
      }
      else
      {
        var mesh = anchor.Surface;
        var g = domain.Grid;
        var box = BoundingBox.Empty;
        foreach (var corner in mesh.GetBoundingBox(false).GetCorners()) box.Union(domain.ToGrid(corner));
        int i0 = Math.Max(0, (int)Math.Floor(box.Min.X) - 1), i1 = Math.Min(g.Nx, (int)Math.Ceiling(box.Max.X) + 1);
        int j0 = Math.Max(0, (int)Math.Floor(box.Min.Y) - 1), j1 = Math.Min(g.Ny, (int)Math.Ceiling(box.Max.Y) + 1);
        int k0 = Math.Max(0, (int)Math.Floor(box.Min.Z) - 1), k1 = Math.Min(g.Nz, (int)Math.Ceiling(box.Max.Z) + 1);
        double reach = 0.6 * h;
        for (int k = k0; k <= k1; k++)
          for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
              int node = g.Node(i, j, k);
              if (!active[node]) continue;
              // Bounded search: it returns null early beyond reach, where the full closest-point query would be wasted.
              if (mesh.ClosestMeshPoint(domain.ToWorld(i, j, k), reach) != null) set.Add(node);
            }
        if (set.Count == 0)
        {
          int step = Math.Max(1, mesh.Vertices.Count / 2000);
          for (int v = 0; v < mesh.Vertices.Count; v += step)
            set.Add(Nearest(domain, active, domain.ToGrid(mesh.Vertices[v])));
        }
      }
      // Nearest finds nothing (-1) for NaN, infinite or overflowing coordinates; such positions land nowhere.
      set.Remove(-1);
      return set.ToList();
    }

    /// <summary>Nearest active node to grid coordinates, growing a cubic shell until no closer node can exist; -1 if none.</summary>
    static int Nearest(VoxelDomain3D domain, bool[] active, Point3d g)
    {
      var grid = domain.Grid;
      int ci = Math.Max(0, Math.Min(grid.Nx, (int)Math.Round(g.X)));
      int cj = Math.Max(0, Math.Min(grid.Ny, (int)Math.Round(g.Y)));
      int ck = Math.Max(0, Math.Min(grid.Nz, (int)Math.Round(g.Z)));
      int best = -1;
      double bestD = double.MaxValue;
      int maxR = Math.Max(grid.Nx, Math.Max(grid.Ny, grid.Nz)) + 1;
      for (int r = 0; r <= maxR; r++)
      {
        for (int k = Math.Max(0, ck - r); k <= Math.Min(grid.Nz, ck + r); k++)
          for (int j = Math.Max(0, cj - r); j <= Math.Min(grid.Ny, cj + r); j++)
          {
            bool faceRow = Math.Abs(k - ck) == r || Math.Abs(j - cj) == r;
            // Interior rows of the shell only need their two end points.
            int step = faceRow ? 1 : Math.Max(1, 2 * r);
            for (int i = ci - r; i <= ci + r; i += step)
            {
              if (i < 0 || i > grid.Nx) continue;
              int node = grid.Node(i, j, k);
              if (!active[node]) continue;
              double d = (i - g.X) * (i - g.X) + (j - g.Y) * (j - g.Y) + (k - g.Z) * (k - g.Z);
              if (d < bestD) { bestD = d; best = node; }
            }
          }
        if (best >= 0 && r > Math.Sqrt(bestD) + 1) break;
      }
      return best;
    }
  }
}
