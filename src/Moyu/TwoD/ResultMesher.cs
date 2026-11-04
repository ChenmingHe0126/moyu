using System;
using System.Collections.Generic;
using System.Drawing;
using Moyu.Core;
using Rhino.Geometry;

namespace Moyu.TwoD
{
  public enum DisplayMode
  {
    /// <summary>Every pixel inside the boundary, shaded by density.</summary>
    Pixels = 0,
    /// <summary>Only pixels at or above the threshold.</summary>
    Solid = 1,
    /// <summary>Shared-vertex mesh with node-averaged densities.</summary>
    Smooth = 2,
  }

  /// <summary>Turns a <see cref="Result2D"/> into display/output geometry in world space.</summary>
  public static class ResultMesher
  {
    public static Mesh BuildMesh(Result2D r, DisplayMode mode, double threshold, Color solid, Color empty)
    {
      var d = r.Domain;
      var g = d.Grid;
      var mesh = new Mesh();

      if (mode == DisplayMode.Smooth)
      {
        var nodal = NodeAverage(r);
        var vid = new int[(g.Nx + 1) * (g.Ny + 1)];
        for (int k = 0; k < vid.Length; k++) vid[k] = -1;
        int V(int i, int j)
        {
          int k = j * (g.Nx + 1) + i;
          if (vid[k] < 0)
          {
            vid[k] = mesh.Vertices.Add(d.ToWorld(i, j));
            mesh.VertexColors.Add(Lerp(empty, solid, nodal[k]));
          }
          return vid[k];
        }
        for (int j = 0; j < g.Ny; j++)
          for (int i = 0; i < g.Nx; i++)
            if (r.Kinds[g.Element(i, j)] != ElementKind.Outside)
              mesh.Faces.AddFace(V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1));
      }
      else
      {
        for (int j = 0; j < g.Ny; j++)
          for (int i = 0; i < g.Nx; i++)
          {
            int e = g.Element(i, j);
            if (r.Kinds[e] == ElementKind.Outside) continue;
            double x = r.Density[e];
            if (mode == DisplayMode.Solid && x < threshold) continue;
            var c = mode == DisplayMode.Solid ? solid : Lerp(empty, solid, x);
            int v = mesh.Vertices.Count;
            mesh.Vertices.Add(d.ToWorld(i, j));
            mesh.Vertices.Add(d.ToWorld(i + 1, j));
            mesh.Vertices.Add(d.ToWorld(i + 1, j + 1));
            mesh.Vertices.Add(d.ToWorld(i, j + 1));
            for (int k = 0; k < 4; k++) mesh.VertexColors.Add(c);
            mesh.Faces.AddFace(v, v + 1, v + 2, v + 3);
          }
      }
      mesh.Normals.ComputeNormals();
      return mesh;
    }

    /// <summary>Closed iso-density outlines; at 0.5 they follow the edges of solid pixel regions.</summary>
    public static List<Curve> BuildContours(Result2D r, double threshold)
    {
      var d = r.Domain;
      var g = d.Grid;
      var nodal = MarchingSquares.NodalField(g, r.Density, r.Kinds);
      var curves = new List<Curve>();
      double tol = d.PixelSize * 1e-6;
      foreach (var loop in MarchingSquares.Contour(g.Nx, g.Ny, nodal, threshold))
      {
        var pl = new Polyline(loop.Count);
        foreach (var q in loop) pl.Add(d.ToWorld(q[0], q[1]));
        pl.DeleteShortSegments(tol);
        if (pl.Count < 4) continue;
        if (!pl.IsClosed) pl.Add(pl[0]);
        curves.Add(new PolylineCurve(pl));
      }
      return curves;
    }

    /// <summary>Average density of the inside pixels around each node.</summary>
    static double[] NodeAverage(Result2D r)
    {
      var g = r.Domain.Grid;
      var f = new double[(g.Nx + 1) * (g.Ny + 1)];
      for (int j = 0; j <= g.Ny; j++)
        for (int i = 0; i <= g.Nx; i++)
        {
          double s = 0.0;
          int n = 0;
          for (int jj = j - 1; jj <= j; jj++)
            for (int ii = i - 1; ii <= i; ii++)
            {
              if (ii < 0 || jj < 0 || ii >= g.Nx || jj >= g.Ny) continue;
              int e = g.Element(ii, jj);
              if (r.Kinds[e] == ElementKind.Outside) continue;
              s += r.Density[e];
              n++;
            }
          f[j * (g.Nx + 1) + i] = n > 0 ? s / n : 0.0;
        }
      return f;
    }

    static Color Lerp(Color a, Color b, double t)
    {
      t = Math.Max(0.0, Math.Min(1.0, t));
      return Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));
    }
  }
}
