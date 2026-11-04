using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Stack.Core
{
  /// <summary>
  /// Planning figures for the boss-key massing study, read straight off the stacked voxels so they hold up to a
  /// second look: one voxel = one structural bay square and three storeys high.
  /// </summary>
  public sealed class MassingMetrics
  {
    public const double Bay = 7.2, StoreyHeight = 3.6;
    public const int StoreysPerVoxel = 3;
    /// <summary>Setback from the well footprint to the site boundary, in bays.</summary>
    public const double SiteMargin = 0.8;

    public int Voxels;
    public double SiteArea, Gfa, Far, Coverage, Height;
    public int Floors;

    public static MassingMetrics From(IEnumerable<Int3> cells, int nx, int ny)
    {
      var list = cells.ToList();
      double site = (nx + 2 * SiteMargin) * Bay * (ny + 2 * SiteMargin) * Bay;
      int footprint = list.Select(c => (c.X, c.Y)).Distinct().Count();
      int floors = list.Count == 0 ? 0 : (list.Max(c => c.Z) + 1) * StoreysPerVoxel;
      double gfa = list.Count * Bay * Bay * StoreysPerVoxel;
      return new MassingMetrics
      {
        Voxels = list.Count,
        SiteArea = site,
        Gfa = gfa,
        Far = gfa / site,
        Coverage = footprint * Bay * Bay / site,
        Floors = floors,
        Height = floors * StoreyHeight,
      };
    }

    /// <summary>
    /// A plausible stepped massing (podium, terrace, tower) for when the well is too empty to pass as a design.
    /// Deterministic per seed.
    /// </summary>
    public static List<Int3> Decoy(int nx, int ny, int nz, int seed)
    {
      var rng = new Random(seed);
      var cells = new HashSet<Int3>();
      int notchX = rng.Next(2) == 0 ? 0 : nx - 1, notchY = rng.Next(2) == 0 ? 0 : ny - 1;
      for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
          if (x != notchX || y != notchY) cells.Add(new Int3(x, y, 0));
      // Terrace: drop one row and one column on the notch side.
      int x0 = notchX == 0 ? 1 : 0, x1 = notchX == 0 ? nx - 1 : nx - 2;
      int y0 = notchY == 0 ? 1 : 0, y1 = notchY == 0 ? ny - 1 : ny - 2;
      for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
          cells.Add(new Int3(x, y, 1));
      // Tower in the corner opposite the notch.
      int tx = notchX == 0 ? nx - 2 : 0, ty = notchY == 0 ? ny - 2 : 0;
      int top = Math.Min(nz - 1, 3 + rng.Next(3));
      for (int z = 2; z <= top; z++)
        for (int dy = 0; dy < 2; dy++)
          for (int dx = 0; dx < 2; dx++)
            cells.Add(new Int3(tx + dx, ty + dy, z));
      return cells.OrderBy(c => c.Z).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
    }
  }
}
