using System.Drawing;
using Moyu.Core;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.TwoD
{
  /// <summary>Data that can draw itself in the Rhino viewport.</summary>
  public interface IMoyuPreview
  {
    BoundingBox Bounds { get; }
    void Draw(DisplayPipeline display, RhinoViewport viewport, Color? highlight);
  }

  /// <summary>Where a condition acts: a point (nearest node) or a curve (nodes it passes over).</summary>
  public sealed class Anchor2D
  {
    public readonly Point3d? Point;
    public readonly Curve Curve;

    public Anchor2D(Point3d point) { Point = point; }
    public Anchor2D(Curve curve) { Curve = curve; }

    public BoundingBox Bounds => Point.HasValue ? new BoundingBox(Point.Value, Point.Value) : Curve.GetBoundingBox(false);

    public override string ToString() => Point.HasValue ? "point" : "curve";
  }

  public sealed class Load2D : IMoyuPreview
  {
    public readonly Anchor2D Anchor;
    /// <summary>Total force in world space; only its component in the domain plane is used.</summary>
    public readonly Vector3d Force;

    public Load2D(Anchor2D anchor, Vector3d force) { Anchor = anchor; Force = force; }

    public BoundingBox Bounds => Anchor.Bounds;

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      var color = highlight ?? Glyphs.LoadColor;
      if (Anchor.Point.HasValue) Glyphs.Arrow(d, vp, Anchor.Point.Value, Force, color);
      else
      {
        d.DrawCurve(Anchor.Curve, color, 2);
        foreach (var p in Glyphs.Samples(Anchor.Curve, vp, 60.0)) Glyphs.Arrow(d, vp, p, Force, color);
      }
    }

    public override string ToString() => $"Load 2D ({Anchor}, |F| = {Force.Length:G4})";
  }

  public sealed class Support2D : IMoyuPreview
  {
    public readonly Anchor2D Anchor;
    public readonly bool FixX;
    public readonly bool FixY;

    public Support2D(Anchor2D anchor, bool fixX, bool fixY) { Anchor = anchor; FixX = fixX; FixY = fixY; }

    public BoundingBox Bounds => Anchor.Bounds;

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      var color = highlight ?? Glyphs.SupportColor;
      if (Anchor.Point.HasValue) Glyphs.Support(d, Anchor.Point.Value, FixX, FixY, color);
      else
      {
        d.DrawCurve(Anchor.Curve, color, 3);
        foreach (var p in Glyphs.Samples(Anchor.Curve, vp, 30.0)) Glyphs.Support(d, p, FixX, FixY, color);
      }
    }

    public override string ToString() => $"Support 2D ({Anchor}, fix {(FixX ? "X" : "")}{(FixY ? "Y" : "")})";
  }

  public sealed class Area2D : IMoyuPreview
  {
    public readonly Curve Region;
    /// <summary><see cref="ElementKind.Solid"/> or <see cref="ElementKind.Void"/>.</summary>
    public readonly ElementKind Kind;

    Polyline _dotted;
    bool _dottedBuilt;

    public Area2D(Curve region, ElementKind kind) { Region = region; Kind = kind; }

    public BoundingBox Bounds => Region.GetBoundingBox(false);

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      if (Kind == ElementKind.Solid) d.DrawCurve(Region, highlight ?? Glyphs.SolidAreaColor, 3);
      else
      {
        var pl = DottedOutline();
        if (pl != null) d.DrawDottedPolyline(pl, highlight ?? Glyphs.VoidAreaColor, true);
      }
    }

    /// <summary>The void outline as a polyline, built once: Draw runs every frame and the region never changes.</summary>
    Polyline DottedOutline()
    {
      if (_dottedBuilt) return _dotted;
      _dottedBuilt = true;
      if (Region.TryGetPolyline(out var p)) _dotted = p;
      else
        using (var plc = Region.ToPolyline(0.0, 0.05, 0.0, 0.0))
          _dotted = plc?.ToPolyline();
      return _dotted;
    }

    public override string ToString() => $"Area 2D ({(Kind == ElementKind.Solid ? "solid" : "void")})";
  }

  public sealed class Material2D
  {
    public readonly double YoungsModulus;
    public readonly double PoissonRatio;

    public Material2D(double e, double nu) { YoungsModulus = e; PoissonRatio = nu; }

    public static Material2D Default => new Material2D(1.0, 0.3);

    public override string ToString() => $"Material 2D (E = {YoungsModulus:G4}, ν = {PoissonRatio:G3})";
  }
}
