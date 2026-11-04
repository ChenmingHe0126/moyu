using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Games.Common;
using Moyu.Games.Snake.Core;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Games.Snake
{
  /// <summary>
  /// The part of the boss-mode circulation study that depends only on the surface: a slope analysis mesh, contour
  /// lines and the site edge. Built once per game session.
  /// </summary>
  public sealed class SiteAnalysis
  {
    /// <summary>Slope at the top of the colour scale.</summary>
    public const double MaxSlope = 0.6;

    static readonly (double at, Color colour)[] Ramp =
    {
      (0, Color.FromArgb(176, 222, 200)), (0.25, Color.FromArgb(236, 232, 160)), (0.55, Color.FromArgb(244, 200, 140)), (1, Color.FromArgb(236, 150, 125)),
    };

    public readonly Mesh Slope;
    public readonly Curve[] Contours;
    public readonly Polyline[] Edges;

    public SiteAnalysis(SurfaceField field, double tolerance)
    {
      Slope = field.Mesh.DuplicateMesh();
      Slope.VertexColors.Clear();
      for (int i = 0; i < Slope.Vertices.Count; i++)
      {
        var n = (Vector3d)Slope.Normals[i];
        double nz = Math.Abs(n.Z);
        double slope = Math.Sqrt(Math.Max(0, 1 - nz * nz)) / Math.Max(nz, 1e-3);
        Slope.VertexColors.Add(Colour(slope / MaxSlope));
      }
      Edges = SiteEdges(field);

      var box = Slope.GetBoundingBox(false);
      double rise = box.Max.Z - box.Min.Z;
      Contours = Array.Empty<Curve>();
      if (rise > field.Step * 0.5)
      {
        // Contour a copy floated off the surface along its normals, so the lines are not swallowed by the shaded mesh.
        var lifted = Slope.DuplicateMesh();
        for (int i = 0; i < lifted.Vertices.Count; i++)
          lifted.Vertices.SetVertex(i, (Point3d)lifted.Vertices[i] + (Vector3d)lifted.Normals[i] * (field.Step * 0.02));
        double interval = rise / 16;
        Contours = Mesh.CreateContourCurves(lifted, new Point3d(0, 0, box.Min.Z + interval * 0.5), new Point3d(0, 0, box.Max.Z), interval, tolerance) ?? Array.Empty<Curve>();
      }
    }

    /// <summary>
    /// The site boundary: every side of a playable cell with no neighbour across it, so a trimmed edge and a hole read
    /// as edges while the seam of a closed surface (where the mesh merely repeats its vertices) does not.
    /// </summary>
    static Polyline[] SiteEdges(SurfaceField field)
    {
      var grid = field.Grid;
      double lift = field.Step * 0.02;
      var edges = new List<Polyline>();
      void Side(int k0, int l0, int dk, int dl)
      {
        var line = new Polyline(3);
        for (int s = 0; s <= 2; s++) line.Add(field.Sample(k0 + dk * s, l0 + dl * s) + field.SampleNormal(k0 + dk * s, l0 + dl * s) * lift);
        edges.Add(line);
      }
      foreach (var c in grid.FreeCells())
      {
        int k = 2 * c.U, l = 2 * c.V;
        if (!grid.TryStep(c, -1, 0, out _)) Side(k, l, 0, 1);
        if (!grid.TryStep(c, 1, 0, out _)) Side(k + 2, l, 0, 1);
        if (!grid.TryStep(c, 0, -1, out _)) Side(k, l, 1, 0);
        if (!grid.TryStep(c, 0, 1, out _)) Side(k, l + 2, 1, 0);
      }
      return edges.ToArray();
    }

    /// <summary>Colour of the slope scale at 0 (flat) … 1 (≥ <see cref="MaxSlope"/>).</summary>
    public static Color Colour(double t)
    {
      t = Math.Max(0, Math.Min(1, t));
      for (int i = 1; i < Ramp.Length; i++)
        if (t <= Ramp[i].at)
        {
          var (a, ca) = Ramp[i - 1];
          var (b, cb) = Ramp[i];
          double f = (t - a) / (b - a);
          return Color.FromArgb((int)(ca.R + (cb.R - ca.R) * f), (int)(ca.G + (cb.G - ca.G) * f), (int)(ca.B + (cb.B - ca.B) * f));
        }
      return Ramp[Ramp.Length - 1].colour;
    }
  }

  /// <summary>
  /// What the boss sees: a landscape design over the slope analysis. The snake's body becomes a paved path from A to
  /// B with direction arrows; trees (a woodland belt along the edge, an avenue along the path, specimens in the
  /// open), benches beside the path, scale figures, and the items as small paved plazas named as a landscape plan
  /// would name them. The figures are measured on the body and the plan itself.
  /// </summary>
  public sealed class CirculationDisguise
  {
    static readonly Color RouteInk = Color.FromArgb(34, 36, 40), NodeInk = Color.FromArgb(52, 58, 64), Paving = Color.FromArgb(233, 222, 200),
      PavingEdge = Color.FromArgb(150, 140, 122), Trunk = Color.FromArgb(118, 98, 80), Wood = Color.FromArgb(176, 138, 98), Shade = Color.FromArgb(60, 70, 64);
    static readonly Color[] Leaves = { Color.FromArgb(150, 192, 136), Color.FromArgb(132, 180, 124), Color.FromArgb(166, 202, 146) };
    static readonly Color Conifer = Color.FromArgb(104, 150, 116);
    static readonly Color[] Clothes = { Color.FromArgb(88, 96, 108), Color.FromArgb(232, 226, 214), Color.FromArgb(200, 125, 94), Color.FromArgb(96, 130, 170) };
    static readonly string[] PlazaNames = { "Lookout", "Pavilion", "Rain garden", "Picnic lawn", "Play area", "Orchard", "Reading terrace", "Pond edge" };

    public readonly SiteAnalysis Site;
    public readonly Curve Route;
    public readonly RouteMetrics Metrics;
    public readonly LandscapePlan Plan;
    public readonly bool IsDecoy;
    public readonly Point3d A, B;
    public readonly (Point3d at, Vector3d normal, string label)[] Nodes;

    readonly SurfaceField _field;
    readonly double _metre;
    readonly (Point3d at, Vector3d dir)[] _arrows;
    readonly Mesh _path = new Mesh(), _plazas = new Mesh(), _benches = new Mesh(), _shadows = new Mesh();
    readonly List<Polyline> _pathEdges = new List<Polyline>();
    readonly List<(Point3d foot, Vector3d normal, double radius, bool evergreen, int tone)> _trees = new List<(Point3d, Vector3d, double, bool, int)>();
    readonly List<(Point3d foot, Vector3d normal, int clothes)> _people = new List<(Point3d, Vector3d, int)>();
    static readonly Mesh UnitSphere = Mesh.CreateFromSphere(new Sphere(Point3d.Origin, 1), 24, 16);
    static readonly Mesh UnitCone = Mesh.CreateFromCone(new Cone(new Plane(new Point3d(0, 0, 1), -Vector3d.ZAxis), 1, 1), 1, 18);
    static readonly Mesh UnitCylinder = Mesh.CreateFromCylinder(new Cylinder(new Circle(Plane.WorldXY, 1), 1), 1, 12);
    readonly Dictionary<(int, bool), Mesh> _painted = new Dictionary<(int, bool), Mesh>();

    /// <param name="body">Head first, as the game keeps it; the route runs from the tail (A) to the head (B).</param>
    public CirculationDisguise(SurfaceField field, SiteAnalysis site, IEnumerable<Cell> body, IEnumerable<Cell> nodes, int seed, double toMetres)
    {
      _field = field;
      _metre = toMetres > 0 ? 1 / toMetres : 1;
      Site = site;
      var cells = body.Reverse().ToList();
      IsDecoy = cells.Count < 6;
      if (IsDecoy) cells = RouteMetrics.Decoy(field.Grid, seed);
      Metrics = RouteMetrics.From(cells, field.Grid, toMetres);
      var nodeCells = nodes.ToList();
      Plan = new LandscapePlan(field.Grid, cells, nodeCells, seed);
      Route = RouteCurve(field, cells);
      if (Route != null)
      {
        A = Route.PointAtStart;
        B = Route.PointAtEnd;
        var ts = Route.DivideByLength(field.Step * 2.5, false) ?? Array.Empty<double>();
        _arrows = ts.Select(t => (Route.PointAt(t), Route.TangentAt(t))).ToArray();
        BuildPath();
        BuildBenches(cells);
      }
      else _arrows = Array.Empty<(Point3d, Vector3d)>();
      Nodes = nodeCells.Select((c, i) => (field.Lifted(c, field.Step * 0.05), field.Normal(c), $"N{i + 1}  {PlazaNames[i % PlazaNames.Length]}")).ToArray();
      BuildPlazas(nodeCells);
      BuildPlanting();
    }

    /// <summary>The body as a smooth curve just above the surface, or null when it is shorter than two cells.</summary>
    public static Curve RouteCurve(SurfaceField field, IReadOnlyList<Cell> cells)
    {
      var pts = cells.Select(c => field.Lifted(c, field.Step * 0.12)).ToList();
      if (pts.Count < 2) return null;
      if (pts.Count < 4) return new PolylineCurve(pts);
      // Every other cell, so the grid's right angles relax into the curves of a drawn path (both ends kept).
      if (pts.Count > 10) pts = pts.Where((p, i) => i % 2 == 0 || i == pts.Count - 1).ToList();
      return Curve.CreateControlPointCurve(pts, 3);
    }

    Vector3d NormalNear(Point3d p)
    {
      var mp = _field.Mesh.ClosestMeshPoint(p, 0);
      var n = mp != null ? _field.Mesh.NormalAt(mp) : Vector3d.ZAxis;
      return n.Unitize() ? n : Vector3d.ZAxis;
    }

    /// <summary>A paved strip about 1.3 m wide along the route, with its two edges.</summary>
    void BuildPath()
    {
      double half = _field.Step * 0.45;
      double len = Route.GetLength();
      int n = Math.Max(2, (int)(len / (_field.Step * 0.3)));
      var left = new Polyline();
      var right = new Polyline();
      for (int i = 0; i <= n; i++)
      {
        if (!Route.LengthParameter(len * i / n, out double t)) t = Route.Domain.ParameterAt(i / (double)n);
        var p = Route.PointAt(t);
        var normal = NormalNear(p);
        var side = Vector3d.CrossProduct(Route.TangentAt(t), normal);
        if (!side.Unitize()) side = Vector3d.XAxis;
        var a = p + side * half - normal * (_field.Step * 0.08);
        var b = p - side * half - normal * (_field.Step * 0.08);
        _path.Vertices.Add(a);
        _path.Vertices.Add(b);
        _path.VertexColors.Add(Paving);
        _path.VertexColors.Add(Paving);
        if (i > 0) _path.Faces.AddFace(2 * i - 2, 2 * i - 1, 2 * i + 1, 2 * i);
        left.Add(a + normal * (_field.Step * 0.01));
        right.Add(b + normal * (_field.Step * 0.01));
      }
      _pathEdges.Add(left);
      _pathEdges.Add(right);
    }

    void BuildBenches(IReadOnlyList<Cell> cells)
    {
      foreach (var (index, sideSign) in Plan.Benches)
      {
        if (index >= cells.Count || !LandscapePlan.Heading(_field.Grid, cells, index, out int du, out int dv)) continue;
        var c = cells[index];
        var normal = _field.Normal(c);
        var along = _field.Direction(c, du, dv);
        var side = Vector3d.CrossProduct(along, normal);
        if (!side.Unitize()) continue;
        var at = _field.Centre(c) + side * (sideSign * _field.Step * 0.8);
        var plane = new Plane(at, along, side);
        var box = new Box(plane, new Interval(-0.8 * _metre, 0.8 * _metre), new Interval(-0.25 * _metre, 0.25 * _metre), new Interval(0, 0.45 * _metre));
        var m = Mesh.CreateFromBox(box, 1, 1, 1);
        if (m == null) continue;
        m.Normals.ComputeNormals();
        Toon.Paint(m, Wood, Shade);
        _benches.Append(m);
      }
    }

    void BuildPlazas(IReadOnlyList<Cell> nodes)
    {
      foreach (var c in nodes)
      {
        var plane = new Plane(_field.Lifted(c, _field.Step * 0.03), _field.Normal(c));
        int k = _plazas.Vertices.Count;
        _plazas.Vertices.Add(plane.Origin);
        _plazas.VertexColors.Add(Paving);
        const int n = 32;
        for (int i = 0; i < n; i++)
        {
          _plazas.Vertices.Add(plane.PointAt(Math.Cos(i * 2 * Math.PI / n) * _field.Step * 1.15, Math.Sin(i * 2 * Math.PI / n) * _field.Step * 1.15));
          _plazas.VertexColors.Add(Paving);
        }
        for (int i = 0; i < n; i++) _plazas.Faces.AddFace(k, k + 1 + i, k + 1 + (i + 1) % n);
      }
    }

    void BuildPlanting()
    {
      var rng = new Random(17);
      foreach (var t in Plan.Trees)
      {
        var normal = Vector3d.ZAxis;
        if (_field.Closed) normal = _field.Normal(t.Cell);
        var u = _field.Direction(t.Cell, 1, 0);
        var v = _field.Direction(t.Cell, 0, 1);
        var foot = _field.Centre(t.Cell) + (u * t.Du + v * t.Dv) * _field.Step;
        double r = t.Radius * _field.Step * 0.85;
        _trees.Add((foot, normal, r, t.Evergreen, rng.Next(Leaves.Length)));
        // A soft shadow on the ground, nudged away from the light.
        var plane = new Plane(foot + _field.Normal(t.Cell) * (_field.Step * 0.02) + new Vector3d(0.25, 0.35, 0) * r, _field.Normal(t.Cell));
        int k = _shadows.Vertices.Count;
        _shadows.Vertices.Add(plane.Origin);
        const int n = 20;
        for (int i = 0; i < n; i++) _shadows.Vertices.Add(plane.PointAt(Math.Cos(i * 2 * Math.PI / n) * r * 0.95, Math.Sin(i * 2 * Math.PI / n) * r * 0.95));
        for (int i = 0; i < n; i++) _shadows.Faces.AddFace(k, k + 1 + i, k + 1 + (i + 1) % n);
      }
      foreach (var c in Plan.People)
      {
        var u = _field.Direction(c, 1, 0);
        _people.Add((_field.Centre(c) + u * (_field.Step * (rng.NextDouble() - 0.5) * 0.4), _field.Closed ? _field.Normal(c) : Vector3d.ZAxis, rng.Next(Clothes.Length)));
      }
    }

    Mesh Painted(int tone, bool evergreen)
    {
      if (_painted.TryGetValue((tone, evergreen), out var m)) return m;
      m = (evergreen ? UnitCone : UnitSphere).DuplicateMesh();
      m.Normals.ComputeNormals();
      Toon.Paint(m, evergreen ? Conifer : Leaves[tone], Shade);
      _painted[(tone, evergreen)] = m;
      return m;
    }

    static readonly DisplayMaterial ShadowMaterial = new DisplayMaterial(Color.Black, 0.78) { Emission = Color.FromArgb(70, 80, 72) };
    static readonly DisplayMaterial[] ClothesMaterial = Clothes.Select(c => new DisplayMaterial(c)).ToArray();
    static readonly DisplayMaterial SkinMaterial = new DisplayMaterial(Color.FromArgb(214, 186, 160));

    public void Draw(DisplayPipeline d)
    {
      double k = GameConduit.UiScale(d.DpiScale, d.FrameSize);
      d.DrawMeshFalseColors(Site.Slope);
      foreach (var c in Site.Contours) d.DrawCurve(c, Color.FromArgb(110, 112, 116), 1);
      foreach (var e in Site.Edges) d.DrawPolyline(e, Color.FromArgb(90, 94, 100), 2);
      if (_shadows.Vertices.Count > 0)
      {
        d.EnableDepthWriting(false);
        d.DrawMeshShaded(_shadows, ShadowMaterial);
        d.EnableDepthWriting(true);
      }
      if (_plazas.Vertices.Count > 0) d.DrawMeshFalseColors(_plazas);
      foreach (var (at, normal, _) in Nodes) d.DrawCircle(new Circle(new Plane(at + normal * (_field.Step * 0.03), normal), _field.Step * 1.15), PavingEdge, 1);
      if (Route != null)
      {
        if (_path.Vertices.Count > 0) d.DrawMeshFalseColors(_path);
        foreach (var e in _pathEdges) d.DrawPolyline(e, PavingEdge, 2);
        foreach (var (at, dir) in _arrows) d.DrawArrowHead(at, dir, RouteInk, (int)Math.Round(9 * k), 0);
      }
      if (_benches.Vertices.Count > 0) d.DrawMeshFalseColors(_benches);
      foreach (var (foot, normal, r, evergreen, tone) in _trees)
      {
        double trunk = evergreen ? r * 0.45 : r * 0.95;
        d.DrawLine(foot, foot + normal * (trunk + r * 0.2), Trunk, 3);
        var crown = new Plane(foot + normal * (evergreen ? trunk : trunk + r * 0.8), normal);
        var xf = evergreen
          ? Transform.PlaneToPlane(Plane.WorldXY, crown) * Transform.Scale(Plane.WorldXY, r * 0.8, r * 0.8, r * 2.6)
          : Transform.PlaneToPlane(Plane.WorldXY, crown) * Transform.Scale(Plane.WorldXY, r, r, r * 0.85);
        d.PushModelTransform(xf);
        d.DrawMeshFalseColors(Painted(tone, evergreen));
        d.PopModelTransform();
      }
      foreach (var (foot, normal, clothes) in _people)
      {
        var plane = new Plane(foot, normal);
        d.PushModelTransform(Transform.PlaneToPlane(Plane.WorldXY, plane) * Transform.Scale(Plane.WorldXY, 0.2 * _metre, 0.2 * _metre, 1.4 * _metre));
        d.DrawMeshShaded(UnitCylinder, ClothesMaterial[clothes]);
        d.PopModelTransform();
        d.PushModelTransform(Transform.Translation(plane.Origin + normal * (1.58 * _metre) - Point3d.Origin) * Transform.Scale(Point3d.Origin, 0.13 * _metre));
        d.DrawMeshShaded(UnitSphere, SkinMaterial);
        d.PopModelTransform();
      }
      if (Route == null) return;
      d.DrawDot(A, " A ", Color.FromArgb(40, 44, 52), Color.White);
      d.DrawDot(B, " B ", Color.FromArgb(214, 52, 52), Color.White);
    }

    /// <summary>Plaza names, drawn on top in screen space; a plaza on the far side of a closed form gets none.</summary>
    public void DrawLabels(DisplayPipeline d, RhinoViewport vp)
    {
      double k = GameConduit.UiScale(d.DpiScale, d.FrameSize);
      int height = (int)Math.Round(12 * k);
      var look = vp.CameraDirection;
      foreach (var (at, normal, label) in Nodes)
      {
        if (normal * look > 0) continue;
        var p = vp.WorldToClient(at + normal * _field.Step * 0.3);
        var box = d.Measure2dText(label, new Point2d(0, 0), false, 0, height, GameConduit.LatinBold);
        d.Draw2dRectangle(new Rectangle((int)(p.X - box.Width / 2 - 5 * k), (int)(p.Y - 31 * k), (int)(box.Width + 10 * k), (int)(20 * k)), Color.Transparent, 0, Color.FromArgb(225, 250, 250, 248));
        d.Draw2dText(label, NodeInk, new Point2d(p.X, p.Y - 22 * k), true, height, GameConduit.LatinBold);
      }
    }

    /// <summary>An aerial three-quarter view of the whole site, nudged right and down clear of the card in the top-left corner.</summary>
    public static void Overview(SurfaceField field, double distance, out Point3d location, out Point3d target)
    {
      var box = field.Bounds;
      double size = box.Diagonal.Length;
      var dir = new Vector3d(0.32, -0.82, 0.64);
      dir.Unitize();
      var right = Vector3d.CrossProduct(-dir, Vector3d.ZAxis);
      right.Unitize();
      var up = Vector3d.CrossProduct(right, -dir);
      target = box.Center - right * (size * 0.07) + up * (size * 0.02);
      location = target + dir * size * distance;
    }
  }
}
