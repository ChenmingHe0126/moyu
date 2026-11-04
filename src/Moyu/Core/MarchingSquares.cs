using System;
using System.Collections.Generic;

namespace Moyu.Core
{
  /// <summary>Iso-contours of a pixel density field, returned as closed polylines in grid units.</summary>
  public static class MarchingSquares
  {
    /// <summary>
    /// Node values are the average of the four surrounding pixels (missing/outside pixels count as 0),
    /// so a solid pixel edge sits exactly on the 0.5 contour. The field is padded with zeros,
    /// which guarantees every contour is closed.
    /// </summary>
    public static double[] NodalField(Grid2D grid, double[] density, ElementKind[] kinds)
    {
      int nx = grid.Nx, ny = grid.Ny;
      var f = new double[(nx + 1) * (ny + 1)];
      for (int j = 0; j <= ny; j++)
        for (int i = 0; i <= nx; i++)
        {
          double s = 0.0;
          for (int dj = -1; dj <= 0; dj++)
            for (int di = -1; di <= 0; di++)
            {
              int ii = i + di, jj = j + dj;
              if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
              int e = grid.Element(ii, jj);
              if (kinds != null && kinds[e] == ElementKind.Outside) continue;
              s += density[e];
            }
          f[j * (nx + 1) + i] = 0.25 * s;
        }
      return f;
    }

    /// <summary>Contours of the nodal field (row-major, (nx+1)·(ny+1) values) at <paramref name="level"/>.</summary>
    public static List<List<double[]>> Contour(int nx, int ny, double[] nodal, double level)
    {
      // Padded grid: node (I, J) ↔ original node (I-1, J-1); the outer ring is 0.
      int px = nx + 3, py = ny + 3;
      double V(int I, int J)
      {
        int i = I - 1, j = J - 1;
        if (i < 0 || j < 0 || i > nx || j > ny) return 0.0;
        return nodal[j * (nx + 1) + i];
      }

      // Edge keys: horizontal edge (I,J)-(I+1,J) → 2*(J*px+I); vertical edge (I,J)-(I,J+1) → 2*(J*px+I)+1.
      long H(int I, int J) => 2L * (J * (long)px + I);
      long Vt(int I, int J) => 2L * (J * (long)px + I) + 1;

      var segA = new List<long>();
      var segB = new List<long>();
      for (int J = 0; J < py - 1; J++)
        for (int I = 0; I < px - 1; I++)
        {
          double v00 = V(I, J), v10 = V(I + 1, J), v11 = V(I + 1, J + 1), v01 = V(I, J + 1);
          int code = (v00 >= level ? 1 : 0) | (v10 >= level ? 2 : 0) | (v11 >= level ? 4 : 0) | (v01 >= level ? 8 : 0);
          if (code == 0 || code == 15) continue;
          long bottom = H(I, J), top = H(I, J + 1), left = Vt(I, J), right = Vt(I + 1, J);
          bool centre = 0.25 * (v00 + v10 + v11 + v01) >= level;
          switch (code)
          {
            case 1: case 14: Add(left, bottom); break;
            case 2: case 13: Add(bottom, right); break;
            case 3: case 12: Add(left, right); break;
            case 4: case 11: Add(right, top); break;
            case 6: case 9: Add(bottom, top); break;
            case 7: case 8: Add(left, top); break;
            case 5:
              if (centre) { Add(left, top); Add(bottom, right); }
              else { Add(left, bottom); Add(right, top); }
              break;
            case 10:
              if (centre) { Add(left, bottom); Add(right, top); }
              else { Add(bottom, right); Add(left, top); }
              break;
          }
        }

      void Add(long a, long b) { segA.Add(a); segB.Add(b); }

      // Each crossing point is shared by exactly two segments on a closed contour.
      var byKey = new Dictionary<long, (int, int)>();
      for (int s = 0; s < segA.Count; s++)
      {
        Link(segA[s], s);
        Link(segB[s], s);
      }
      void Link(long key, int s)
      {
        if (byKey.TryGetValue(key, out var pair)) byKey[key] = (pair.Item1, s);
        else byKey[key] = (s, -1);
      }

      double[] Point(long key)
      {
        long cell = key / 2;
        int I = (int)(cell % px), J = (int)(cell / px);
        bool vertical = (key & 1) == 1;
        double va = V(I, J), vb = vertical ? V(I, J + 1) : V(I + 1, J);
        double t = Math.Abs(vb - va) < 1e-12 ? 0.5 : (level - va) / (vb - va);
        t = Math.Min(1.0, Math.Max(0.0, t));
        double x = I - 1 + (vertical ? 0.0 : t);
        double y = J - 1 + (vertical ? t : 0.0);
        return new[] { x, y };
      }

      var used = new bool[segA.Count];
      var result = new List<List<double[]>>();
      for (int s0 = 0; s0 < segA.Count; s0++)
      {
        if (used[s0]) continue;
        var poly = new List<double[]>();
        long start = segA[s0], key = segB[s0];
        int s = s0;
        used[s] = true;
        poly.Add(Point(start));
        while (true)
        {
          poly.Add(Point(key));
          if (key == start) break;
          var pair = byKey[key];
          int next = pair.Item1 == s ? pair.Item2 : pair.Item1;
          if (next < 0 || used[next]) break;
          used[next] = true;
          key = segA[next] == key ? segB[next] : segA[next];
          s = next;
        }
        if (poly.Count >= 4) result.Add(poly);
      }
      return result;
    }
  }
}
