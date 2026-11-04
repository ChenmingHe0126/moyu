using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;

namespace Moyu.Games.Common
{
  /// <summary>
  /// One colour scheme of a game's look: the background gradient, two text inks, an accent, and the cool tint that
  /// shadows lean towards (shadows go bluish-violet, never grey, as in a painted scene).
  /// </summary>
  public readonly struct Palette
  {
    public readonly Color Sky, Horizon, Ink, Muted, Accent, Shadow;

    public Palette(Color sky, Color horizon, Color ink, Color muted, Color accent, Color shadow)
    {
      Sky = sky;
      Horizon = horizon;
      Ink = ink;
      Muted = muted;
      Accent = accent;
      Shadow = shadow;
    }

    public static Palette Lerp(Palette a, Palette b, double t) => new Palette(
      Toon.Mix(a.Sky, b.Sky, t), Toon.Mix(a.Horizon, b.Horizon, t), Toon.Mix(a.Ink, b.Ink, t),
      Toon.Mix(a.Muted, b.Muted, t), Toon.Mix(a.Accent, b.Accent, t), Toon.Mix(a.Shadow, b.Shadow, t));

    /// <summary>Close enough that redrawing the background would not show.</summary>
    public bool Near(Palette o) => Diff(Sky, o.Sky) + Diff(Horizon, o.Horizon) + Diff(Shadow, o.Shadow) < 6;

    static int Diff(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    public static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    /// <summary>The level band (0–4) both games use for their five chapter names and palettes.</summary>
    public static int Band(int level) => level >= 13 ? 4 : level >= 10 ? 3 : level >= 7 ? 2 : level >= 4 ? 1 : 0;
  }

  /// <summary>
  /// Unlit, painterly colouring under one fixed light, so the games look the same in any display mode: faces
  /// towards the light lift towards white, faces away sink towards the palette's shadow tint, in soft bands.
  /// </summary>
  public static class Toon
  {
    /// <summary>From high up, front-left, like a late-morning sun.</summary>
    public static readonly Vector3d Light = Unit(-0.45, -0.6, 0.85);

    static Vector3d Unit(double x, double y, double z)
    {
      var v = new Vector3d(x, y, z);
      v.Unitize();
      return v;
    }

    public static Color Mix(Color a, Color b, double t)
    {
      t = Math.Max(0, Math.Min(1, t));
      return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static double Smooth(double e0, double e1, double x)
    {
      double t = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0)));
      return t * t * (3 - 2 * t);
    }

    /// <summary>Colour of a surface with normal <paramref name="n"/> painted <paramref name="paint"/>.</summary>
    public static Color Shade(Color paint, Color shadow, Vector3d n)
    {
      double t = n * Light;
      var lit = Mix(paint, Color.White, 0.3);
      var dark = Mix(paint, shadow, 0.45);
      var c = Mix(paint, lit, Smooth(0.3, 0.62, t));
      return Mix(c, dark, Smooth(0.08, -0.3, t));
    }

    /// <summary>Gives every vertex its shaded colour from its normal.</summary>
    public static void Paint(Mesh mesh, Color paint, Color shadow)
    {
      if (mesh.Normals.Count != mesh.Vertices.Count) mesh.Normals.ComputeNormals();
      mesh.VertexColors.Clear();
      for (int i = 0; i < mesh.Vertices.Count; i++) mesh.VertexColors.Add(Shade(paint, shadow, mesh.Normals[i]));
    }

    /// <summary>A unit cube with its own vertices per face, so each face can take one flat tone.</summary>
    public static Mesh Cube(double x0, double y0, double z0, double x1, double y1, double z1, Color paint, Color shadow)
    {
      var m = new Mesh();
      AddCube(m, x0, y0, z0, x1, y1, z1, paint, shadow);
      return m;
    }

    /// <summary>A box spanning x0…x1, y0…y1, z0…z1 along <paramref name="frame"/>'s axes, shaded as it sits in the world.</summary>
    public static void AddBox(Mesh m, Plane frame, double x0, double y0, double z0, double x1, double y1, double z1, Color paint, Color shadow)
    {
      Point3d P(double x, double y, double z) => frame.Origin + frame.XAxis * x + frame.YAxis * y + frame.ZAxis * z;
      void Quad(Point3d a, Point3d b, Point3d c, Point3d d, Vector3d n)
      {
        int i = m.Vertices.Count;
        var col = Shade(paint, shadow, n);
        foreach (var p in new[] { a, b, c, d })
        {
          m.Vertices.Add(p);
          m.Normals.Add(n);
          m.VertexColors.Add(col);
        }
        m.Faces.AddFace(i, i + 1, i + 2, i + 3);
      }
      Vector3d X = frame.XAxis, Y = frame.YAxis, Z = frame.ZAxis;
      Quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1), Z);
      Quad(P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0), -Z);
      Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1), -Y);
      Quad(P(x1, y1, z0), P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), Y);
      Quad(P(x0, y1, z0), P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), -X);
      Quad(P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1), X);
    }

    /// <summary>A flat polygon (in <paramref name="frame"/>'s XY) extruded from z0 to z1, shaded as it sits in the world.</summary>
    public static void AddPrism(Mesh m, Plane frame, IList<Point2d> outline, double z0, double z1, Color paint, Color shadow)
    {
      int n = outline.Count;
      if (n < 3) return;
      Point3d P(Point2d q, double z) => frame.Origin + frame.XAxis * q.X + frame.YAxis * q.Y + frame.ZAxis * z;
      foreach (var (z, nrm) in new[] { (z1, frame.ZAxis), (z0, -frame.ZAxis) })
      {
        var ring = new Polyline(outline.Select(q => P(q, z)));
        ring.Add(ring[0]);
        var cap = Mesh.CreateFromClosedPolyline(ring);
        if (cap == null) continue;
        var col = Shade(paint, shadow, nrm);
        int off = m.Vertices.Count;
        foreach (var v in cap.Vertices)
        {
          m.Vertices.Add(v);
          m.Normals.Add(nrm);
          m.VertexColors.Add(col);
        }
        foreach (var f in cap.Faces) m.Faces.AddFace(f.A + off, f.B + off, f.C + off);
      }
      double cx = outline.Average(q => q.X), cy = outline.Average(q => q.Y);
      for (int i = 0; i < n; i++)
      {
        var a = outline[i];
        var b = outline[(i + 1) % n];
        var side = new Vector2d(b.Y - a.Y, a.X - b.X);
        if (side * new Vector2d(a.X - cx, a.Y - cy) < 0) side = -side;
        if (!side.Unitize()) continue;
        var nrm = frame.XAxis * side.X + frame.YAxis * side.Y;
        var col = Shade(paint, shadow, nrm);
        int k = m.Vertices.Count;
        foreach (var p in new[] { P(a, z0), P(b, z0), P(b, z1), P(a, z1) })
        {
          m.Vertices.Add(p);
          m.Normals.Add(nrm);
          m.VertexColors.Add(col);
        }
        m.Faces.AddFace(k, k + 1, k + 2, k + 3);
      }
    }

    public static void AddCube(Mesh m, double x0, double y0, double z0, double x1, double y1, double z1, Color paint, Color shadow)
    {
      void Quad(Point3d a, Point3d b, Point3d c, Point3d d, Vector3d n)
      {
        int i = m.Vertices.Count;
        var col = Shade(paint, shadow, n);
        foreach (var p in new[] { a, b, c, d })
        {
          m.Vertices.Add(p);
          m.Normals.Add(n);
          m.VertexColors.Add(col);
        }
        m.Faces.AddFace(i, i + 1, i + 2, i + 3);
      }
      Quad(new Point3d(x0, y0, z1), new Point3d(x1, y0, z1), new Point3d(x1, y1, z1), new Point3d(x0, y1, z1), Vector3d.ZAxis);
      Quad(new Point3d(x0, y0, z0), new Point3d(x0, y1, z0), new Point3d(x1, y1, z0), new Point3d(x1, y0, z0), -Vector3d.ZAxis);
      Quad(new Point3d(x0, y0, z0), new Point3d(x1, y0, z0), new Point3d(x1, y0, z1), new Point3d(x0, y0, z1), -Vector3d.YAxis);
      Quad(new Point3d(x1, y1, z0), new Point3d(x0, y1, z0), new Point3d(x0, y1, z1), new Point3d(x1, y1, z1), Vector3d.YAxis);
      Quad(new Point3d(x0, y1, z0), new Point3d(x0, y0, z0), new Point3d(x0, y0, z1), new Point3d(x0, y1, z1), -Vector3d.XAxis);
      Quad(new Point3d(x1, y0, z0), new Point3d(x1, y1, z0), new Point3d(x1, y1, z1), new Point3d(x1, y0, z1), Vector3d.XAxis);
    }
  }
}
