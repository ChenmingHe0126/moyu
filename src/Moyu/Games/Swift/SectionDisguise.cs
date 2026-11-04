using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Games.Common;
using Moyu.Games.Swift.Core;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Games.Swift
{
  /// <summary>
  /// What the boss sees: the game's own section as a drawing, so the cut is barely a cut. The floor and roof slabs and
  /// the cross walls are in poché exactly where the game had them, each opening where the swift was flying through,
  /// the back wall's tall windows seen beyond; rooflights over the bays, scale figures and benches, levels and a
  /// dimension chain. Over it a climate study: noon sun at both solstices through the rooflights, a daylight-factor
  /// strip along the workplane, and the airflow coloured by speed — the main line is the swift's own flight, with
  /// the probe P1 where the swift was. Everything comes from <see cref="SectionModel"/>, in the game's view units.
  /// </summary>
  public sealed class SectionDisguise
  {
    static readonly Color Ink = Color.FromArgb(46, 50, 56), Poche = Color.FromArgb(52, 56, 63), Beyond = Color.FromArgb(150, 156, 164),
      Faint = Color.FromArgb(200, 205, 211), Glass = Color.FromArgb(120, 170, 210), Summer = Color.FromArgb(240, 128, 40), Winter = Color.FromArgb(226, 178, 30),
      Earth = Color.FromArgb(170, 160, 150), AirInk = Color.FromArgb(40, 120, 170), Grey = Color.FromArgb(96, 102, 110);

    static readonly (double at, Color c)[] DfRamp =
    {
      (0, Color.FromArgb(44, 62, 140)), (1, Color.FromArgb(47, 143, 201)), (2, Color.FromArgb(63, 191, 159)),
      (3, Color.FromArgb(166, 216, 84)), (4, Color.FromArgb(242, 212, 61)), (5, Color.FromArgb(245, 154, 59)),
    };

    static readonly (double at, Color c)[] AirRamp =
    {
      (0, Color.FromArgb(120, 178, 230)), (0.4, Color.FromArgb(52, 190, 200)), (0.8, Color.FromArgb(242, 193, 78)), (1.2, Color.FromArgb(232, 85, 61)),
    };

    public readonly SectionModel Model;
    readonly SwiftSession _s;
    readonly Mesh _poche = new Mesh(), _strips = new Mesh();
    readonly List<Line> _hatch = new List<Line>(), _glass = new List<Line>(), _beyond = new List<Line>(), _faint = new List<Line>(), _ink = new List<Line>();
    readonly List<(Point3d at, string text, int size, bool bold, Color colour, int align, bool masked)> _labels = new List<(Point3d, string, int, bool, Color, int, bool)>();
    readonly List<(Line line, Color colour)> _rays = new List<(Line, Color)>();
    /// <summary>Airflow segments batched by colour band and width, so a frame draws a few dozen calls, not thousands.</summary>
    readonly Dictionary<(Color c, int width), List<Line>> _air = new Dictionary<(Color, int), List<Line>>();
    readonly List<(Point3d at, Vector3d dir, Color c)> _arrows = new List<(Point3d, Vector3d, Color)>();
    readonly List<Point3d> _dots = new List<Point3d>();

    public SectionDisguise(SwiftSession s, SectionModel model)
    {
      _s = s;
      Model = model;
      Build();
    }

    double M => Model.Metres;
    Point3d P(double x, double y, double z = 0) => _s.At(x, y, z);
    Point3d P(V2 v, double z = 0) => P(v.X, v.Y, z);
    void L(List<Line> to, double x0, double y0, double x1, double y1, double z = 0.01) => to.Add(new Line(P(x0, y0, z), P(x1, y1, z)));
    /// <summary>align: 0 starts at the point, 1 centres on it, 2 ends at it. A masked label sits on a white patch, as call-outs do on a drawing.</summary>
    void Label(double x, double y, string text, int size = 11, bool bold = false, Color? colour = null, int align = 1, bool masked = false) =>
      _labels.Add((P(x, y), text, size, bold, colour ?? Ink, align, masked));

    static Color Ramp((double at, Color c)[] ramp, double v)
    {
      if (v <= ramp[0].at) return ramp[0].c;
      for (int i = 1; i < ramp.Length; i++)
        if (v <= ramp[i].at) return Toon.Mix(ramp[i - 1].c, ramp[i].c, (v - ramp[i - 1].at) / (ramp[i].at - ramp[i - 1].at));
      return ramp[ramp.Length - 1].c;
    }

    void Build()
    {
      var md = Model;
      double left = md.Input.Left, right = md.Input.Right;

      // Ground under the floor slab.
      for (double x = left; x < right; x += 0.5) L(_hatch, x, -SectionModel.FloorSlab - 0.05, x + 0.6, -SectionModel.FloorSlab - 0.65);
      for (double x = left + 0.25; x < right; x += 1.0) L(_hatch, x, -1.4, x + 0.6, -2.0);
      L(_ink, left, -SectionModel.FloorSlab, right, -SectionModel.FloorSlab);

      foreach (var poly in md.Cut)
      {
        var ring = new Polyline(poly.Select(v => P(v, 0.02)));
        ring.Add(ring[0]);
        var mesh = Mesh.CreateFromClosedPolyline(ring);
        if (mesh != null) _poche.Append(mesh);
      }
      _poche.VertexColors.CreateMonotoneMesh(Poche);

      // The back wall's windows, seen beyond the cut, on the game's rhythm.
      for (double x = left - md.Input.WindowPhase; x < right; x += SwiftGame.WindowPitch)
      {
        double a = x + (SwiftGame.WindowPitch - SwiftGame.WindowWidth) / 2, b = a + SwiftGame.WindowWidth;
        double s0 = SwiftGame.WindowSill, s1 = SwiftGame.WindowHead;
        L(_faint, a, s0, b, s0, -0.05);
        L(_faint, b, s0, b, s1, -0.05);
        L(_faint, b, s1, a, s1, -0.05);
        L(_faint, a, s1, a, s0, -0.05);
        L(_faint, (a + b) / 2, s0, (a + b) / 2, s1, -0.05);
        L(_faint, a, s1 - 1.9, b, s1 - 1.85, -0.05);
        L(_faint, a - 0.12, s0 - 0.12, b + 0.12, s0 - 0.12, -0.05);
      }

      // Openings: sill and head across each wall; rooflight glazing in a shallow ridge over the kerbs.
      foreach (var w in md.Walls)
      {
        L(_ink, w.X0, w.Lo, w.X1, w.Lo, 0.03);
        L(_ink, w.X0, w.Hi, w.X1, w.Hi, 0.03);
      }
      double top = md.RoofTop + SectionModel.Kerb;
      foreach (var b in md.Bays.Where(b => b.HasRooflight))
      {
        double a = b.LightX0 - 0.18, c = b.LightX1 + 0.18, m = (a + c) / 2;
        L(_glass, a, top, m, top + 0.35, 0.03);
        L(_glass, m, top + 0.35, c, top, 0.03);
        L(_ink, a, top, c, top, 0.03);
      }

      Entourage();
      Daylight();
      Sun();
      Air();
      Annotations();
    }

    // ---------------------------------------------------------------- entourage

    void Person(double x, bool facingRight)
    {
      // A 1.75 m figure.
      double k = 1 / M, d = facingRight ? 1 : -1;
      for (int i = 0; i < 16; i++)
      {
        double a0 = i * Math.PI / 8, a1 = (i + 1) * Math.PI / 8;
        L(_beyond, x + 0.11 * k * Math.Cos(a0), 1.62 * k + 0.11 * k * Math.Sin(a0), x + 0.11 * k * Math.Cos(a1), 1.62 * k + 0.11 * k * Math.Sin(a1), 0.005);
      }
      L(_beyond, x - 0.18 * k, 1.44 * k, x + 0.18 * k, 1.44 * k, 0.005);
      L(_beyond, x - 0.18 * k, 1.44 * k, x - 0.13 * k, 0.86 * k, 0.005);
      L(_beyond, x + 0.18 * k, 1.44 * k, x + 0.13 * k, 0.86 * k, 0.005);
      L(_beyond, x - 0.13 * k, 0.86 * k, x + 0.13 * k, 0.86 * k, 0.005);
      L(_beyond, x - 0.08 * k, 0.86 * k, x - 0.1 * k, 0, 0.005);
      L(_beyond, x + 0.08 * k, 0.86 * k, x + (0.1 + 0.06 * d) * k, 0, 0.005);
    }

    void Bench(double x0, double x1)
    {
      double k = 1 / M;
      L(_beyond, x0, 0.45 * k, x1, 0.45 * k, 0.005);
      L(_beyond, x0, 0.4 * k, x1, 0.4 * k, 0.005);
      L(_beyond, x0 + 0.15 * k, 0.4 * k, x0 + 0.15 * k, 0, 0.005);
      L(_beyond, x1 - 0.15 * k, 0.4 * k, x1 - 0.15 * k, 0, 0.005);
    }

    void Entourage()
    {
      int i = 0;
      foreach (var b in Model.Bays.Where(b => b.Width > 4.5))
      {
        if (i % 2 == 0) Person(b.X0 + 1.4, true);
        else Person(b.X1 - 1.4, false);
        if (i % 3 != 2) Bench(b.Mid - 1.2, b.Mid + 1.2);
        i++;
      }
    }

    // ---------------------------------------------------------------- analysis

    void Daylight()
    {
      const double band = 0.3;
      var pts = Model.Daylight.OrderBy(q => q.x).ToList();
      int start = 0;
      for (int i = 1; i <= pts.Count; i++)
      {
        if (i < pts.Count && pts[i].x - pts[i - 1].x <= 0.5) continue;
        var run = pts.Skip(start).Take(i - start).ToList();
        var peak = run.OrderByDescending(q => q.df).First();
        if (run.Count > 4) Label(peak.x, peak.y + 0.75, $"DF {peak.df:F1} %", 10, false, Grey);
        for (int k = 0; k + 1 < run.Count; k++)
        {
          int v = _strips.Vertices.Count;
          var ca = Ramp(DfRamp, run[k].df);
          var cb = Ramp(DfRamp, run[k + 1].df);
          _strips.Vertices.Add(P(run[k].x, run[k].y - band / 2, 0.04));
          _strips.Vertices.Add(P(run[k + 1].x, run[k].y - band / 2, 0.04));
          _strips.Vertices.Add(P(run[k + 1].x, run[k].y + band / 2, 0.04));
          _strips.Vertices.Add(P(run[k].x, run[k].y + band / 2, 0.04));
          _strips.VertexColors.Add(ca);
          _strips.VertexColors.Add(cb);
          _strips.VertexColors.Add(cb);
          _strips.VertexColors.Add(ca);
          _strips.Faces.AddFace(v, v + 1, v + 2, v + 3);
        }
        start = i;
      }
    }

    void Sun()
    {
      foreach (var ray in Model.Rays)
      {
        _rays.Add((new Line(P(ray.From, 0.05), P(ray.To, 0.05)), ray.Summer ? Summer : Winter));
        if (ray.OnWall) _dots.Add(P(ray.To, 0.05));
      }
      // Name the two suns where their rays come in.
      var s = Model.Rays.Where(r => r.Summer).OrderBy(r => r.From.X).FirstOrDefault();
      var w = Model.Rays.Where(r => !r.Summer).OrderBy(r => r.From.X).FirstOrDefault();
      if (s != null) Label(s.From.X + 0.3, s.From.Y + 0.55, $"21 JUN 12:00  {SectionModel.SummerAltitude:F1}°", 10, true, Summer, 0, true);
      if (w != null) Label(w.From.X - 0.3, w.From.Y + 0.55, $"21 DEC 12:00  {SectionModel.WinterAltitude:F1}°", 10, true, Color.FromArgb(190, 146, 12), 2, true);
    }

    void Air()
    {
      for (int n = 0; n < Model.Streamlines.Count; n++)
      {
        var line = Model.Streamlines[n];
        int width = n == 0 ? 4 : line.Stack ? 2 : 3;
        for (int i = 0; i + 1 < line.Points.Count; i++)
        {
          // Speeds in steps of 0.05 m/s: colour bands fine enough to read as a gradient.
          var key = (Ramp(AirRamp, Math.Round((line.Speeds[i] + line.Speeds[i + 1]) * 10) / 20), width);
          if (!_air.TryGetValue(key, out var lines)) _air[key] = lines = new List<Line>();
          lines.Add(new Line(P(line.Points[i], 0.06), P(line.Points[i + 1], 0.06)));
        }
        for (int i = 7; i + 1 < line.Points.Count; i += line.Stack ? 14 : 22)
          _arrows.Add((P(line.Points[i], 0.06), P(line.Points[i + 1], 0.06) - P(line.Points[i - 1], 0.06), Ramp(AirRamp, line.Speeds[i])));
      }
    }

    void Annotations()
    {
      var md = Model;
      var m = md.Metrics;
      double h = SectionModel.Height;
      // Levels at the right.
      double lx = 31;
      foreach (var (y, text, below) in new[] { (0.0, "FFL  ±0.000", false), (h, $"SOFFIT  +{h * M:F3}", true), (md.RoofTop, $"ROOF  +{md.RoofTop * M:F3}", false) })
      {
        L(_ink, lx - 0.3, y, lx + 2.4, y, 0.02);
        L(_ink, lx, y, lx + 0.25, y + 0.32, 0.02);
        L(_ink, lx, y, lx - 0.25, y + 0.32, 0.02);
        L(_ink, lx - 0.25, y + 0.32, lx + 0.25, y + 0.32, 0.02);
        Label(lx + 0.45, below ? y - 0.55 : y + 0.62, text, 10, false, Ink, 0, true);
      }
      // A dimension chain over the roof: centre to centre of the cross walls on screen.
      double dy = md.RoofTop + SectionModel.Kerb + 3.1;
      var ticks = md.Walls.Select(w => w.Middle).Where(x => x > -5 && x < 28).ToList();
      if (ticks.Count >= 2)
      {
        L(_ink, ticks.First() - 1.0, dy, ticks.Last() + 1.0, dy, 0.02);
        foreach (double x in ticks)
        {
          L(_ink, x - 0.22, dy - 0.22, x + 0.22, dy + 0.22, 0.02);
          L(_faint, x, dy - 0.5, x, md.RoofTop + 0.05, 0.02);
        }
        for (int i = 0; i + 1 < ticks.Count; i++)
          Label((ticks[i] + ticks[i + 1]) / 2, dy + 0.55, $"{(ticks[i + 1] - ticks[i]) * M * 1000:N0}".Replace(",", " "), 10);
      }
      foreach (var b in md.Bays.Where(b => b.Width > 5 && b.Mid > -4 && b.Mid < 29)) Label(b.Mid, h - 1.1, b.Name, 10, true, Grey);
      // Airflow call-outs, and the probe where the swift was.
      Label(-2.6, 8.4, $"WIND  {m.Wind:F1} m/s  →", 10, true, AirInk, 1, true);
      var p = md.Input.Probe;
      _dots.Add(P(p, 0.07));
      L(_ink, p.X - 0.45, p.Y, p.X + 0.45, p.Y, 0.07);
      L(_ink, p.X, p.Y - 0.45, p.X, p.Y + 0.45, 0.07);
      Label(p.X + 0.7, p.Y + 0.55, $"P1  {m.ProbeSpeed:F2} m/s", 10, true, Ink, 0, true);
      var lit = md.Bays.FirstOrDefault(b => b.HasRooflight && b.Mid > 6 && b.Mid < 26);
      if (lit != null) Label((lit.LightX0 + lit.LightX1) / 2 + 2.6, md.RoofTop + SectionModel.Kerb + 1.0, $"STACK  {m.StackAch:F1} ach", 10, true, AirInk, 0, true);
    }

    // ---------------------------------------------------------------- drawing

    public BoundingBox Bounds => new BoundingBox(new[] { P(Model.Input.Left, -2.5, -0.2), P(Model.Input.Right, Model.RoofTop + 4, 0.2) });

    /// <summary>The cut outlines (walls, slabs, kerbs) as curves on the section plane (component output).</summary>
    public IEnumerable<Curve> CutCurves() =>
      Model.Cut.Select(poly => (Curve)new PolylineCurve(poly.Select(v => P(v)).Concat(new[] { P(poly[0]) })));

    public void Draw(DisplayPipeline d)
    {
      d.DrawLines(_faint, Faint, 1);
      d.DrawLines(_hatch, Earth, 1);
      d.DrawLines(_beyond, Beyond, 1);
      if (_strips.Vertices.Count > 0) d.DrawMeshFalseColors(_strips);
      if (_poche.Vertices.Count > 0) d.DrawMeshFalseColors(_poche);
      d.DrawLines(_glass, Glass, 2);
      d.DrawLines(_ink, Ink, 1);
      foreach (var (line, c) in _rays) d.DrawLine(line, c, 1);
      foreach (var kv in _air) d.DrawLines(kv.Value, kv.Key.c, kv.Key.width);
      foreach (var (at, dir, c) in _arrows) d.DrawArrowHead(at, dir, c, 13, 0);
      foreach (var p in _dots) d.DrawPoint(p, PointStyle.RoundSimple, 3, Ink);
    }

    /// <summary>Labels, the metrics card, the legend and the title block, in screen space (laid out in HUD units, k pixels each).</summary>
    public void DrawOverlay(DisplayPipeline d, RhinoViewport vp, double bossTime)
    {
      double k = GameConduit.UiScale(d.DpiScale, d.FrameSize);
      foreach (var (at, text, size, bold, colour, align, masked) in _labels)
      {
        var p = vp.WorldToClient(at);
        var font = bold ? "Segoe UI Semibold" : "Segoe UI";
        int px = (int)Math.Round(size * k);
        var box = d.Measure2dText(text, new Point2d(0, 0), false, 0, px, font);
        double left = align == 1 ? p.X - box.Width / 2.0 : align == 2 ? p.X - box.Width : p.X, top = p.Y - px * 0.7;
        if (masked)
          d.Draw2dRectangle(new Rectangle((int)(left - 4 * k), (int)(top - 2 * k), box.Width + (int)(8 * k), (int)(px * 1.45 + 4 * k)), Color.Transparent, 0, Color.FromArgb(235, 248, 249, 250));
        d.Draw2dText(text, colour, new Point2d(left, top), false, px, font);
      }
      var frame = d.FrameSize;
      var m = Model.Metrics;
      int iter = (212 + (int)(bossTime / 1.2) * 3) % 400 + 1;
      DrawCard(d, k, iter, m);
      Legend(d, k, frame);
      double tx = frame.Width - 380 * k, ty = frame.Height - 62 * k;
      d.Draw2dText("SECTION A–A", Ink, new Point2d(tx, ty), false, (int)Math.Round(17 * k), "Segoe UI Semibold");
      d.Draw2dText("1:100", Ink, new Point2d(tx + 140 * k, ty + 4 * k), false, (int)Math.Round(12 * k), "Segoe UI");
      d.Draw2dLine(new PointF((float)tx, (float)(ty + 26 * k)), new PointF((float)(tx + 340 * k), (float)(ty + 26 * k)), Ink, 1);
      d.Draw2dText($"Long gallery  ·  daylight & natural ventilation  ·  lat {SectionModel.Latitude:F1}° N", Color.FromArgb(110, 116, 124), new Point2d(tx, ty + 32 * k), false, (int)Math.Round(11 * k), "Segoe UI");
    }

    static void DrawCard(DisplayPipeline d, double k, int iter, ClimateMetrics m)
    {
      var rows = new[]
      {
        ("Mean daylight factor", $"{m.MeanDf:F1} %"), ("Workplane ≥ 2 % DF", $"{m.Sda * 100:F0} %"), ("Rooflights", $"{m.Rooflights}  ({m.RooflightArea:F0} m²)"),
        ("Cross ventilation", $"{m.CrossAch:F1} ach"), ("Stack ventilation", $"{m.StackAch:F1} ach"), ("Air speed at P1", $"{m.ProbeSpeed:F2} m/s"),
      };
      var ink = Color.FromArgb(52, 58, 64);
      var grey = Color.FromArgb(120, 126, 134);
      int Px(double units) => (int)Math.Round(units * k);
      int x = Px(24), y = Px(24), w = Px(300), h = Px(112 + rows.Length * 22);
      d.Draw2dRectangle(new Rectangle(x, y, w, h), Color.FromArgb(206, 212, 218), 1, Color.FromArgb(242, 255, 255, 255));
      d.Draw2dText("Climate Study  ·  Section A–A", ink, new Point2d(x + Px(16), y + Px(14)), false, Px(15), "Segoe UI Semibold");
      d.Draw2dText($"Noon, 21 Jun / 21 Dec · wind {m.Wind:F1} m/s from S · ΔT 2.5 K", grey, new Point2d(x + Px(16), y + Px(38)), false, Px(11), "Segoe UI");
      for (int r = 0; r < rows.Length; r++)
      {
        d.Draw2dText(rows[r].Item1, grey, new Point2d(x + Px(16), y + Px(64 + r * 22)), false, Px(13), "Segoe UI");
        d.Draw2dText(rows[r].Item2, ink, new Point2d(x + Px(186), y + Px(64 + r * 22)), false, Px(13), "Segoe UI Semibold");
      }
      int py = y + Px(64 + rows.Length * 22 + 10);
      d.Draw2dText($"Solving airflow (k-ε)…  iteration {iter} / 400", grey, new Point2d(x + Px(16), py), false, Px(11), "Segoe UI");
      d.Draw2dRectangle(new Rectangle(x + Px(16), py + Px(20), w - Px(32), Px(5)), Color.FromArgb(222, 226, 230), 0, Color.FromArgb(222, 226, 230));
      d.Draw2dRectangle(new Rectangle(x + Px(16), py + Px(20), (int)((w - Px(32)) * iter / 400.0), Px(5)), Color.FromArgb(73, 80, 87), 0, Color.FromArgb(73, 80, 87));
    }

    /// <summary>One slim strip along the bottom-left: the two colour scales, the two suns and the flight path.</summary>
    static void Legend(DisplayPipeline d, double k, Size frame)
    {
      var ink = Color.FromArgb(52, 58, 64);
      var grey = Color.FromArgb(120, 126, 134);
      int Px(double units) => (int)Math.Round(units * k);
      int x = Px(24), y = frame.Height - Px(58);
      d.Draw2dRectangle(new Rectangle(x, y, Px(560), Px(44)), Color.FromArgb(206, 212, 218), 1, Color.FromArgb(242, 255, 255, 255));
      void Bar(int bx, string title, (double at, Color c)[] ramp, string lo, string hi)
      {
        d.Draw2dText(title, grey, new Point2d(bx, y + Px(7)), false, Px(10), "Segoe UI");
        int bw = Px(110);
        double max = ramp[ramp.Length - 1].at;
        for (int i = 0; i < bw; i++) d.Draw2dLine(new PointF(bx + i, y + Px(24)), new PointF(bx + i, y + Px(31)), Ramp(ramp, max * i / (bw - 1)), 1);
        d.Draw2dText(lo, ink, new Point2d(bx - Px(2), y + Px(31)), false, Px(9), "Segoe UI");
        d.Draw2dText(hi, ink, new Point2d(bx + bw - Px(14), y + Px(31)), false, Px(9), "Segoe UI");
      }
      Bar(x + Px(12), "Daylight factor %", DfRamp, "0", "5+");
      Bar(x + Px(148), "Air speed m/s", AirRamp, "0", "1.2+");
      d.Draw2dLine(new PointF(x + Px(290), y + Px(16)), new PointF(x + Px(314), y + Px(16)), Summer, 2);
      d.Draw2dText("Sun 21 Jun", ink, new Point2d(x + Px(320), y + Px(9)), false, Px(10), "Segoe UI");
      d.Draw2dLine(new PointF(x + Px(290), y + Px(31)), new PointF(x + Px(314), y + Px(31)), Winter, 2);
      d.Draw2dText("Sun 21 Dec", ink, new Point2d(x + Px(320), y + Px(24)), false, Px(10), "Segoe UI");
      d.Draw2dLine(new PointF(x + Px(412), y + Px(22)), new PointF(x + Px(436), y + Px(22)), Color.FromArgb(52, 190, 200), 4);
      d.Draw2dText("P1 = flight path", ink, new Point2d(x + Px(442), y + Px(15)), false, Px(10), "Segoe UI");
    }

    /// <summary>Straight on to the section, framed as the game frames the course, a touch further back.</summary>
    public void CameraFor(out Point3d location, out Point3d target)
    {
      target = P(14, 7.0);
      location = target + _s.Plane.ZAxis * (44 * _s.Size);
    }
  }
}
