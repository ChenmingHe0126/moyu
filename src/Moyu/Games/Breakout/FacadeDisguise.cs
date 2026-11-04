using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Games.Breakout.Core;
using Moyu.Games.Common;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Games.Breakout
{
  /// <summary>
  /// What the boss sees: the south elevation of an office tower in a unitised curtain wall, drawn as a presentation
  /// elevation. Panels follow <see cref="TowerFacade"/> (so the brickwork as it stands is the facade pattern): glass
  /// carries a sky reflection with a cloud streak, the neighbour reflected low down and a slight panel-to-panel
  /// variation; clear panels show blinds behind them. A glazed lobby and a stone amenity floor make the podium, a
  /// louvred plant screen the crown. Neighbours, street trees and people, grid bubbles, levels, an overall dimension
  /// and an enlarged typical bay complete the sheet.
  /// </summary>
  public sealed class FacadeDisguise
  {
    static readonly Color Ink = Color.FromArgb(52, 58, 66), Outline = Color.FromArgb(60, 68, 78), Mullion = Color.FromArgb(226, 231, 236),
      Faint = Color.FromArgb(200, 205, 211), Context = Color.FromArgb(240, 242, 245), ContextLine = Color.FromArgb(185, 191, 198),
      Spandrel = Color.FromArgb(70, 85, 102), Fin = Color.FromArgb(200, 207, 214), Stone = Color.FromArgb(218, 211, 200),
      Leaf = Color.FromArgb(160, 190, 150), Beyond = Color.FromArgb(140, 146, 154), Blind = Color.FromArgb(214, 220, 226);

    public readonly TowerFacade Tower;
    readonly BreakoutSession _s;
    readonly double _m, _cx;
    readonly Mesh _fills = new Mesh(), _glass = new Mesh(), _trees = new Mesh();
    readonly List<Line> _mullions = new List<Line>(), _outline = new List<Line>(), _context = new List<Line>(), _faint = new List<Line>(),
      _ink = new List<Line>(), _beyond = new List<Line>(), _fins = new List<Line>();
    readonly List<(Point3d a, Point3d b)> _grid = new List<(Point3d, Point3d)>();
    readonly List<(Point3d at, string text, int size, bool bold, int align)> _labels = new List<(Point3d, string, int, bool, int)>();
    readonly List<(Point3d at, double r)> _bubbles = new List<(Point3d, double)>();
    const double DetailScale = 4.6, DetailX = 66, DetailY = 40;
    const int DetailFloor = 15, DetailColumn = 3;

    public FacadeDisguise(BreakoutSession s, TowerFacade tower)
    {
      _s = s;
      Tower = tower;
      _m = Math.Max(1e-6, s.Size * s.ToMetres);
      _cx = s.Settings.Width / 2 - tower.Width / 2 / _m;
      Build();
    }

    /// <summary>A point on the elevation in metres: x from the tower's west edge, y up from the street.</summary>
    Point3d P(double x, double y, double z = 0) => _s.At(_cx + x / _m, y / _m, z / _m);

    void L(List<Line> to, double x0, double y0, double x1, double y1, double z = 0) => to.Add(new Line(P(x0, y0, z), P(x1, y1, z)));

    void Quad(Mesh m, double x0, double y0, double x1, double y1, double z, Color c00, Color c10, Color c11, Color c01)
    {
      int i = m.Vertices.Count;
      m.Vertices.Add(P(x0, y0, z));
      m.Vertices.Add(P(x1, y0, z));
      m.Vertices.Add(P(x1, y1, z));
      m.Vertices.Add(P(x0, y1, z));
      m.VertexColors.Add(c00);
      m.VertexColors.Add(c10);
      m.VertexColors.Add(c11);
      m.VertexColors.Add(c01);
      m.Faces.AddFace(i, i + 1, i + 2, i + 3);
    }

    void Quad(Mesh m, double x0, double y0, double x1, double y1, double z, Color c) => Quad(m, x0, y0, x1, y1, z, c, c, c, c);

    /// <summary>A label at (x, y) metres: <paramref name="align"/> 0 starts there, 1 centres on it, 2 ends there (vertically centred).</summary>
    void Label(double x, double y, string text, int size = 10, bool bold = false, int align = 0) => _labels.Add((P(x, y), text, size, bold, align));

    static Color Mix(Color a, Color b, double t) => Toon.Mix(a, b, t);

    /// <summary>The sky the glass reflects at (x, y) metres: brighter high up, a soft diagonal streak, the neighbour low on the west.</summary>
    Color Sky(double x, double y)
    {
      double u = x / Tower.Width, v = y / Tower.Top;
      var c = Mix(Color.FromArgb(104, 128, 152), Color.FromArgb(204, 219, 232), Math.Pow(Math.Max(0, v), 0.8));
      double streak = Math.Exp(-Math.Pow((u * 0.6 + v - 0.82) / 0.08, 2));
      c = Mix(c, Color.FromArgb(236, 242, 248), 0.5 * streak);
      double cloud = 0.5 + 0.25 * Math.Sin(u * 7.1 + v * 9.3) + 0.25 * Math.Sin(u * 2.3 - v * 13.7 + 1.3);
      c = Mix(c, Color.White, 0.1 * cloud);
      if (u < 0.42 && v < 0.4) c = Mix(c, Color.FromArgb(84, 98, 114), 0.4 * (1 - v / 0.4) * Math.Min(1, (0.42 - u) / 0.1));
      return c;
    }

    Color PanelTone(PanelKind k, Color sky, double jitter)
    {
      // One glass skin: the kinds differ only a little, so the reflection reads first and the pattern second.
      Color c;
      switch (k)
      {
        case PanelKind.Clear: c = Mix(sky, Color.FromArgb(58, 74, 92), 0.28); break;
        case PanelKind.Fritted: c = Mix(sky, Color.FromArgb(226, 231, 236), 0.3); break;
        case PanelKind.Spandrel: c = Mix(sky, Spandrel, 0.55); break;
        case PanelKind.Fin: c = Fin; break;
        default: c = sky; break;
      }
      return jitter >= 0 ? Mix(c, Color.White, jitter) : Mix(c, Color.Black, -jitter);
    }

    // ---------------------------------------------------------------- building the sheet

    void Build()
    {
      var t = Tower;
      double w = t.Width;
      var rng = new Random(11);

      // Street, neighbours and their window grids.
      L(_ink, -40, 0, w + 40, 0);
      for (double x = -40; x < w + 40; x += 1.6) L(_faint, x, -0.4, x + 1.2, -1.6);
      foreach (var (x0, x1, h, setback) in new[] { (-30.0, -2.0, 41.0, 4.0), (w + 2, w + 24, 53.0, 0.0) })
      {
        Quad(_fills, x0, 0, x1, h, -1, Context);
        if (setback > 0) Quad(_fills, x0 + setback, h, x1 - setback, h + 5, -1, Context);
        foreach (var (a, b, c, d) in new[] { (x0, 0.0, x1, h), (x0 + setback, h, x1 - setback, h + (setback > 0 ? 5 : 0)) })
        {
          if (d <= b) continue;
          L(_context, a, b, c, b, -0.9);
          L(_context, c, b, c, d, -0.9);
          L(_context, c, d, a, d, -0.9);
          L(_context, a, d, a, b, -0.9);
        }
        for (double y = 4.5; y < h - 1; y += 3.6) L(_faint, x0, y, x1, y, -0.9);
        for (double x = x0 + 2.8; x < x1 - 1; x += 2.8) L(_faint, x, 4.5, x, h - 1.5, -0.9);
      }

      // Podium: glazed lobby, stone amenity floor with a ribbon window.
      for (int c = 0; c < TowerFacade.Columns; c += 2)
      {
        double x0 = c * TowerFacade.Module, x1 = x0 + 2 * TowerFacade.Module;
        var lo = Mix(Color.FromArgb(78, 92, 108), Color.FromArgb(150, 132, 104), 0.25);
        var hi = Mix(Sky(x0, TowerFacade.Lobby), Color.FromArgb(70, 84, 100), 0.35);
        Quad(_glass, x0, 0, x1, TowerFacade.Lobby, 0, lo, lo, hi, hi);
        L(_mullions, x0, 0, x0, TowerFacade.Lobby, 0.05);
        L(_mullions, x0, 4.2, x1, 4.2, 0.05);
      }
      double entry0 = w / 2 - 4.5, entry1 = w / 2 + 4.5;
      Quad(_fills, entry0 - 1.5, 4.6, entry1 + 1.5, 5.0, 0.2, Color.FromArgb(90, 98, 108));
      foreach (double x in new[] { w / 2 - 1.5, w / 2 + 1.5 }) L(_outline, x, 0, x, 3.0, 0.1);
      L(_outline, w / 2 - 1.5, 3.0, w / 2 + 1.5, 3.0, 0.1);
      L(_beyond, w / 2, 0, w / 2, 3.0, 0.1);
      Quad(_fills, 0, TowerFacade.Lobby, w, t.TypicalBase, 0, Stone);
      Quad(_glass, 0, TowerFacade.Lobby + 1.4, w, TowerFacade.Lobby + 3.2, 0.02, Mix(Sky(w / 2, 9), Color.FromArgb(70, 84, 100), 0.3));
      for (double x = 0; x <= w + 1e-9; x += 3) L(_mullions, x, TowerFacade.Lobby + 1.4, x, TowerFacade.Lobby + 3.2, 0.05);
      for (double y = TowerFacade.Lobby + 0.7; y < t.TypicalBase; y += 0.7)
        if (y < TowerFacade.Lobby + 1.4 || y > TowerFacade.Lobby + 3.2) L(_faint, 0, y, w, y, 0.03);

      // Typical floors: a spandrel band at each slab and a row of panels above it.
      double vision = TowerFacade.FloorHeight - TowerFacade.SpandrelHeight;
      for (int f = 0; f < TowerFacade.Floors; f++)
      {
        double y0 = t.FloorLevel(f), ys = y0 + TowerFacade.SpandrelHeight, y1 = y0 + TowerFacade.FloorHeight;
        for (int c = 0; c < TowerFacade.Columns; c++)
        {
          double x0 = c * TowerFacade.Module, x1 = x0 + TowerFacade.Module;
          double jitter = (rng.NextDouble() - 0.5) * 0.09;
          var band = Mix(Sky(x0, y0), Spandrel, 0.42);
          Quad(_glass, x0, y0, x1, ys, 0, band);
          var k = t.Panels[f, c];
          Quad(_glass, x0, ys, x1, y1, 0, PanelTone(k, Sky(x0, ys), jitter), PanelTone(k, Sky(x1, ys), jitter), PanelTone(k, Sky(x1, y1), jitter), PanelTone(k, Sky(x0, y1), jitter));
          if (k == PanelKind.Clear && rng.NextDouble() < 0.4)
          {
            // Blinds behind the clear glass, mostly at the same drop, as an office leaves them.
            double drop = 0.22 + rng.NextDouble() * 0.08;
            Quad(_glass, x0 + 0.06, y1 - vision * drop, x1 - 0.06, y1 - 0.06, 0.02, Mix(Sky(x0, y1), Blind, 0.45));
          }
          if (k == PanelKind.Fritted)
            for (int i = 1; i <= 3; i++) L(_faint, x0 + 0.1, ys + i * vision / 4, x1 - 0.1, ys + i * vision / 4, 0.03);
          if (k == PanelKind.Fin) L(_fins, (x0 + x1) / 2, ys, (x0 + x1) / 2, y1, 0.08);
        }
        L(_mullions, 0, y0, w, y0, 0.05);
        L(_mullions, 0, ys, w, ys, 0.05);
      }
      for (int c = 1; c < TowerFacade.Columns; c++)
        L(_mullions, c * TowerFacade.Module, t.TypicalBase, c * TowerFacade.Module, t.Roof, 0.05);

      // Crown: a louvred plant screen and a dark cap, a maintenance arm on the roof.
      Quad(_fills, 0, t.Roof, w, t.Top - 0.8, 0, Color.FromArgb(172, 181, 191), Color.FromArgb(172, 181, 191), Color.FromArgb(198, 205, 213), Color.FromArgb(198, 205, 213));
      for (double y = t.Roof + 0.45; y < t.Top - 0.8; y += 0.45) L(_context, 0, y, w, y, 0.03);
      for (double x = 6; x < w; x += 6) L(_outline, x, t.Roof, x, t.Top - 0.8, 0.04);
      Quad(_fills, -0.3, t.Top - 0.8, w + 0.3, t.Top, 0.05, Color.FromArgb(88, 96, 106));
      L(_beyond, w - 7, t.Top, w - 7, t.Top + 4.5, -0.1);
      L(_beyond, w - 7, t.Top + 4.5, w - 15, t.Top + 4.5, -0.1);
      L(_beyond, w - 15, t.Top + 4.5, w - 15, t.Top + 3.2, -0.1);
      foreach (var (a, b, c, d) in new[] { (0.0, 0.0, w, t.Top) })
      {
        L(_outline, a, b, c, b, 0.1);
        L(_outline, c, b, c, d, 0.1);
        L(_outline, c, d, a, d, 0.1);
        L(_outline, a, d, a, b, 0.1);
      }
      L(_outline, 0, t.Roof, w, t.Roof, 0.1);
      L(_outline, 0, t.TypicalBase, w, t.TypicalBase, 0.1);
      L(_outline, 0, TowerFacade.Lobby, w, TowerFacade.Lobby, 0.1);

      Street(rng);
      Annotate();
      Detail();
    }

    void Street(Random rng)
    {
      double w = Tower.Width;
      foreach (double x in new[] { -6.0, 3.0, 33.0, w + 6, w + 16 })
      {
        double r = 2.3 + rng.NextDouble() * 0.6, cy = 5.2 + rng.NextDouble() * 0.6;
        L(_beyond, x, 0, x, cy - r * 0.6, 0.3);
        int n = 36, i0 = _trees.Vertices.Count;
        _trees.Vertices.Add(P(x, cy, 0.3));
        for (int i = 0; i < n; i++)
        {
          double a = i * 2 * Math.PI / n, wob = 1 + 0.07 * Math.Sin(a * 6 + x);
          _trees.Vertices.Add(P(x + Math.Cos(a) * r * wob, cy + Math.Sin(a) * r * wob * 0.9, 0.3));
        }
        for (int i = 0; i < n; i++)
        {
          _trees.Faces.AddFace(i0, i0 + 1 + i, i0 + 1 + (i + 1) % n);
          _beyond.Add(new Line(_trees.Vertices[i0 + 1 + i], _trees.Vertices[i0 + 1 + (i + 1) % n]));
        }
      }
      _trees.VertexColors.CreateMonotoneMesh(Leaf);
      foreach (double x in new[] { -12.0, 1.5, 9.0, 21.0, 24.5, w + 10 })
      {
        // A simple standing figure, 1.75 m.
        L(_beyond, x - 0.25, 0, x - 0.15, 0.9, 0.35);
        L(_beyond, x + 0.25, 0, x + 0.15, 0.9, 0.35);
        L(_beyond, x - 0.25, 0.9, x + 0.25, 0.9, 0.35);
        L(_beyond, x - 0.25, 0.9, x - 0.25, 1.5, 0.35);
        L(_beyond, x + 0.25, 0.9, x + 0.25, 1.5, 0.35);
        L(_beyond, x - 0.25, 1.5, x + 0.25, 1.5, 0.35);
        for (int i = 0; i < 10; i++)
        {
          double a0 = i * Math.PI / 5, a1 = (i + 1) * Math.PI / 5;
          L(_beyond, x + 0.14 * Math.Cos(a0), 1.65 + 0.14 * Math.Sin(a0), x + 0.14 * Math.Cos(a1), 1.65 + 0.14 * Math.Sin(a1), 0.35);
        }
      }
    }

    void Annotate()
    {
      var t = Tower;
      double w = t.Width, gridTop = t.Top + 7;
      // Structural grid A–E every 9 m with bubbles above the crown.
      for (int i = 0; i * TowerFacade.GridEvery <= TowerFacade.Columns; i++)
      {
        double x = i * TowerFacade.GridEvery * TowerFacade.Module;
        _grid.Add((P(x, -0.5, 0.2), P(x, gridTop - 1.6, 0.2)));
        _bubbles.Add((P(x, gridTop, 0.2), 1.6));
        Label(x, gridTop, ((char)('A' + i)).ToString(), 11, true, 1);
      }
      // Levels on the east side.
      double lx = w + 1.2;
      var levels = new List<(double y, string name)> { (0, "L01"), (TowerFacade.Lobby, "L02"), (t.TypicalBase, "L03") };
      for (int f = 4; f < TowerFacade.Floors; f += 4) levels.Add((t.FloorLevel(f), $"L{f + 3:00}"));
      levels.Add((t.Roof, "ROOF"));
      levels.Add((t.Top, "T.O. CROWN"));
      foreach (var (y, name) in levels)
      {
        L(_ink, lx, y, lx + 3.2, y, 0.2);
        L(_ink, lx + 0.4, y, lx + 0.9, y + 0.9, 0.2);
        L(_ink, lx + 0.9, y + 0.9, lx - 0.1, y + 0.9, 0.2);
        L(_ink, lx - 0.1, y + 0.9, lx + 0.4, y, 0.2);
        Label(lx + 3.6, y, $"{name}  {(y == 0 ? "±" : "+")}{y:F3}", 10);
      }
      // Overall height on the west side.
      double dx = -4.0;
      L(_ink, dx, 0, dx, t.Top, 0.2);
      foreach (double y in new[] { 0.0, t.Top }) L(_ink, dx - 0.6, y - 0.6, dx + 0.6, y + 0.6, 0.2);
      Label(dx - 1.2, t.Top / 2, $"{t.Top * 1000:N0}".Replace(",", " "), 10, true, 2);
      // Call-out for the typical bay detail.
      double cx = (DetailColumn + 1.5) * TowerFacade.Module, cy = t.FloorLevel(DetailFloor) + TowerFacade.FloorHeight;
      _bubbles.Add((P(cx, cy, 0.2), 5.5));
      L(_ink, cx + 5.5, cy + 1.2, DetailX - 2, DetailY + 2 * TowerFacade.FloorHeight * DetailScale, 0.2);
    }

    /// <summary>Three bays of the curtain wall over two floors, enlarged, with its parts labelled.</summary>
    void Detail()
    {
      var t = Tower;
      double k = DetailScale, mod = TowerFacade.Module * k, fh = TowerFacade.FloorHeight * k, sp = TowerFacade.SpandrelHeight * k;
      double x0 = DetailX, y0 = DetailY;
      for (int f = 0; f < 2; f++)
        for (int c = 0; c < 3; c++)
        {
          double xa = x0 + c * mod, ya = y0 + f * fh;
          var kind = t.Panels[DetailFloor + f, DetailColumn + c];
          var sky = Sky((DetailColumn + c) * TowerFacade.Module, t.FloorLevel(DetailFloor + f));
          Quad(_glass, xa, ya, xa + mod, ya + sp, 0, Mix(Spandrel, sky, 0.1));
          Quad(_glass, xa, ya + sp, xa + mod, ya + fh, 0, PanelTone(kind, sky, 0.02), PanelTone(kind, sky, 0.02), PanelTone(kind, Mix(sky, Color.White, 0.2), 0.02), PanelTone(kind, Mix(sky, Color.White, 0.2), 0.02));
          if (kind == PanelKind.Fritted)
            for (double yy = ya + sp + 0.5; yy < ya + fh - 0.3; yy += 0.5) L(_faint, xa + 0.4, yy, xa + mod - 0.4, yy, 0.03);
        }
      // Mullions and transoms as bars with an inner sight line.
      double bar = 0.065 * k * 1.6;
      for (int c = 0; c <= 3; c++)
      {
        double x = x0 + c * mod;
        Quad(_fills, x - bar / 2, y0, x + bar / 2, y0 + 2 * fh, 0.1, Mullion);
        L(_outline, x - bar / 2, y0, x - bar / 2, y0 + 2 * fh, 0.12);
        L(_outline, x + bar / 2, y0, x + bar / 2, y0 + 2 * fh, 0.12);
      }
      for (int f = 0; f <= 2; f++)
        foreach (double y in f < 2 ? new[] { y0 + f * fh, y0 + f * fh + sp } : new[] { y0 + f * fh })
        {
          Quad(_fills, x0, y - bar / 2, x0 + 3 * mod, y + bar / 2, 0.11, Mullion);
          L(_outline, x0, y - bar / 2, x0 + 3 * mod, y - bar / 2, 0.12);
          L(_outline, x0, y + bar / 2, x0 + 3 * mod, y + bar / 2, 0.12);
        }
      // Slab edges behind the spandrels, dashed.
      for (int f = 0; f < 2; f++) _grid.Add((P(x0, y0 + f * fh + 0.3 * k, 0.13), P(x0 + 3 * mod, y0 + f * fh + 0.3 * k, 0.13)));

      double lx = x0 + 3 * mod + 4;
      void Leader(double px, double py, double ly, string text)
      {
        L(_ink, px, py, lx - 0.6, ly, 0.2);
        L(_ink, lx - 0.6, ly, lx, ly, 0.2);
        Label(lx + 0.6, ly, text, 10);
      }
      Leader(x0 + 3 * mod - bar, y0 + fh + sp + 3, y0 + 2 * fh - 1, "Mullion  65 × 200 mm, anodised aluminium");
      // Name each kind of panel in the bay once.
      var named = new HashSet<PanelKind>();
      double ly = y0 + 2 * fh - 6;
      for (int f = 1; f >= 0; f--)
        for (int c = 2; c >= 0; c--)
        {
          var kind = t.Panels[DetailFloor + f, DetailColumn + c];
          if (!named.Add(kind) || named.Count > 3) continue;
          Leader(x0 + (c + 0.5) * mod, y0 + f * fh + sp + 5, ly, KindText(kind));
          ly -= 5;
        }
      Leader(x0 + 3 * mod - bar, y0 + fh + sp, ly, "Transom  65 × 150 mm, thermally broken");
      Leader(x0 + 2.6 * mod, y0 + fh + sp / 2, y0 + fh - 4, "Spandrel  insulated shadow box, opaque");
      Leader(x0 + 2.8 * mod, y0 + 0.3 * k, y0 + 3, "Slab edge  fire stop + acoustic seal");
      Label(x0, y0 - 3, "1   TYPICAL BAY", 11, true);
      Label(x0 + 3 * mod, y0 - 3, "1:50", 10, false, 2);
    }

    static string KindText(PanelKind k) =>
      k == PanelKind.Clear ? "Vision  DGU 8/16/8 low-e, clear" : k == PanelKind.Vision ? "Vision  DGU 8/16/8 low-e, tinted" :
      k == PanelKind.Fritted ? "Fritted glass  40 % ceramic frit" : k == PanelKind.Spandrel ? "Spandrel panel  opaque, back-painted" : "Aluminium panel  with 300 mm fin";

    // ---------------------------------------------------------------- drawing

    public BoundingBox Bounds => new BoundingBox(new[] { P(-42, -6, -2), P(DetailX + 70, Tower.Top + 12, 2) });

    public void Draw(DisplayPipeline d)
    {
      if (_fills.Vertices.Count > 0) d.DrawMeshFalseColors(_fills);
      d.DrawLines(_context, ContextLine, 1);
      d.DrawLines(_faint, Faint, 1);
      d.DrawMeshFalseColors(_glass);
      d.DrawLines(_mullions, Mullion, 1);
      d.DrawLines(_fins, Color.FromArgb(240, 243, 246), 2);
      d.DrawLines(_outline, Outline, 1);
      foreach (var (a, b) in _grid) d.DrawDottedLine(a, b, Faint);
      d.DrawMeshFalseColors(_trees);
      d.DrawLines(_beyond, Beyond, 1);
      d.DrawLines(_ink, Ink, 1);
      foreach (var (at, r) in _bubbles)
        d.DrawCircle(new Circle(new Plane(at, _s.Plane.XAxis, _s.Plane.YAxis), r / _m * _s.Size), Ink, 1);
    }

    /// <summary>Labels, the metrics card and the title block, in screen space.</summary>
    public void DrawOverlay(DisplayPipeline d, RhinoViewport vp, double bossTime, string pattern)
    {
      // Pixels per HUD unit: the sheet's text and cards grow with the display's DPI scale.
      double k = GameConduit.UiScale(d.DpiScale, d.FrameSize);
      int Px(double height) => (int)Math.Round(height * k);
      foreach (var (at, text, size, bold, align) in _labels)
      {
        var p = vp.WorldToClient(at);
        var font = bold ? "Segoe UI Semibold" : "Segoe UI";
        int px = Px(size);
        if (align == 1)
        {
          d.Draw2dText(text, Ink, new Point2d(p.X, p.Y), true, px, font);
          continue;
        }
        double width = align == 2 ? d.Measure2dText(text, new Point2d(0, 0), false, 0, px, font).Width : 0;
        d.Draw2dText(text, Ink, new Point2d(p.X - width, p.Y - px * 0.7), false, px, font);
      }
      var t = Tower;
      int iter = (37 + (int)(bossTime / 1.2)) % 120 + 1;
      var rows = new[]
      {
        ("Storeys", $"{t.Storeys}  ({t.Top:F1} m)"), ("Curtain-wall panels", $"{t.PanelCount:N0}"), ("Window-to-wall", $"{t.Wwr * 100:F0} %"),
        ("Vision glass", $"{t.VisionArea:N0} m²"), ("Fritted / spandrel / fin", $"{t.FrittedPanels} / {t.OpaquePanels} / {t.FinPanels}"),
        ("Facade U-value", $"{t.UValue:F2} W/m²K"), ("Solar factor g", $"{t.GValue:F2}"), ("Embodied carbon", $"{t.CarbonTonnes:N0} t CO₂e"),
      };
      var ink = Color.FromArgb(52, 58, 64);
      var grey = Color.FromArgb(120, 126, 134);
      int x = Px(24), y = Px(24), w = Px(330), pad = Px(16), pitch = Px(22);
      int h = Px(112) + rows.Length * pitch;
      d.Draw2dRectangle(new Rectangle(x, y, w, h), Color.FromArgb(206, 212, 218), 1, Color.FromArgb(240, 255, 255, 255));
      d.Draw2dText("Facade Study  ·  Elevation B", ink, new Point2d(x + pad, y + Px(14)), false, Px(15), "Segoe UI Semibold");
      d.Draw2dText($"Unitised curtain wall, 1.5 m module · pattern from {pattern}", grey, new Point2d(x + pad, y + Px(38)), false, Px(11), "Segoe UI");
      for (int r = 0; r < rows.Length; r++)
      {
        d.Draw2dText(rows[r].Item1, grey, new Point2d(x + pad, y + Px(64) + r * pitch), false, Px(13), "Segoe UI");
        d.Draw2dText(rows[r].Item2, ink, new Point2d(x + Px(196), y + Px(64) + r * pitch), false, Px(13), "Segoe UI Semibold");
      }
      int py = y + Px(64) + rows.Length * pitch + Px(10);
      d.Draw2dText($"Optimizing frit pattern for glare…  iter {iter} / 120", grey, new Point2d(x + pad, py), false, Px(11), "Segoe UI");
      d.Draw2dRectangle(new Rectangle(x + pad, py + Px(20), w - 2 * pad, Px(5)), Color.FromArgb(222, 226, 230), 0, Color.FromArgb(222, 226, 230));
      d.Draw2dRectangle(new Rectangle(x + pad, py + Px(20), (int)((w - 2 * pad) * iter / 120.0), Px(5)), Color.FromArgb(73, 80, 87), 0, Color.FromArgb(73, 80, 87));

      // The title block, bottom right; in a view too small for both it stays inside the frame and clear of the card.
      var frame = d.FrameSize;
      double tx = Math.Max(frame.Width - 380 * k, x), ty = Math.Max(frame.Height - 70 * k, y);
      if (ty < y + h + Px(12)) tx = Math.Max(tx, x + w + pad);
      d.Draw2dText("ELEVATION B  —  SOUTH", ink, new Point2d(tx, ty), false, Px(18), "Segoe UI Semibold");
      d.Draw2dText("1:500", ink, new Point2d(tx + 250 * k, ty + 4 * k), false, Px(13), "Segoe UI");
      d.Draw2dLine(new PointF((float)tx, (float)(ty + 28 * k)), new PointF((float)(tx + 330 * k), (float)(ty + 28 * k)), ink, 1);
      d.Draw2dText("Office tower, 24 storeys  ·  curtain wall and podium", grey, new Point2d(tx, ty + 36 * k), false, Px(11), "Segoe UI");
    }

    /// <summary>Straight on to the elevation with a long lens, the sheet set right of the metrics card.</summary>
    public void CameraFor(out Point3d location, out Point3d target)
    {
      target = P(Tower.Width / 2 + 12, Tower.Top / 2 + 3);
      location = target + _s.Plane.ZAxis * (560 / _m * _s.Size);
    }
  }
}
