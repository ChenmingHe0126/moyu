using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Plain-GDI drawings of what the components output (no viewport exists headless):
  /// the Visualize mesh colours and contours, plus the load/support marks stored in the result.
  /// </summary>
  static class Render
  {
    static readonly Color Background = Color.FromArgb(214, 214, 214);
    static readonly Color Contour = Color.FromArgb(0, 122, 204);
    static readonly Color LoadColor = Color.FromArgb(220, 50, 47);
    static readonly Color SupportColor = Color.FromArgb(38, 139, 210);

    public static string Folder;

    sealed class Frame
    {
      public Plane Plane;
      public double Size;
      public int Nx, Ny;
      public float Scale;
      public int Pad = 36;

      public int Width => (int)(Nx * Scale) + 2 * Pad;
      public int Height => (int)(Ny * Scale) + 2 * Pad;

      public PointF Map(Point3d p)
      {
        Plane.ClosestParameter(p, out double s, out double t);
        return new PointF(Pad + (float)(s / Size) * Scale, Pad + (float)(Ny - t / Size) * Scale);
      }

      public PointF Grid(double i, double j) => new PointF(Pad + (float)i * Scale, Pad + (float)(Ny - j) * Scale);
    }

    static Frame FrameOf(dynamic result, int maxWidth = 1000, int maxHeight = 640)
    {
      var f = new Frame { Plane = result.Domain.Plane, Size = result.Domain.PixelSize, Nx = result.Domain.Grid.Nx, Ny = result.Domain.Grid.Ny };
      f.Scale = (float)Math.Min((double)maxWidth / f.Nx, (double)maxHeight / f.Ny);
      return f;
    }

    /// <summary>Visualize 2D output (mesh + contours) with the solver's applied conditions.</summary>
    public static void Result(GH_Component vis, GH_Component solver, string file, bool contours = true)
    {
      dynamic r = Canvas.Result(solver);
      Frame f = FrameOf(r);
      using var bmp = new Bitmap(f.Width, f.Height);
      using var g = Graphics.FromImage(bmp);
      g.Clear(Background);
      FillMesh(g, f, Canvas.MeshOut(vis));
      g.SmoothingMode = SmoothingMode.AntiAlias;
      if (contours)
        using (var pen = new Pen(Contour, 2.2f))
          foreach (var c in Canvas.CurvesOut(vis))
            if (c.TryGetPolyline(out var pl)) g.DrawLines(pen, pl.Select(f.Map).ToArray());
      Conditions(g, f, r);
      Save(bmp, file);
    }

    /// <summary>The pixel grid of a domain: inside pixels light, outside transparent-grey, with grid lines.</summary>
    public static void Domain(GH_Component solver, string file)
    {
      dynamic r = Canvas.Result(solver);
      Frame f = FrameOf(r, 700, 700);
      var kinds = ((Array)r.Kinds).Cast<object>().Select(k => k.ToString()).ToArray();
      using var bmp = new Bitmap(f.Width, f.Height);
      using var g = Graphics.FromImage(bmp);
      g.Clear(Background);
      using var inside = new SolidBrush(Color.FromArgb(250, 250, 250));
      using var gridPen = new Pen(Color.FromArgb(185, 185, 185), 1f);
      for (int j = 0; j < f.Ny; j++)
        for (int i = 0; i < f.Nx; i++)
        {
          if (kinds[j * f.Nx + i] == "Outside") continue;
          var a = f.Grid(i, j + 1);
          g.FillRectangle(inside, a.X, a.Y, f.Scale, f.Scale);
          g.DrawRectangle(gridPen, a.X, a.Y, f.Scale, f.Scale);
        }
      g.SmoothingMode = SmoothingMode.AntiAlias;
      using var outline = new Pen(Color.FromArgb(90, 90, 90), 2.5f);
      foreach (Polyline pl in r.Domain.Outlines) g.DrawLines(outline, pl.Select(f.Map).ToArray());
      Conditions(g, f, r);
      Save(bmp, file);
    }

    public static void Csv(string file, IList<double> values)
    {
      File.WriteAllLines(Path.Combine(Folder, file),
        new[] { "iteration,compliance" }.Concat(values.Select((v, k) => $"{k + 1},{v.ToString("R", CultureInfo.InvariantCulture)}")));
    }

    static void FillMesh(Graphics g, Frame f, Mesh mesh)
    {
      g.SmoothingMode = SmoothingMode.None;
      for (int k = 0; k < mesh.Faces.Count; k++)
      {
        var face = mesh.Faces[k];
        var idx = new[] { face.A, face.B, face.C, face.D };
        var cols = idx.Select(i => mesh.VertexColors[i]).ToArray();
        var avg = Color.FromArgb((int)cols.Average(c => c.R), (int)cols.Average(c => c.G), (int)cols.Average(c => c.B));
        using var br = new SolidBrush(avg);
        var pts = idx.Select(i => f.Map(mesh.Vertices[i])).ToArray();
        g.FillPolygon(br, pts);
        g.DrawPolygon(new Pen(avg, 1f), pts);
      }
    }

    static void Conditions(Graphics g, Frame f, dynamic r)
    {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      using var support = new SolidBrush(SupportColor);
      foreach (var s in r.Supports)
        foreach (Point3d p in s.Nodes)
        {
          var q = f.Map(p);
          if (s.FixX && s.FixY) g.FillPolygon(support, new[] { q, new PointF(q.X - 7, q.Y + 12), new PointF(q.X + 7, q.Y + 12) });
          else g.FillEllipse(support, q.X - 6, q.Y + 1, 12, 12);
        }
      foreach (var l in r.Loads)
      {
        Vector3d force = l.Force;
        f.Plane.ClosestParameter(f.Plane.Origin + force, out double fx, out double fy);
        var dir = new Vector2d(fx, -fy);
        dir.Unitize();
        var nodes = (Point3d[])l.Nodes;
        int step = Math.Max(1, nodes.Length / 16);
        using var pen = new Pen(LoadColor, 2.6f) { CustomEndCap = new AdjustableArrowCap(4, 5) };
        for (int k = 0; k < nodes.Length; k += step)
        {
          var q = f.Map(nodes[k]);
          g.DrawLine(pen, q.X - (float)dir.X * 34, q.Y - (float)dir.Y * 34, q.X, q.Y);
        }
      }
    }

    static void Save(Bitmap bmp, string file)
    {
      Directory.CreateDirectory(Folder);
      var path = Path.Combine(Folder, file);
      bmp.Save(path, ImageFormat.Png);
      Console.WriteLine($"    image {path}");
    }
  }
}
