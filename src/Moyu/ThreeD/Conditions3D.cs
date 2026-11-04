using System;
using System.Collections.Generic;
using System.Drawing;
using Moyu.Core;
using Moyu.TwoD;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.ThreeD
{
  /// <summary>Where a 3D condition acts: a point, a curve, or a surface (kept as a mesh).</summary>
  public sealed class Anchor3D
  {
    public readonly Point3d? Point;
    public readonly Curve Curve;
    public readonly Mesh Surface;

    public Anchor3D(Point3d point) { Point = point; }
    public Anchor3D(Curve curve) { Curve = curve; }
    public Anchor3D(Mesh surface) { Surface = surface; }

    public BoundingBox Bounds =>
      Point.HasValue ? new BoundingBox(Point.Value, Point.Value) :
      Curve != null ? Curve.GetBoundingBox(false) : Surface.GetBoundingBox(false);

    /// <summary>A few points spread over the anchor, for drawing symbols.</summary>
    public IEnumerable<Point3d> Samples(RhinoViewport vp, int surfaceMax = 16)
    {
      if (Point.HasValue) { yield return Point.Value; yield break; }
      if (Curve != null)
      {
        foreach (var p in Glyphs.Samples(Curve, vp, 60.0)) yield return p;
        yield break;
      }
      int n = Surface.Faces.Count;
      int step = Math.Max(1, n / surfaceMax);
      for (int f = 0; f < n; f += step) yield return Surface.Faces.GetFaceCenter(f);
    }

    public void DrawShape(DisplayPipeline d, Color color)
    {
      if (Curve != null) d.DrawCurve(Curve, color, 2);
      else if (Surface != null) d.DrawMeshWires(Surface, color);
    }

    public override string ToString() => Point.HasValue ? "point" : Curve != null ? "curve" : "surface";
  }

  public sealed class Load3D : IMoyuPreview
  {
    public readonly Anchor3D Anchor;
    /// <summary>Total force in world space, shared evenly by the nodes the anchor lands on.</summary>
    public readonly Vector3d Force;

    public Load3D(Anchor3D anchor, Vector3d force) { Anchor = anchor; Force = force; }

    public BoundingBox Bounds => Anchor.Bounds;

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      var color = highlight ?? Glyphs.LoadColor;
      Anchor.DrawShape(d, color);
      foreach (var p in Anchor.Samples(vp)) Glyphs.Arrow(d, vp, p, Force, color);
    }

    public override string ToString() => $"Load 3D ({Anchor}, |F| = {Force.Length:G4})";
  }

  public sealed class Support3D : IMoyuPreview
  {
    public readonly Anchor3D Anchor;
    public readonly bool FixX, FixY, FixZ;

    public Support3D(Anchor3D anchor, bool fixX, bool fixY, bool fixZ)
    {
      Anchor = anchor;
      FixX = fixX;
      FixY = fixY;
      FixZ = fixZ;
    }

    public bool FixesAll => FixX && FixY && FixZ;

    public BoundingBox Bounds => Anchor.Bounds;

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      var color = highlight ?? Glyphs.SupportColor;
      Anchor.DrawShape(d, color);
      foreach (var p in Anchor.Samples(vp, 32)) Glyphs.Support(d, p, FixesAll, FixesAll, color);
    }

    public override string ToString() => $"Support 3D ({Anchor}, fix {(FixX ? "X" : "")}{(FixY ? "Y" : "")}{(FixZ ? "Z" : "")})";
  }

  public sealed class Area3D : IMoyuPreview
  {
    public readonly Mesh Region;
    /// <summary><see cref="ElementKind.Solid"/> or <see cref="ElementKind.Void"/>.</summary>
    public readonly ElementKind Kind;

    public Area3D(Mesh region, ElementKind kind) { Region = region; Kind = kind; }

    public BoundingBox Bounds => Region.GetBoundingBox(false);

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight) =>
      d.DrawMeshWires(Region, highlight ?? (Kind == ElementKind.Solid ? Glyphs.SolidAreaColor : Glyphs.VoidAreaColor));

    public override string ToString() => $"Area 3D ({(Kind == ElementKind.Solid ? "solid" : "void")})";
  }

  public sealed class Material3D
  {
    public readonly double YoungsModulus;
    public readonly double PoissonRatio;

    public Material3D(double e, double nu) { YoungsModulus = e; PoissonRatio = nu; }

    public static Material3D Default => new Material3D(1.0, 0.3);

    public override string ToString() => $"Material 3D (E = {YoungsModulus:G4}, ν = {PoissonRatio:G3})";
  }
}
