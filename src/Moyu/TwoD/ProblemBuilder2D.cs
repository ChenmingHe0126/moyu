using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Core;
using Rhino.Geometry;

namespace Moyu.TwoD
{
  /// <summary>Nodes a load acts on, in world space, for display.</summary>
  public sealed class LoadMark
  {
    public Point3d[] Nodes;
    /// <summary>Total in-plane force in world space.</summary>
    public Vector3d Force;
  }

  public sealed class SupportMark
  {
    public Point3d[] Nodes;
    public bool FixX;
    public bool FixY;
  }

  /// <summary>Maps Rhino geometry conditions onto the pixel grid as a Rhino-free <see cref="Problem2D"/>.</summary>
  public static class ProblemBuilder2D
  {
    public sealed class Output
    {
      public Problem2D Problem;
      public readonly List<LoadMark> Loads = new List<LoadMark>();
      public readonly List<SupportMark> Supports = new List<SupportMark>();
      public readonly List<string> Warnings = new List<string>();
    }

    public static Output Build(PixelDomain2D domain, IList<Load2D> loads, IList<Support2D> supports,
      IList<Area2D> areas, Material2D material, out string error)
    {
      error = null;
      var grid = domain.Grid;
      var result = new Output();
      var p = new Problem2D(grid)
      {
        YoungsModulus = material.YoungsModulus,
        PoissonRatio = material.PoissonRatio,
      };
      result.Problem = p;

      for (int e = 0; e < grid.ElementCount; e++)
        p.Kinds[e] = domain.Inside[e] ? ElementKind.Design : ElementKind.Outside;

      // Areas in input order; later ones win where they overlap. They never extend the boundary.
      foreach (var area in areas)
      {
        var mask = PolygonRaster.Fill(grid.Nx, grid.Ny, domain.ToGridPolygon(area.Region));
        int hits = 0;
        for (int e = 0; e < mask.Length; e++)
          if (mask[e] && p.Kinds[e] != ElementKind.Outside) { p.Kinds[e] = area.Kind; hits++; }
        if (hits == 0) result.Warnings.Add("An area does not cover any pixel inside the boundary.");
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
        double fx = load.Force * plane.XAxis, fy = load.Force * plane.YAxis;
        double inPlane = Math.Sqrt(fx * fx + fy * fy);
        if (inPlane <= load.Force.Length * 1e-9 || inPlane == 0.0)
        {
          result.Warnings.Add("A load is perpendicular to the domain plane and was ignored.");
          continue;
        }
        if (inPlane < load.Force.Length * 0.99)
          result.Warnings.Add("A load has an out-of-plane component; only its in-plane part is used.");

        var nodes = Snap(domain, active, load.Anchor);
        if (nodes.Count == 0)
        {
          result.Warnings.Add("A load lies at NaN, infinite or overflowing coordinates and was ignored.");
          continue;
        }
        foreach (int n in nodes)
        {
          p.Force[2 * n] += fx / nodes.Count;
          p.Force[2 * n + 1] += fy / nodes.Count;
        }
        result.Loads.Add(new LoadMark { Nodes = nodes.Select(n => NodeWorld(domain, n)).ToArray(), Force = plane.XAxis * fx + plane.YAxis * fy });
      }

      foreach (var support in supports)
      {
        if (!support.FixX && !support.FixY) continue;
        var nodes = Snap(domain, active, support.Anchor);
        if (nodes.Count == 0)
        {
          result.Warnings.Add("A support lies at NaN, infinite or overflowing coordinates and was ignored.");
          continue;
        }
        foreach (int n in nodes)
        {
          if (support.FixX) p.Fixed[2 * n] = true;
          if (support.FixY) p.Fixed[2 * n + 1] = true;
        }
        result.Supports.Add(new SupportMark { Nodes = nodes.Select(n => NodeWorld(domain, n)).ToArray(), FixX = support.FixX, FixY = support.FixY });
      }

      bool anyForce = false;
      for (int d = 0; d < p.Force.Length; d++)
        if (p.Force[d] != 0.0 && !p.Fixed[d]) anyForce = true;
      if (!anyForce) { error = "No usable load: add a load in the domain plane that is not on a fixed node."; return null; }
      if (!p.Fixed.Any(f => f)) { error = "No supports: the structure needs at least one support."; return null; }

      return result;
    }

    static Point3d NodeWorld(PixelDomain2D domain, int node)
    {
      domain.Grid.NodeIJ(node, out int i, out int j);
      return domain.ToWorld(i, j);
    }

    /// <summary>Points → nearest active node. Curves → nearest active node of samples every half pixel.</summary>
    static List<int> Snap(PixelDomain2D domain, bool[] active, Anchor2D anchor)
    {
      var set = new HashSet<int>();
      if (anchor.Point.HasValue) set.Add(Nearest(domain, active, domain.ToGrid(anchor.Point.Value)));
      else
      {
        var c = anchor.Curve;
        int n = Math.Max(1, (int)Math.Ceiling(c.GetLength() / (domain.PixelSize * 0.5)));
        var ts = c.DivideByCount(n, true) ?? new[] { c.Domain.Min, c.Domain.Max };
        foreach (var t in ts) set.Add(Nearest(domain, active, domain.ToGrid(c.PointAt(t))));
      }
      // Nearest finds nothing (-1) for NaN, infinite or overflowing coordinates; such positions land nowhere.
      set.Remove(-1);
      return set.ToList();
    }

    /// <summary>Nearest active node to grid coordinates, growing a square search ring until no closer node can exist; -1 if none.</summary>
    static int Nearest(PixelDomain2D domain, bool[] active, Point2d g)
    {
      var grid = domain.Grid;
      int ci = Math.Max(0, Math.Min(grid.Nx, (int)Math.Round(g.X)));
      int cj = Math.Max(0, Math.Min(grid.Ny, (int)Math.Round(g.Y)));
      int best = -1;
      double bestD = double.MaxValue;
      int maxR = Math.Max(grid.Nx, grid.Ny) + 1;
      void Try(int i, int j)
      {
        if (i < 0 || j < 0 || i > grid.Nx || j > grid.Ny) return;
        int node = grid.Node(i, j);
        if (!active[node]) return;
        double d = (i - g.X) * (i - g.X) + (j - g.Y) * (j - g.Y);
        if (d < bestD) { bestD = d; best = node; }
      }
      for (int r = 0; r <= maxR; r++)
      {
        if (r == 0) Try(ci, cj);
        for (int i = ci - r; i <= ci + r && r > 0; i++) { Try(i, cj - r); Try(i, cj + r); }
        for (int j = cj - r + 1; j <= cj + r - 1; j++) { Try(ci - r, j); Try(ci + r, j); }
        if (best >= 0 && r > Math.Sqrt(bestD) + 1) break;
      }
      return best;
    }
  }
}
