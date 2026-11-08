using System;
using System.Collections.Generic;

namespace Moyu.Games.Snake.Core
{
  /// <summary>
  /// Figures for the boss-key circulation study, read off the snake's body as a walking route over the surface:
  /// length, climb and slopes, so they hold up to a second look.
  /// </summary>
  public sealed class RouteMetrics
  {
    /// <summary>Slope limit of an accessible ramp-free path (5 %).</summary>
    public const double AccessibleSlope = 0.05;
    /// <summary>Steepest slope reported for a step (100 %); a vertical step on a closed site would otherwise print as millions of percent.</summary>
    public const double MaxReported = 1.0;

    public double Length, Gain, AverageSlope, MaxSlope, Accessible;
    public int Steps;

    /// <param name="toMetres">Model units → metres.</param>
    public static RouteMetrics From(IReadOnlyList<Cell> route, SurfaceGrid grid, double toMetres)
    {
      var m = new RouteMetrics();
      double weighted = 0, accessible = 0;
      for (int i = 0; i + 1 < route.Count; i++)
      {
        var a = route[i];
        var b = route[i + 1];
        double d = grid.Distance(a, b) * toMetres;
        double dz = (grid.Height(b) - grid.Height(a)) * toMetres;
        double s = Math.Min(MaxReported, Math.Abs(grid.Slope(a, b)));
        m.Length += d;
        if (dz > 0) m.Gain += dz;
        weighted += s * d;
        if (s <= AccessibleSlope) accessible += d;
        m.MaxSlope = Math.Max(m.MaxSlope, s);
        m.Steps++;
      }
      if (m.Length > 0)
      {
        m.AverageSlope = weighted / m.Length;
        m.Accessible = accessible / m.Length;
      }
      return m;
    }

    /// <summary>Shortest decoy worth drawing as a path; anything shorter falls back to a straight walk.</summary>
    public const int MinDecoy = 6;

    /// <summary>
    /// A plausible meandering route across the field for when the snake is too short to pass as a path. On a trimmed
    /// site each bend keeps to the free cells of its column, and a hole only splits the route: the longest continuous
    /// run is kept, so a trimmed edge never cuts it down to a cell or two.
    /// </summary>
    public static List<Cell> Decoy(SurfaceGrid grid, int seed)
    {
      var rng = new Random(seed);
      double phase = rng.NextDouble() * Math.PI * 2, amp = Math.Max(1, grid.Nv / 4.0), period = 4 + rng.NextDouble() * 3;
      int mid = grid.Nv / 2;
      var best = new List<Cell>();
      var route = new List<Cell>();
      var used = new HashSet<Cell>();
      void Split()
      {
        if (route.Count > best.Count) best = route;
        route = new List<Cell>();
        used = new HashSet<Cell>();
      }
      for (int u = 1; u < grid.Nu - 1; u++)
      {
        int v = Math.Max(0, Math.Min(grid.Nv - 1, mid + (int)Math.Round(amp * Math.Sin(u / period + phase))));
        int free = NearestFree(grid, u, v);
        if (free < 0)
        {
          Split();
          continue;
        }
        var target = new Cell(u, free);
        if (route.Count > 0 && Join(grid, route, used, target)) continue;
        // The first cell, or no way round from the last column: start a new run here.
        Split();
        route.Add(target);
        used.Add(target);
      }
      Split();
      if (best.Count < MinDecoy)
      {
        var line = LongestLine(grid);
        if (line.Count > best.Count) best = line;
      }
      return best;
    }

    /// <summary>The free row of column <paramref name="u"/> nearest <paramref name="v"/>, or −1 when the whole column is blocked.</summary>
    static int NearestFree(SurfaceGrid grid, int u, int v)
    {
      for (int d = 0; d < grid.Nv; d++)
      {
        if (!grid.IsBlocked(new Cell(u, v - d))) return v - d;
        if (!grid.IsBlocked(new Cell(u, v + d))) return v + d;
      }
      return -1;
    }

    /// <summary>
    /// Walks from the route's end to <paramref name="to"/> in the next column one grid step at a time, so the route
    /// stays continuous: along the last column and then across, or across and then along the new one, whichever stays
    /// on free cells it has not used yet.
    /// </summary>
    static bool Join(SurfaceGrid grid, List<Cell> route, HashSet<Cell> used, Cell to)
    {
      var from = route[route.Count - 1];
      int dv = Math.Sign(to.V - from.V);
      foreach (bool alongFirst in new[] { true, false })
      {
        int column = alongFirst ? from.U : to.U;
        var steps = new List<Cell>();
        if (!alongFirst) steps.Add(new Cell(to.U, from.V));
        for (int v = from.V; v != to.V; )
        {
          v += dv;
          steps.Add(new Cell(column, v));
        }
        if (alongFirst) steps.Add(to);
        bool clear = true;
        foreach (var c in steps) clear &= !grid.IsBlocked(c) && !used.Contains(c);
        if (!clear) continue;
        route.AddRange(steps);
        used.UnionWith(steps);
        return true;
      }
      return false;
    }

    /// <summary>The longest straight run of free cells along a row or a column; of equal ones, the nearest the middle.</summary>
    static List<Cell> LongestLine(SurfaceGrid grid)
    {
      var best = new List<Cell>();
      foreach (bool rows in new[] { true, false })
      {
        int lines = rows ? grid.Nv : grid.Nu, cells = rows ? grid.Nu : grid.Nv;
        // Middle line first, then outwards, so a tie keeps the walk off the edge.
        for (int k = 0; k < lines; k++)
        {
          int a = lines / 2 + (k % 2 == 0 ? k / 2 : -(k + 1) / 2);
          var run = new List<Cell>();
          // One past the end counts as blocked, which closes the last run.
          for (int b = 0; b <= cells; b++)
          {
            var c = rows ? new Cell(b, a) : new Cell(a, b);
            if (!grid.IsBlocked(c))
            {
              run.Add(c);
              continue;
            }
            if (run.Count > best.Count) best = run;
            run = new List<Cell>();
          }
        }
      }
      return best;
    }
  }
}
