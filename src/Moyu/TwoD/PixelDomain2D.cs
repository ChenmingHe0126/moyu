using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Core;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.TwoD
{
  /// <summary>
  /// The design domain: a plane-aligned pixel grid plus the pixels whose centres fall inside the boundary.
  /// The plane origin sits on the lower-left grid corner, so grid coordinates are local coordinates / pixel size.
  /// </summary>
  public sealed class PixelDomain2D : IMoyuPreview
  {
    public readonly Plane Plane;
    public readonly double PixelSize;
    public readonly Grid2D Grid;
    /// <summary>Per pixel (row-major): centre lies inside the boundary region.</summary>
    public readonly bool[] Inside;
    /// <summary>Boundary loops in world space, for display.</summary>
    public readonly Polyline[] Outlines;

    Mesh _pixelMesh;

    PixelDomain2D(Plane plane, double pixelSize, Grid2D grid, bool[] inside, Polyline[] outlines)
    {
      Plane = plane;
      PixelSize = pixelSize;
      Grid = grid;
      Inside = inside;
      Outlines = outlines;
    }

    public int InsideCount => Inside.Count(b => b);

    public Point3d ToWorld(double gx, double gy) => Plane.PointAt(gx * PixelSize, gy * PixelSize);

    public Point2d ToGrid(Point3d p)
    {
      Plane.ClosestParameter(p, out double s, out double t);
      return new Point2d(s / PixelSize, t / PixelSize);
    }

    public BoundingBox Bounds
    {
      get
      {
        var box = BoundingBox.Empty;
        box.Union(ToWorld(0, 0));
        box.Union(ToWorld(Grid.Nx, 0));
        box.Union(ToWorld(Grid.Nx, Grid.Ny));
        box.Union(ToWorld(0, Grid.Ny));
        return box;
      }
    }

    /// <summary>Region curve → polygon in grid units (for <see cref="PolygonRaster"/>).</summary>
    public List<double[]> ToGridPolygon(Curve curve)
    {
      var pts = Discretise(curve, PixelSize * 0.1);
      var poly = new List<double[]>(pts.Count);
      foreach (var p in pts)
      {
        var g = ToGrid(p);
        poly.Add(new[] { g.X, g.Y });
      }
      return poly;
    }

    /// <summary>Quad mesh of the inside pixels, for previewing the resolution.</summary>
    public Mesh PixelMesh
    {
      get
      {
        if (_pixelMesh != null) return _pixelMesh;
        var mesh = new Mesh();
        int nx = Grid.Nx, ny = Grid.Ny;
        var vid = new int[(nx + 1) * (ny + 1)];
        for (int k = 0; k < vid.Length; k++) vid[k] = -1;
        int V(int i, int j)
        {
          int k = j * (nx + 1) + i;
          if (vid[k] < 0) vid[k] = mesh.Vertices.Add(ToWorld(i, j));
          return vid[k];
        }
        for (int j = 0; j < ny; j++)
          for (int i = 0; i < nx; i++)
            if (Inside[Grid.Element(i, j)])
              mesh.Faces.AddFace(V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1));
        mesh.Normals.ComputeNormals();
        return _pixelMesh = mesh;
      }
    }

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      d.DrawMeshWires(PixelMesh, highlight ?? Color.FromArgb(200, 200, 200));
      foreach (var pl in Outlines) d.DrawPolyline(pl, highlight ?? Glyphs.DomainColor, 2);
    }

    public override string ToString() => $"Pixel domain 2D ({Grid.Nx}×{Grid.Ny}, {InsideCount} pixels, size {PixelSize:G4})";

    /// <summary>
    /// Builds the grid around closed planar boundary curves. <paramref name="resolution"/> is the number of
    /// pixels along the longer side; nested curves become holes (even-odd).
    /// </summary>
    public static PixelDomain2D Create(IList<Curve> curves, int resolution, Plane? plane, out string error)
    {
      error = null;
      curves = curves?.Where(c => c != null && c.IsValid).ToList();
      if (curves == null || curves.Count == 0) { error = "No valid boundary curve."; return null; }
      if (curves.Any(c => !c.IsClosed)) { error = "Boundary curves must be closed."; return null; }
      resolution = Math.Max(2, resolution);

      var bbox = BoundingBox.Empty;
      foreach (var c in curves) bbox.Union(c.GetBoundingBox(false));
      double tol = Math.Max(bbox.Diagonal.Length * 1e-4, 1e-9);

      Plane basePlane;
      if (plane.HasValue && plane.Value.IsValid) basePlane = plane.Value;
      else if (!TryPickPlane(curves[0], tol, out basePlane)) { error = "Boundary curves must be planar."; return null; }

      // Discretise in plane coordinates and find the extent.
      var loops = new List<List<Point3d>>();
      double umin = double.MaxValue, vmin = double.MaxValue, umax = double.MinValue, vmax = double.MinValue;
      foreach (var c in curves)
      {
        var pts = Discretise(c, bbox.Diagonal.Length / 2000.0);
        loops.Add(pts);
        foreach (var p in pts)
        {
          basePlane.ClosestParameter(p, out double s, out double t);
          umin = Math.Min(umin, s); umax = Math.Max(umax, s);
          vmin = Math.Min(vmin, t); vmax = Math.Max(vmax, t);
        }
      }
      double w = umax - umin, h = vmax - vmin;
      if (w <= tol || h <= tol) { error = "Boundary has no area in the domain plane."; return null; }

      double size = Math.Max(w, h) / resolution;
      int nx = Math.Max(1, (int)Math.Ceiling(w / size - 1e-6));
      int ny = Math.Max(1, (int)Math.Ceiling(h / size - 1e-6));
      var gridPlane = new Plane(basePlane.PointAt(umin, vmin), basePlane.XAxis, basePlane.YAxis);

      var domain = new PixelDomain2D(gridPlane, size, new Grid2D(nx, ny), new bool[nx * ny],
        loops.Select(l => new Polyline(l)).ToArray());
      foreach (var c in curves) PolygonRaster.Toggle(nx, ny, domain.ToGridPolygon(c), domain.Inside);
      if (domain.InsideCount == 0) { error = "Resolution too coarse: no pixel centre lies inside the boundary."; return null; }
      return domain;
    }

    /// <summary>Prefers world-aligned axes (plan, front, side) so pixels line up with the drawing.</summary>
    static bool TryPickPlane(Curve curve, double tol, out Plane plane)
    {
      plane = Plane.Unset;
      if (!curve.TryGetPlane(out var fitted, tol)) return false;
      var n = fitted.ZAxis;
      var o = fitted.Origin;
      if (Math.Abs(n * Vector3d.ZAxis) > 0.9999) plane = new Plane(o, Vector3d.XAxis, Vector3d.YAxis);
      else if (Math.Abs(n * Vector3d.YAxis) > 0.9999) plane = new Plane(o, Vector3d.XAxis, Vector3d.ZAxis);
      else if (Math.Abs(n * Vector3d.XAxis) > 0.9999) plane = new Plane(o, Vector3d.YAxis, Vector3d.ZAxis);
      else plane = fitted;
      return true;
    }

    static List<Point3d> Discretise(Curve curve, double tolerance)
    {
      if (curve.TryGetPolyline(out var pl)) return pl.ToList();
      var polyline = curve.ToPolyline(tolerance, 0.05, 0.0, 0.0);
      if (polyline != null && polyline.TryGetPolyline(out pl)) return pl.ToList();
      // A degenerate curve can fail to divide; an empty loop then reads as "no area" instead of crashing.
      var ts = curve.DivideByCount(256, true);
      return ts == null ? new List<Point3d>() : ts.Select(curve.PointAt).ToList();
    }
  }
}
