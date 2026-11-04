using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.TwoD
{
  /// <summary>Screen-sized viewport symbols for loads and supports.</summary>
  public static class Glyphs
  {
    public static readonly Color LoadColor = Color.FromArgb(220, 50, 47);
    public static readonly Color SupportColor = Color.FromArgb(38, 139, 210);
    public static readonly Color SolidAreaColor = Color.FromArgb(40, 40, 40);
    public static readonly Color VoidAreaColor = Color.FromArgb(203, 75, 22);
    public static readonly Color DomainColor = Color.FromArgb(150, 150, 150);

    const double ArrowPixels = 45.0;

    static double PixelsToWorld(RhinoViewport vp, Point3d at, double pixels) =>
      vp.GetWorldToScreenScale(at, out double ppu) && ppu > 0 ? pixels / ppu : pixels;

    /// <summary>Arrow whose tip touches <paramref name="tip"/>, pointing along the force.</summary>
    public static void Arrow(DisplayPipeline d, RhinoViewport vp, Point3d tip, Vector3d force, Color color)
    {
      var dir = force;
      if (!dir.Unitize()) { d.DrawPoint(tip, PointStyle.X, 5, color); return; }
      double len = PixelsToWorld(vp, tip, ArrowPixels);
      d.DrawLine(tip - dir * len, tip, color, 2);
      d.DrawArrowHead(tip, dir, color, 14.0, 0.0);
    }

    /// <summary>Triangle = pinned (X and Y), circle = roller (one direction).</summary>
    public static void Support(DisplayPipeline d, Point3d at, bool fixX, bool fixY, Color color)
    {
      var style = fixX && fixY ? PointStyle.Triangle : PointStyle.Circle;
      d.DrawPoint(at, style, 7, color);
    }

    /// <summary>Points along a curve, roughly every <paramref name="spacingPixels"/> on screen.</summary>
    public static IEnumerable<Point3d> Samples(Curve curve, RhinoViewport vp, double spacingPixels)
    {
      var mid = curve.PointAtNormalizedLength(0.5);
      double spacing = PixelsToWorld(vp, mid, spacingPixels);
      int n = Math.Max(1, Math.Min(64, (int)Math.Round(curve.GetLength() / Math.Max(spacing, 1e-12))));
      var ts = curve.DivideByCount(n, true);
      if (ts == null) yield break;
      foreach (var t in ts) yield return curve.PointAt(t);
    }
  }
}
