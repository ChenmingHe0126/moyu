using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Snake.Core
{
  /// <summary>A tree in the boss-key landscape plan: its cell, an offset inside the cell (−0.5…0.5), canopy radius in cells.</summary>
  public readonly struct TreeSpot
  {
    public readonly Cell Cell;
    public readonly double Du, Dv, Radius;
    public readonly bool Evergreen;

    public TreeSpot(Cell cell, double du, double dv, double radius, bool evergreen)
    {
      Cell = cell;
      Du = du;
      Dv = dv;
      Radius = radius;
      Evergreen = evergreen;
    }
  }

  /// <summary>
  /// The planting and furniture that make the boss-key circulation study read as a landscape design: a woodland
  /// belt along the site edge, an avenue of trees either side of the route, a few specimen trees in the open,
  /// benches every few metres along the route, scale figures on it and at the nodes. Nothing stands on the route or
  /// on ground steeper than a person would plant on. Placement depends only on the grid, the route and the seed.
  /// </summary>
  public sealed class LandscapePlan
  {
    public const double MaxPlantingSlope = 0.45;
    public readonly List<TreeSpot> Trees = new List<TreeSpot>();
    /// <summary>Cells a figure stands on, with which side of the route (−1, 0 or 1).</summary>
    public readonly List<Cell> People = new List<Cell>();
    /// <summary>Indices into the route where a bench stands beside it, and on which side.</summary>
    public readonly List<(int index, int side)> Benches = new List<(int, int)>();
    /// <summary>Share of route cells under or beside a canopy.</summary>
    public double ShadeShare { get; private set; }

    public LandscapePlan(SurfaceGrid grid, IReadOnlyList<Cell> route, IReadOnlyList<Cell> nodes, int seed)
    {
      var rng = new Random(seed * 31 + 7);
      var path = new HashSet<Cell>(route);
      var taken = new HashSet<Cell>(nodes);
      bool Near(Cell c, int reach) => route.Any(r => grid.GridDistance(r, c) <= reach);
      bool Plantable(Cell c) => grid.Contains(c) && !grid.IsBlocked(c) && !path.Contains(c) && !taken.Contains(c) && SteepestFrom(grid, c) <= MaxPlantingSlope;
      void Plant(Cell c, double radius, bool evergreen)
      {
        Trees.Add(new TreeSpot(c, (rng.NextDouble() - 0.5) * 0.5, (rng.NextDouble() - 0.5) * 0.5, radius, evergreen));
        taken.Add(c);
      }

      // An avenue: every fourth cell of the route, a tree two cells out on alternate sides.
      for (int i = 2; i < route.Count; i += 4)
      {
        if (!Heading(grid, route, i, out int du, out int dv)) continue;
        int side = (i / 4) % 2 == 0 ? 1 : -1;
        if (grid.TryStep(route[i], -dv * side, du * side, out var one) && grid.TryStep(one, -dv * side, du * side, out var two) && Plantable(two) && !Near(two, 1))
          Plant(two, 1.0 + rng.NextDouble() * 0.3, false);
      }
      // A woodland belt along the open edges of the site, thinning inwards; a few specimen trees in the open.
      foreach (var c in grid.FreeCells().OrderBy(_ => rng.Next()))
      {
        if (!Plantable(c) || Near(c, 2) || Trees.Any(t => grid.GridDistance(t.Cell, c) < 3)) continue;
        int edge = Math.Min(grid.WrapU ? int.MaxValue : Math.Min(c.U, grid.Nu - 1 - c.U), grid.WrapV ? int.MaxValue : Math.Min(c.V, grid.Nv - 1 - c.V));
        // Sparse enough that the slope analysis still reads between the trees.
        double chance = edge <= 1 ? 0.4 : edge <= 3 ? 0.12 : 0.03;
        if (rng.NextDouble() < chance) Plant(c, 0.8 + rng.NextDouble() * 0.7, rng.NextDouble() < 0.35);
      }
      // Benches beside the route every seven cells, facing it; a figure on the route every eight, two at each node.
      for (int i = 3; i < route.Count - 1; i += 7) Benches.Add((i, (i / 7) % 2 == 0 ? 1 : -1));
      for (int i = 5; i < route.Count; i += 8) People.Add(route[i]);
      foreach (var n in nodes.Take(6))
      {
        People.Add(n);
        if (grid.TryStep(n, 1, 0, out var next) && !grid.IsBlocked(next)) People.Add(next);
      }
      ShadeShare = route.Count == 0 ? 0 : route.Count(r => Trees.Any(t => grid.GridDistance(t.Cell, r) <= Math.Max(2, (int)Math.Ceiling(t.Radius + 1)))) / (double)route.Count;
    }

    /// <summary>The steepest step from a cell to any neighbour.</summary>
    public static double SteepestFrom(SurfaceGrid grid, Cell c)
    {
      double worst = 0;
      foreach (var (du, dv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        if (grid.TryStep(c, du, dv, out var n)) worst = Math.Max(worst, Math.Abs(grid.Slope(c, n)));
      return worst;
    }

    /// <summary>The route's grid direction at <paramref name="i"/> (from the previous cell, or to the next).</summary>
    public static bool Heading(SurfaceGrid grid, IReadOnlyList<Cell> route, int i, out int du, out int dv)
    {
      du = dv = 0;
      if (route.Count < 2) return false;
      var a = route[Math.Max(0, i - 1)];
      var b = route[Math.Max(1, i)];
      foreach (var (x, y) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        if (grid.TryStep(a, x, y, out var n) && n == b)
        {
          du = x;
          dv = y;
          return true;
        }
      return false;
    }
  }
}
