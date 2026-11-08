using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Games.Common;
using Moyu.Games.Stack.Core;
using Rhino.Display;
using Rhino.Geometry;
using Point = System.Drawing.Point;

namespace Moyu.Games.Stack
{
  /// <summary>
  /// Draws VoxStack like a painted model: the well on a plinth, the stack as pastel blocks with one flat tone per
  /// face under a fixed sun, the falling piece with a see-through landing ghost, NEXT / HOLD as small floating
  /// models, chapter palettes that change with the level, and the quiet HUD; or, in boss mode, the massing study.
  /// </summary>
  sealed class StackConduit : GameConduit
  {
    const double Inset = 0.035, BlendSeconds = 1.6;

    /// <summary>Stack colours by storey: a soft spectrum that sits well on every chapter's sky.</summary>
    static readonly Color[] LayerColours =
    {
      Palette.Hex(0xF2938C), Palette.Hex(0xF5B27C), Palette.Hex(0xF0D277), Palette.Hex(0x8FD3A3),
      Palette.Hex(0x7EC7D6), Palette.Hex(0x8FA2E8), Palette.Hex(0xB794E0), Palette.Hex(0xEC9BC3),
    };

    static readonly (Palette palette, Color plinth)[] Chapters =
    {
      (new Palette(Palette.Hex(0xF8F0E7), Palette.Hex(0xEAD6CE), Palette.Hex(0x43405A), Palette.Hex(0x998F9E), Palette.Hex(0xE36F5A), Palette.Hex(0x5C5A8C)), Palette.Hex(0xEFE2D2)),
      (new Palette(Palette.Hex(0xEDF6F1), Palette.Hex(0xCAE3DA), Palette.Hex(0x2F4858), Palette.Hex(0x7C9A93), Palette.Hex(0xF08A62), Palette.Hex(0x46668A)), Palette.Hex(0xDCEBE3)),
      (new Palette(Palette.Hex(0xF0ECF8), Palette.Hex(0xD1C9EA), Palette.Hex(0x3B3558), Palette.Hex(0x8A82A6), Palette.Hex(0xEDA546), Palette.Hex(0x554C8A)), Palette.Hex(0xE3DDF1)),
      (new Palette(Palette.Hex(0xFCEADB), Palette.Hex(0xEFBE9F), Palette.Hex(0x4A2F35), Palette.Hex(0xA27565), Palette.Hex(0x3E7CD6), Palette.Hex(0x74496E)), Palette.Hex(0xF3D9C5)),
      (new Palette(Palette.Hex(0xDCE5F1), Palette.Hex(0x9AAFCB), Palette.Hex(0x20304A), Palette.Hex(0x5D7591), Palette.Hex(0xFFB24A), Palette.Hex(0x2E3E66)), Palette.Hex(0xCCD8E6)),
    };

    static readonly string[] ChapterNames = { "亭子 Pavilion", "多层 Low-rise", "小高层 Mid-rise", "高层 High-rise", "超高层 Supertall" };

    readonly StackSession _s;
    Mesh _locked, _plinth;
    Line[] _lockedEdges = Array.Empty<Line>();
    (StackGame game, int version, bool clearing, Palette palette) _lockedKey;
    int _band = -1, _fromBand;
    double _blendStart = double.NegativeInfinity;

    public StackConduit(StackSession session) : base(session) => _s = session;

    public override void Release()
    {
      _locked?.Dispose();
      _plinth?.Dispose();
      _locked = _plinth = null;
      base.Release();
    }

    // ---------------------------------------------------------------- palette

    double Blend => Math.Min(1, (_s.Time - _blendStart) / BlendSeconds);

    (Palette palette, Color plinth) Current
    {
      get
      {
        int band = Palette.Band(_s.Game.Level);
        if (_band < 0) _band = _fromBand = band;
        else if (band != _band)
        {
          _fromBand = _band;
          _band = band;
          _blendStart = _s.Time;
        }
        double t = Blend;
        if (t >= 1) return Chapters[_band];
        var (pa, ca) = Chapters[_fromBand];
        var (pb, cb) = Chapters[_band];
        return (Palette.Lerp(pa, pb, t), Lerp(ca, cb, t));
      }
    }

    protected override Palette Theme => Current.palette;

    protected override (string keys, string meaning)[] KeyHints => new[]
    {
      ("← ↑ ↓ →", T("移动", "move")), (T("拖圆环", "Gumball"), T("旋转", "drag a ring to turn")), ("Q E", T("水平转", "spin")),
      ("W S A D", T("翻转", "tip")), ("Space", T("落下", "drop")), ("Shift", T("加速", "faster")), ("C", T("暂存", "hold")),
      ("Z X", T("转视角", "turn view")), ("Esc", T("暂停", "pause")), ("Home", T("复位视角", "reset view")), BossHint,
    };

    protected override void IncludeBounds(CalculateBoundingBoxEventArgs e)
    {
      var st = _s.Settings;
      // The well's box in grid units, then into the world, so a turned plane still gets the whole well inside the clipping planes.
      var box = new BoundingBox(-1, -1, -1, st.Nx + 1, st.Ny + 1, st.Nz + 2);
      box.Transform(_s.GridToWorld);
      e.IncludeBoundingBox(box);
      if (_s.Boss && _s.Disguise != null) e.IncludeBoundingBox(_s.Disguise.WorldBounds);
    }

    protected override void DrawBoss(DrawEventArgs e) => _s.Disguise?.Draw(e.Display);

    static Color Pastel(Color c) => Lerp(c, Color.White, 0.12);

    // ---------------------------------------------------------------- 3D

    static IEnumerable<Line> CubeEdges(double x, double y, double z, double inset) =>
      new BoundingBox(x + inset, y + inset, z + inset, x + 1 - inset, y + 1 - inset, z + 1 - inset).GetEdges();

    /// <summary>
    /// A copy of <paramref name="m"/> (quads with one colour per corner) with its faces in far-to-near order from
    /// <paramref name="eye"/>: faces draw in index order, so one draw call with depth testing off still layers right.
    /// </summary>
    static Mesh BackToFront(Mesh m, Point3d eye)
    {
      var sorted = new Mesh();
      foreach (int f in Enumerable.Range(0, m.Faces.Count).OrderByDescending(f => m.Faces.GetFaceCenter(f).DistanceTo(eye)))
      {
        var face = m.Faces[f];
        int i = sorted.Vertices.Count;
        for (int k = 0; k < 4; k++)
        {
          sorted.Vertices.Add(m.Vertices[face[k]]);
          sorted.VertexColors.Add(m.VertexColors[face[k]]);
        }
        sorted.Faces.AddFace(i, i + 1, i + 2, i + 3);
      }
      return sorted;
    }

    protected override void DrawGame(DrawEventArgs e)
    {
      var d = e.Display;
      var g = _s.Game;
      var (palette, plinth) = Current;
      _s.GridToWorld.TryGetInverse(out var toGrid);
      var eye = e.Viewport.CameraLocation;
      eye.Transform(toGrid);
      d.PushModelTransform(_s.GridToWorld);
      try
      {
        DrawWell(d, g.Well, eye, palette, plinth);
        RebuildLocked(g, palette);
        d.DrawMeshFalseColors(_locked);
        d.DrawLines(_lockedEdges, Fade(palette.Shadow, 0.28), 1);
        if (g.ClearingLayers.Count > 0) DrawClearing(d, g, palette);
        if (g.Current != null)
        {
          DrawPiece(d, g, palette);
          DrawGumball(d, e.Viewport, g, eye, palette);
        }
      }
      finally
      {
        d.PopModelTransform();
      }
      DrawPreviews(e, palette);
    }

    /// <summary>The well stands on a plinth like a model on its base; only the two far walls show their grid.</summary>
    void DrawWell(DisplayPipeline d, Well w, Point3d eye, Palette p, Color plinth)
    {
      // Rebuilt on every frame of a chapter blend so its colour fades; the old mesh is freed now, not by the finalizer.
      if (_plinth == null || Blend < 1 || _lockedKey.palette.Shadow != p.Shadow)
      {
        _plinth?.Dispose();
        _plinth = Toon.Cube(-0.45, -0.45, -0.6, w.Nx + 0.45, w.Ny + 0.45, 0, plinth, p.Shadow);
      }
      d.DrawMeshFalseColors(_plinth);
      var floor = new List<Line>();
      for (int i = 0; i <= w.Nx; i++) floor.Add(new Line(i, 0, 0.003, i, w.Ny, 0.003));
      for (int j = 0; j <= w.Ny; j++) floor.Add(new Line(0, j, 0.003, w.Nx, j, 0.003));
      d.DrawLines(floor, Fade(p.Shadow, 0.22), 1);

      double wx = eye.X > w.Nx / 2.0 ? 0 : w.Nx, wy = eye.Y > w.Ny / 2.0 ? 0 : w.Ny;
      var walls = new List<Line>();
      for (int k = 1; k <= w.Nz; k++)
      {
        walls.Add(new Line(wx, 0, k, wx, w.Ny, k));
        walls.Add(new Line(0, wy, k, w.Nx, wy, k));
      }
      for (int j = 0; j <= w.Ny; j++) walls.Add(new Line(wx, j, 0, wx, j, w.Nz));
      for (int i = 0; i <= w.Nx; i++) walls.Add(new Line(i, wy, 0, i, wy, w.Nz));
      d.DrawLines(walls, Fade(p.Shadow, 0.055), 1);

      var corners = new[] { new Point3d(0, 0, 0), new Point3d(w.Nx, 0, 0), new Point3d(w.Nx, w.Ny, 0), new Point3d(0, w.Ny, 0) };
      var edges = new List<Line>();
      for (int c = 0; c < 4; c++)
      {
        var a = corners[c];
        var b = corners[(c + 1) % 4];
        edges.Add(new Line(a.X, a.Y, w.Nz, b.X, b.Y, w.Nz));
        edges.Add(new Line(a.X, a.Y, 0, a.X, a.Y, w.Nz));
      }
      d.DrawLines(edges, Fade(p.Ink, 0.16), 1);
    }

    void RebuildLocked(StackGame g, Palette p)
    {
      bool clearing = g.ClearingLayers.Count > 0;
      if (_locked != null && ReferenceEquals(_lockedKey.game, g) && _lockedKey.version == g.Well.Version && _lockedKey.clearing == clearing && Blend >= 1 &&
        _lockedKey.palette.Shadow == p.Shadow) return;
      _lockedKey = (g, g.Well.Version, clearing, p);
      var skip = new HashSet<int>(g.ClearingLayers);
      // Once per lock and every blend frame: free the old stack mesh rather than leave it to the finalizer.
      _locked?.Dispose();
      _locked = new Mesh();
      var edges = new List<Line>();
      foreach (var (c, _) in g.Well.Cells())
      {
        if (skip.Contains(c.Z)) continue;
        Toon.AddCube(_locked, c.X + Inset, c.Y + Inset, c.Z + Inset, c.X + 1 - Inset, c.Y + 1 - Inset, c.Z + 1 - Inset, LayerColours[c.Z % LayerColours.Length], p.Shadow);
        edges.AddRange(CubeEdges(c.X, c.Y, c.Z, Inset));
      }
      _lockedEdges = edges.ToArray();
    }

    void DrawClearing(DisplayPipeline d, StackGame g, Palette p)
    {
      double t = Math.Min(1, g.ClearProgress);
      var w = g.Well;
      using (var glow = new Mesh())
      {
        foreach (int z in g.ClearingLayers)
          for (int y = 0; y < w.Ny; y++)
            for (int x = 0; x < w.Nx; x++)
            {
              double inset = 0.04 + 0.44 * t;
              Toon.AddCube(glow, x + inset, y + inset, z + inset, x + 1 - inset, y + 1 - inset, z + 1 - inset, Lerp(LayerColours[z % LayerColours.Length], Color.White, 0.7), p.Shadow);
            }
        d.DrawMeshFalseColors(glow);
      }
      var rng = new Random(g.PiecesPlaced * 31 + 7);
      double reach = Math.Max(w.Nx, w.Ny) * 0.5;
      foreach (int z in g.ClearingLayers)
        for (int i = 0; i < 26; i++)
        {
          double a = rng.NextDouble() * Math.PI * 2, r = reach * (0.7 + 0.6 * rng.NextDouble()) * (0.6 + 1.2 * t);
          double h = z + 0.5 + (rng.NextDouble() - 0.3) * 1.4 * t;
          Ball(d, new Point3d(w.Nx / 2.0 + Math.Cos(a) * r, w.Ny / 2.0 + Math.Sin(a) * r, h), 0.09 * (1 - t), i % 2 == 0 ? p.Accent : LayerColours[i % LayerColours.Length]);
        }
    }

    void DrawPiece(DisplayPipeline d, StackGame g, Palette p)
    {
      var colour = Pastel(Color.FromArgb(g.Current.Colour));
      var cells = g.PieceCells.ToList();
      int drop = g.DropDistance();
      if (drop > 0)
      {
        var ghostEdges = new List<Line>();
        using (var ghost = new Mesh())
        {
          foreach (var c in cells)
          {
            Toon.AddCube(ghost, c.X + Inset, c.Y + Inset, c.Z - drop + Inset, c.X + 1 - Inset, c.Y + 1 - Inset, c.Z - drop + 1 - Inset, colour, p.Shadow);
            ghostEdges.AddRange(CubeEdges(c.X, c.Y, c.Z - drop, Inset));
          }
          d.DrawMeshShaded(ghost, Flat(colour, 0.7));
        }
        d.DrawLines(ghostEdges, Fade(Lerp(colour, p.Shadow, 0.4), 0.8), 1);
        foreach (var low in cells.GroupBy(c => (c.X, c.Y)).Select(col => col.OrderBy(c => c.Z).First()))
          d.DrawDottedLine(new Point3d(low.X + 0.5, low.Y + 0.5, low.Z), new Point3d(low.X + 0.5, low.Y + 0.5, low.Z - drop + 1), Fade(Lerp(colour, p.Shadow, 0.3), 0.6));
      }
      var edges = new List<Line>();
      using (var mesh = new Mesh())
      {
        foreach (var c in cells)
        {
          Toon.AddCube(mesh, c.X + Inset, c.Y + Inset, c.Z + Inset, c.X + 1 - Inset, c.Y + 1 - Inset, c.Z + 1 - Inset, colour, p.Shadow);
          edges.AddRange(CubeEdges(c.X, c.Y, c.Z, Inset));
        }
        d.DrawMeshFalseColors(mesh);
      }
      d.DrawLines(edges, Fade(Lerp(colour, p.Shadow, 0.5), 0.5), 1);
    }

    /// <summary>
    /// The gumball pinned in the bottom-right corner (grid units, inside the grid transform): a small copy of the piece
    /// in the middle and three rings round it, drawn over the scene (depth testing off, back halves of the rings first,
    /// then the piece, then the front halves). The ring under the mouse thickens, the one being dragged shows how far the
    /// drag has gone, and a ring glows for a moment after it turns the piece.
    /// </summary>
    void DrawGumball(DisplayPipeline d, RhinoViewport vp, StackGame g, Point3d eye, Palette p)
    {
      var gb = _s.Gumball;
      var (c, r) = gb.Place(vp, d.FrameSize);
      // Built upright, then turned by the tilt (so the front and back halves are judged from the eye as the tilt sees it).
      var tilt = gb.Tilt(vp, c);
      tilt.TryGetInverse(out var untilt);
      var eyeUpright = eye;
      eyeUpright.Transform(untilt);
      var rings = Enumerable.Range(0, 3).Select(a => Gumball.Halves(c, a, r, eyeUpright)).ToArray();
      foreach (var (front, back) in rings)
      {
        front.Transform(tilt);
        back?.Transform(tilt);
      }
      Color RingColour(int a, double alpha) => Fade(Lerp(Gumball.Colours[a], Color.White, 0.08), alpha);
      int Width(int a)
      {
        double glow = Math.Max(0, 1 - (_s.Time - gb.LastTurnTime) / 0.35);
        int w = gb.Hot == a || gb.Dragging == a ? 5 : 3;
        return gb.LastAxis == a && glow > 0 ? w + (int)Math.Round(3 * glow) : w;
      }
      d.EnableDepthTesting(false);
      try
      {
        for (int a = 0; a < 3; a++)
          if (rings[a].back != null) d.DrawPolyline(rings[a].back, RingColour(a, gb.Hot == a || gb.Dragging == a ? 0.7 : 0.4), Math.Max(2, Width(a) - 1));
        // The piece, small, centred in the gumball; faces back to front since nothing hides them.
        var cells = g.Offsets;
        double sx = cells.Average(o => o.X) + 0.5, sy = cells.Average(o => o.Y) + 0.5, sz = cells.Average(o => o.Z) + 0.5;
        double k = r * 0.2, gap = k * 0.05;
        var colour = Pastel(Color.FromArgb(g.Current.Colour));
        using (var mini = new Mesh())
        {
          foreach (var o in cells)
          {
            double x0 = c.X + (o.X - sx) * k, y0 = c.Y + (o.Y - sy) * k, z0 = c.Z + (o.Z - sz) * k;
            Toon.AddCube(mini, x0 + gap, y0 + gap, z0 + gap, x0 + k - gap, y0 + k - gap, z0 + k - gap, colour, p.Shadow);
          }
          mini.Transform(tilt);
          using (var sorted = BackToFront(mini, eye)) d.DrawMeshFalseColors(sorted);
        }
        for (int a = 0; a < 3; a++)
          d.DrawPolyline(rings[a].front, RingColour(a, gb.Hot == a || gb.Dragging == a ? 1 : 0.88), Width(a));
        int dragged = gb.Dragging;
        if (dragged >= 0 && Math.Abs(gb.DragAngle) > 0.02)
        {
          // How far the drag has gone towards the next quarter turn.
          double sweep = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, gb.DragAngle));
          var arc = Gumball.Ring(c, dragged, r * 1.12, gb.GrabAngle, sweep);
          arc.Transform(tilt);
          d.DrawPolyline(arc, RingColour(dragged, 0.9), 4);
        }
      }
      finally
      {
        d.EnableDepthTesting(true);
      }
    }

    /// <summary>
    /// Screen anchors of the previews (<paramref name="k"/> pixels per HUD unit): NEXT down the right edge under the
    /// chapter, as many of the three as stop short of the gumball's labels in a short frame; HOLD on the left, lifted
    /// clear of the key hints when the frame is low.
    /// </summary>
    static IEnumerable<(Point client, int pixels, int slot)> PreviewSlots(RhinoViewport vp, Size frame, double k)
    {
      var (at, r) = Gumball.Anchor(vp, frame);
      double top = at.Y - r * 1.28 - 16 * k;
      int x = frame.Width - (int)Math.Round(120 * k);
      var next = new[] { (y: 215, pixels: 24), (y: 305, pixels: 18), (y: 380, pixels: 15) };
      for (int i = 0; i < next.Length; i++)
      {
        int pixels = (int)Math.Round(next[i].pixels * k);
        double y = next[i].y * k;
        // A piece reaches at most two voxels from its middle.
        if (y + 2.4 * pixels > top) break;
        yield return (new Point(x, (int)Math.Round(y)), pixels, i);
      }
      double hold = Math.Max(360 * k, Math.Min(430 * k, frame.Height - 95 * k));
      yield return (new Point((int)Math.Round(140 * k), (int)Math.Round(hold)), (int)Math.Round(20 * k), -1);
    }

    void DrawPreviews(DrawEventArgs e, Palette p)
    {
      var g = _s.Game;
      if (g.IsOver) return;
      var d = e.Display;
      foreach (var (client, pixels, slot) in PreviewSlots(e.Viewport, d.FrameSize, Ui(d)))
      {
        var def = slot < 0 ? g.Held : g.Upcoming(slot);
        if (def != null) DrawMini(e, def, client, pixels, slot < 0 && g.HoldUsed, p);
      }
    }

    /// <summary>A small model of a piece floating at a screen position, turned the same way as the well.</summary>
    void DrawMini(DrawEventArgs e, PieceDef def, Point client, int pixels, bool faded, Palette p)
    {
      var vp = e.Viewport;
      var ray = vp.ClientToWorld(client);
      // The ray runs far → near; aim from the camera towards whichever end is farther.
      var far = ray.From.DistanceTo(vp.CameraLocation) > ray.To.DistanceTo(vp.CameraLocation) ? ray.From : ray.To;
      var dir = far - vp.CameraLocation;
      if (!dir.Unitize()) return;
      double dist = vp.CameraLocation.DistanceTo(vp.CameraTarget) * 0.6;
      var centre = vp.CameraLocation + dir * dist;
      if (!vp.GetWorldToScreenScale(centre, out double ppu) || ppu <= 0) return;
      double u = pixels / ppu;
      var plane = _s.Plane;
      var size = def.Size;
      var origin = centre - (plane.XAxis * size.X + plane.YAxis * size.Y + plane.ZAxis * size.Z) * (u / 2);
      var colour = faded ? Lerp(p.Muted, Color.White, 0.4) : Pastel(Color.FromArgb(def.Colour));
      var d = e.Display;
      var xf = Transform.Translation(origin - Point3d.Origin) * Transform.PlaneToPlane(Plane.WorldXY, new Plane(Point3d.Origin, plane.XAxis, plane.YAxis)) * Transform.Scale(Point3d.Origin, u);
      xf.TryGetInverse(out var toMini);
      var eye = vp.CameraLocation;
      eye.Transform(toMini);
      d.PushModelTransform(xf);
      d.EnableDepthTesting(false);
      try
      {
        // Back to front, since depth testing is off so the mini always shows over the scene.
        using (var mesh = new Mesh())
        {
          foreach (var c in def.Cells)
            Toon.AddCube(mesh, c.X + Inset, c.Y + Inset, c.Z + Inset, c.X + 1 - Inset, c.Y + 1 - Inset, c.Z + 1 - Inset, colour, p.Shadow);
          using (var sorted = BackToFront(mesh, eye)) d.DrawMeshFalseColors(sorted);
        }
      }
      finally
      {
        d.EnableDepthTesting(true);
        d.PopModelTransform();
      }
    }

    // ---------------------------------------------------------------- HUD

    /// <summary>Screen rectangle covered by the well.</summary>
    RectangleF WellOnScreen(RhinoViewport vp)
    {
      var st = _s.Settings;
      float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
      foreach (var x in new[] { 0, st.Nx })
        foreach (var y in new[] { 0, st.Ny })
          foreach (var z in new[] { 0, st.Nz })
          {
            var p = vp.WorldToClient(_s.WellPoint(x, y, z));
            minX = Math.Min(minX, (float)p.X); maxX = Math.Max(maxX, (float)p.X);
            minY = Math.Min(minY, (float)p.Y); maxY = Math.Max(maxY, (float)p.Y);
          }
      return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    protected override void HudGame(DrawEventArgs e)
    {
      var d = e.Display;
      var frame = d.FrameSize;
      var g = _s.Game;
      var p = Theme;
      (string, string, Color?) third;
      switch (_s.Settings.Mode)
      {
        case StackMode.Blitz:
          var left = TimeSpan.FromSeconds(Math.Ceiling(g.TimeLeft));
          third = ("TIME", $"{(int)left.TotalMinutes}:{left.Seconds:00}", g.TimeLeft < 15 ? p.Accent : (Color?)null);
          break;
        case StackMode.Zen:
          // PIECES is already the second figure: show the time played instead (like Blitz's clock).
          var played = TimeSpan.FromSeconds(Math.Floor(g.Elapsed));
          third = ("TIME", $"{(int)played.TotalMinutes}:{played.Seconds:00}", null);
          break;
        default: third = ("BEST", Math.Max(_s.Best, g.Score).ToString("N0"), null); break;
      }
      ScoreBlock(d, "VoxStack", g.Score, ("LAYERS", g.Layers.ToString(), null), ("PIECES", g.PiecesPlaced.ToString(), null), third);
      LevelBlock(d, g.Level, ChapterNames[Palette.Band(g.Level)], (g.Layers % StackGame.LayersPerLevel) / (double)StackGame.LayersPerLevel);
      double k = Ui(d);
      double y = 222 * k;
      if (g.Combo >= 2) Status(d, ref y, p.Accent, $"{T("连消", "Combo")} ×{g.Combo}", -1);
      if (g.SoftDrop && !g.IsOver) Status(d, ref y, p.Accent, T("加速下落", "Fast drop"), -1);

      if (!g.IsOver)
        foreach (var (client, _, slot) in PreviewSlots(e.Viewport, frame, k))
        {
          if (slot == 0) Text(d, Spaced("NEXT"), p.Muted, client.X, client.Y - 66 * k, (int)Math.Round(11 * k), UiStrong, true);
          if (slot == -1) Text(d, Spaced("HOLD · C"), p.Muted, client.X, client.Y - 62 * k, (int)Math.Round(11 * k), UiStrong, true);
        }
      if (g.Current != null && !g.IsOver) GumballLabels(e, p);
      Popups(e, g, p);
    }

    /// <summary>Each ring's axis and its keys beside it, and a two-line how-to left of the gumball for the first minute.</summary>
    void GumballLabels(DrawEventArgs e, Palette p)
    {
      var d = e.Display;
      var vp = e.Viewport;
      var gb = _s.Gumball;
      var (c, r) = gb.Place(vp, d.FrameSize);
      var tilt = gb.Tilt(vp, c);
      var keys = gb.KeyLabels();
      string[] names = { "X", "Y", "Z" };
      Point2d Screen(Point3d grid)
      {
        grid.Transform(tilt);
        grid.Transform(_s.GridToWorld);
        return vp.WorldToClient(grid);
      }
      var cs = Screen(c);
      double k = Ui(d);
      for (int a = 0; a < 3; a++)
      {
        // Label each ring at whichever of its points sits furthest out on screen, away from the others.
        var best = Enumerable.Range(0, 16).Select(i => Screen(Gumball.On(c, a, i * Math.PI / 8, r * 1.28)))
          .OrderByDescending(q => (q - cs).Length + (a == 2 ? -(q.Y - cs.Y) * 0.6 : a == 0 ? (q.X - cs.X) * 0.4 : (q.Y - cs.Y) * 0.4)).First();
        Text(d, $"{names[a]}  {keys[a]}", Lerp(Gumball.Colours[a], p.Ink, 0.25), best.X, best.Y, (int)Math.Round(12 * k), UiStrong, true);
      }
      if (_s.PlayTime < 60)
      {
        var (_, pixels) = Gumball.Anchor(vp, d.FrameSize);
        var hint = Fade(p.Muted, Math.Min(1, (60 - _s.PlayTime) / 5));
        double right = cs.X - pixels * 1.45;
        int size = (int)Math.Round(13 * k);
        TextRight(d, T("拖动圆环", "Drag a ring"), hint, right, cs.Y - size * 1.5, size, TextFace);
        TextRight(d, T("绕这根轴转 90°", "to turn 90° about its axis"), hint, right, cs.Y + size * 0.2, size, TextFace);
      }
    }

    protected override (string title, string big, string line, string badge) ResultText
    {
      get
      {
        var g = _s.Game;
        string title = g.State == StackState.TimeUp ? T("时间到", "Time's up") : T("封顶了", "Topped out");
        var played = TimeSpan.FromSeconds(g.Elapsed);
        // Past an hour the clock shows hours too, instead of wrapping.
        string time = played.TotalHours >= 1 ? $"{(int)played.TotalHours}:{played.Minutes:00}:{played.Seconds:00}" : $"{played.Minutes}:{played.Seconds:00}";
        string line = Zh
          ? $"{g.Layers} 层    ·    第 {g.Level} 级    ·    {g.PiecesPlaced} 块    ·    {time}" + (_s.NewBest ? "" : $"    ·    最高 {_s.Best:N0}")
          : $"{g.Layers} layers    ·    level {g.Level}    ·    {g.PiecesPlaced} pieces    ·    {time}" + (_s.NewBest ? "" : $"    ·    best {_s.Best:N0}");
        return (title, $"{g.Score:N0}", line, _s.NewBest ? "NEW BEST" : null);
      }
    }

    void Popups(DrawEventArgs e, StackGame g, Palette p)
    {
      var d = e.Display;
      var frame = d.FrameSize;
      double k = Ui(d);
      var clear = g.LastClear;
      if (clear != null && g.Elapsed - clear.Time < 1.6)
      {
        double t = (g.Elapsed - clear.Time) / 1.6;
        var well = WellOnScreen(e.Viewport);
        double x = well.Right + 36 * k;
        if (x > frame.Width - 320 * k) x = Math.Max(24 * k, well.Left - 320 * k);
        double y = well.Top + well.Height * 0.5 - 30 * k * t;
        string title = clear.Perfect ? "SITE CLEARED" : clear.Layers == 1 ? "FLOOR CLEARED" : clear.Layers == 2 ? "DOUBLE FLOOR" : clear.Layers == 3 ? "TRIPLE FLOOR" : "MEGA FLOOR";
        Text(d, Spaced(title), Fade(p.Accent, 1.4 - t), x, y, (int)Math.Round(14 * k), UiStrong);
        Text(d, $"+{clear.Points:N0}", Fade(p.Ink, 1.4 - t), x - 2 * k, y + 22 * k, (int)Math.Round(44 * k), Display);
        if (clear.Combo > 1) Text(d, $"{T("连消", "Combo")} ×{clear.Combo}", Fade(p.Muted, 1.3 - t), x, y + 76 * k, (int)Math.Round(15 * k), TextFace);
      }
      if (g.Elapsed - g.LevelUpTime < 2.6 && g.Level > 1)
      {
        double t = (g.Elapsed - g.LevelUpTime) / 2.6;
        double a = Math.Min(1, Math.Min(t * 5, (1 - t) * 3));
        double cx = frame.Width / 2.0;
        Text(d, Spaced($"LEVEL {g.Level:00}"), Fade(p.Accent, a), cx, frame.Height * 0.16, (int)Math.Round(14 * k), UiStrong, true);
        Text(d, Named(ChapterNames[Palette.Band(g.Level)]), Fade(p.Ink, a), cx, frame.Height * 0.16 + 36 * k, (int)Math.Round(34 * k), TextFace, true);
      }
      if (g.Elapsed - g.SiteResetTime < 2.2)
      {
        double t = (g.Elapsed - g.SiteResetTime) / 2.2;
        Text(d, T("场地重置", "Site reset"), Fade(p.Ink, 1.3 - t), frame.Width / 2.0, frame.Height * 0.16 + 90 * k, (int)Math.Round(20 * k), TextFace, true);
      }
    }

    protected override void HudBoss(DrawEventArgs e)
    {
      var dz = _s.Disguise;
      if (dz == null) return;
      var m = dz.Metrics;
      string kind = m.Floors <= 9 ? "low-rise block" : m.Floors <= 18 ? "terraced mid-rise" : "tower on podium";
      int iteration = (23 + (int)(_s.BossTime / 1.2)) % 100 + 1;
      double fitness = 0.80 + 0.12 * (1 - Math.Exp(-iteration / 25.0)) + 0.004 * Math.Sin(iteration * 1.7);
      MetricsCard(e.Display, "Massing Study  ·  Option B", $"Site 03 — {kind}, {MassingMetrics.StoreysPerVoxel}F / module", new[]
      {
        ("Site area", $"{m.SiteArea:N0} m²"), ("GFA", $"{m.Gfa:N0} m²"), ("FAR", $"{m.Far:F2}"),
        ("Floors", m.Floors.ToString()), ("Coverage", $"{m.Coverage * 100:F0} %"), ("Height", $"{m.Height:F1} m"),
      }, $"Optimizing…  iter {iteration} / 100   fitness {fitness:F3}", iteration / 100.0);
      NorthArrow(e.Display, e.Viewport, _s.Plane.Origin, _s.Plane.YAxis * _s.VoxelSize * 10);
    }
  }
}
