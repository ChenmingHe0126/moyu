using System;
using System.Collections.Generic;

namespace Moyu.Core
{
  /// <summary>Scan-line rasterisation of closed polygons onto pixel centres (grid units).</summary>
  public static class PolygonRaster
  {
    /// <summary>
    /// Flips <paramref name="mask"/> for every pixel whose centre lies inside the polygon.
    /// Toggling several polygons into one mask gives even-odd fill, so nested loops become holes.
    /// </summary>
    public static void Toggle(int nx, int ny, IList<double[]> polygon, bool[] mask)
    {
      int n = polygon.Count;
      if (n < 3) return;
      var xs = new List<double>();
      for (int j = 0; j < ny; j++)
      {
        double y = j + 0.5;
        xs.Clear();
        for (int k = 0; k < n; k++)
        {
          double[] a = polygon[k], b = polygon[(k + 1) % n];
          if ((a[1] <= y) == (b[1] <= y)) continue;
          xs.Add(a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1]));
        }
        xs.Sort();
        for (int k = 0; k + 1 < xs.Count; k += 2)
        {
          int i0 = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5));
          int i1 = Math.Min(nx - 1, (int)Math.Ceiling(xs[k + 1] - 0.5) - 1);
          for (int i = i0; i <= i1; i++) mask[j * nx + i] = !mask[j * nx + i];
        }
      }
    }

    public static bool[] Fill(int nx, int ny, IList<double[]> polygon)
    {
      var mask = new bool[nx * ny];
      Toggle(nx, ny, polygon, mask);
      return mask;
    }
  }
}
