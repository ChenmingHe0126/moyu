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

    /// <summary>A plausible meandering route across the field for when the snake is too short to pass as a path.</summary>
    public static List<Cell> Decoy(SurfaceGrid grid, int seed)
    {
      var rng = new Random(seed);
      var route = new List<Cell>();
      double phase = rng.NextDouble() * Math.PI * 2, amp = Math.Max(1, grid.Nv / 4.0), period = 4 + rng.NextDouble() * 3;
      int mid = grid.Nv / 2;
      Cell? last = null;
      for (int u = 1; u < grid.Nu - 1; u++)
      {
        int v = Math.Max(0, Math.Min(grid.Nv - 1, mid + (int)Math.Round(amp * Math.Sin(u / period + phase))));
        var target = new Cell(u, v);
        if (last != null)
        {
          // Walk there one grid step at a time so the route stays continuous.
          var c = last.Value;
          while (c.V != v)
          {
            c = new Cell(c.U, c.V + Math.Sign(v - c.V));
            if (grid.IsBlocked(c)) return route;
            route.Add(c);
          }
          c = new Cell(u, c.V);
          if (grid.IsBlocked(c)) return route;
          route.Add(c);
          last = c;
        }
        else if (!grid.IsBlocked(target))
        {
          route.Add(target);
          last = target;
        }
      }
      return route;
    }
  }
}
