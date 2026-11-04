using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// A tiny software renderer for headless runs: flat-shaded mesh from a fixed axonometric view
  /// (painter's algorithm, back faces culled), plus the domain box, supports and loads.
  /// </summary>
  static class Render3D
  {
    static readonly Color Background = Color.FromArgb(214, 214, 214);
    static readonly Color LoadColor = Color.FromArgb(220, 50, 47);
    static readonly Color SupportColor = Color.FromArgb(38, 139, 210);

    public static void Mesh(Mesh mesh, BoundingBox domain, IEnumerable<(Point3d[] nodes, Vector3d force)> loads,
      IEnumerable<Point3d[]> supports, Color colour, string file, int width = 1000, int height = 680)
    {
      var eye = new Vector3d(1.0, -1.35, 0.95);
      eye.Unitize();
      var forward = -eye;
      var right = Vector3d.CrossProduct(forward, Vector3d.ZAxis);
      right.Unitize();
      var up = Vector3d.CrossProduct(right, forward);
      var light = new Vector3d(0.45, -0.8, 1.2);
      light.Unitize();

      // Fit the domain box into the image.
      var corners = domain.GetCorners();
      double minX = corners.Min(c => (Vector3d)c * right), maxX = corners.Max(c => (Vector3d)c * right);
      double minY = corners.Min(c => (Vector3d)c * up), maxY = corners.Max(c => (Vector3d)c * up);
      double pad = 50;
      double scale = Math.Min((width - 2 * pad) / (maxX - minX), (height - 2 * pad) / (maxY - minY));
      double ox = (width - (maxX - minX) * scale) / 2, oy = (height - (maxY - minY) * scale) / 2;
      PointF P(Point3d p)
      {
        var v = (Vector3d)p;
        return new PointF((float)(ox + (v * right - minX) * scale), (float)(height - oy - (v * up - minY) * scale));
      }

      using var bmp = new Bitmap(width, height);
      using var g = Graphics.FromImage(bmp);
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.Clear(Background);

      // Back edges of the domain box first, so the part covers them.
      using (var boxPen = new Pen(Color.FromArgb(150, 150, 150), 1.2f))
        foreach (var (a, b) in BoxEdges(corners)) g.DrawLine(boxPen, P(a), P(b));

      var faces = new List<(double depth, PointF[] pts, Color c)>();
      for (int f = 0; f < mesh.Faces.Count; f++)
      {
        var face = mesh.Faces[f];
        var idx = face.IsQuad ? new[] { face.A, face.B, face.C, face.D } : new[] { face.A, face.B, face.C };
        var pts3 = idx.Select(i => (Point3d)mesh.Vertices[i]).ToArray();
        var n = Vector3d.CrossProduct(pts3[1] - pts3[0], pts3[2] - pts3[0]);
        if (face.IsQuad) n += Vector3d.CrossProduct(pts3[2] - pts3[0], pts3[3] - pts3[0]);
        if (!n.Unitize() || n * forward >= 0) continue; // back face
        double shade = 0.35 + 0.65 * Math.Max(0.0, n * light);
        var c = Color.FromArgb((int)(colour.R * shade), (int)(colour.G * shade), (int)(colour.B * shade));
        double depth = pts3.Average(p => (Vector3d)p * forward);
        faces.Add((depth, pts3.Select(P).ToArray(), c));
      }
      foreach (var (_, pts, c) in faces.OrderByDescending(t => t.depth))
      {
        using var br = new SolidBrush(c);
        g.FillPolygon(br, pts);
        using var pen = new Pen(c, 0.6f);
        g.DrawPolygon(pen, pts);
      }

      using (var support = new SolidBrush(SupportColor))
        foreach (var nodes in supports)
          foreach (var p in Thin(nodes, 120))
          {
            var q = P(p);
            g.FillPolygon(support, new[] { q, new PointF(q.X - 5, q.Y + 9), new PointF(q.X + 5, q.Y + 9) });
          }
      foreach (var (nodes, force) in loads)
      {
        var dir = new PointF((float)(force * right), (float)(-(force * up)));
        double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len == 0) continue;
        using var pen = new Pen(LoadColor, 2.2f) { CustomEndCap = new AdjustableArrowCap(4, 5) };
        foreach (var p in Thin(nodes, 14))
        {
          var q = P(p);
          g.DrawLine(pen, q.X - (float)(dir.X / len * 34), q.Y - (float)(dir.Y / len * 34), q.X, q.Y);
        }
      }

      Directory.CreateDirectory(Render.Folder);
      var path = Path.Combine(Render.Folder, file);
      bmp.Save(path, ImageFormat.Png);
      Console.WriteLine($"    image {path}");
    }

    static IEnumerable<(Point3d, Point3d)> BoxEdges(Point3d[] c)
    {
      int[,] e = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
      for (int k = 0; k < 12; k++) yield return (c[e[k, 0]], c[e[k, 1]]);
    }

    static IEnumerable<Point3d> Thin(Point3d[] pts, int max)
    {
      if (pts.Length <= max) return pts;
      double step = (pts.Length - 1) / (double)(max - 1);
      return Enumerable.Range(0, max).Select(k => pts[(int)Math.Round(k * step)]);
    }
  }
}
