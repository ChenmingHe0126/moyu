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
  /// Draws Swift as a sectional model: a floor slab and a ceiling slab running off both edges, the hall's far wall
  /// with tall windows scrolling past, cross walls with a sill and a lintel round each opening, a far skyline drifting
  /// slowly behind the windows, feathers, and a small faceted swift
  /// that beats its scythe wings on every flap; chapter palettes and the quiet HUD; or, in boss mode, the climate
  /// section.
  /// </summary>
  sealed class SwiftConduit : GameConduit
  {
    const double BlendSeconds = 1.6, Left = -8, Right = 36, Back = -2.0, Front = 1.4, FarWall = -2.9, Skyline = -20, Parallax = 0.25,
      WindowPitch = SwiftGame.WindowPitch, WindowWidth = SwiftGame.WindowWidth, Sill = SwiftGame.WindowSill, Head = SwiftGame.WindowHead;

    static readonly (Palette palette, Color wall, Color slab, string name)[] Chapters =
    {
      (new Palette(Palette.Hex(0xFBF1E6), Palette.Hex(0xF1CFB8), Palette.Hex(0x4A3A3F), Palette.Hex(0xA08A86), Palette.Hex(0xE56B5D), Palette.Hex(0x6A5A8C)), Palette.Hex(0xF4E3D3), Palette.Hex(0xE9D2BF), "门厅 Foyer"),
      (new Palette(Palette.Hex(0xEEF6F0), Palette.Hex(0xC8E2D6), Palette.Hex(0x2F4858), Palette.Hex(0x7C9A93), Palette.Hex(0xF08A62), Palette.Hex(0x46668A)), Palette.Hex(0xDCEDE4), Palette.Hex(0xC7DDD1), "回廊 Cloister"),
      (new Palette(Palette.Hex(0xF1EDF9), Palette.Hex(0xD3C9EC), Palette.Hex(0x3B3558), Palette.Hex(0x8A82A6), Palette.Hex(0xEDA546), Palette.Hex(0x554C8A)), Palette.Hex(0xE5DFF3), Palette.Hex(0xD3CAE9), "中庭 Atrium"),
      (new Palette(Palette.Hex(0xE2EEF8), Palette.Hex(0xA9C6E2), Palette.Hex(0x22374F), Palette.Hex(0x6283A3), Palette.Hex(0xF4845F), Palette.Hex(0x34507A)), Palette.Hex(0xD6E4F2), Palette.Hex(0xBFD3E8), "天井 Lightwell"),
      (new Palette(Palette.Hex(0xDCD6EE), Palette.Hex(0xF2C6B6), Palette.Hex(0x2E2A4A), Palette.Hex(0x7D769A), Palette.Hex(0xE8735F), Palette.Hex(0x3E3970)), Palette.Hex(0xE7DDEB), Palette.Hex(0xD6C6DC), "黄昏 Dusk"),
    };

    static readonly Color Body = Palette.Hex(0x34365A), Belly = Palette.Hex(0xF4F1EA), Wing = Palette.Hex(0x474B78), Beak = Palette.Hex(0xF2A65A),
      FeatherPaint = Palette.Hex(0xF6B85A), Bump = Palette.Hex(0xFF7A66);

    (string keys, string meaning)[] KeyRow => new[]
    {
      ("Space", T("扇一下翅膀", "flap")), (T("鼠标左键", "Click"), T("也能扇", "flaps too")), ("Esc", T("暂停", "pause")),
      ("Home", T("复位视角", "reset view")), BossHint,
    };

    readonly SwiftSession _s;
    readonly List<(double x, double w, double h, double z)> _skyline = new List<(double, double, double, double)>();
    /// <summary>Static for a palette (slabs, skyline, far wall; the last two slide along X); the walls and the swift are refilled each frame.</summary>
    Mesh _slabs, _sky, _farWall;
    readonly Mesh _walls = new Mesh(), _swift = new Mesh();
    /// <summary>The colours the static meshes were built with.</summary>
    (Palette palette, Color wall, Color slab) _staticFor;
    SwiftGame _levelGame;
    int _band = -1, _fromBand, _levelSeen = 1;
    double _blendStart = double.NegativeInfinity, _levelAt = double.NegativeInfinity;

    public SwiftConduit(SwiftSession session) : base(session)
    {
      _s = session;
      var rng = new Random(5);
      for (double x = 0; x < 64;)
      {
        double w = 2 + rng.NextDouble() * 4, h = 3 + rng.NextDouble() * 9;
        _skyline.Add((x, w, h, Skyline - rng.NextDouble() * 4));
        x += w + rng.NextDouble() * 1.5;
      }
    }

    public override void Release()
    {
      _slabs?.Dispose();
      _sky?.Dispose();
      _farWall?.Dispose();
      _slabs = _sky = _farWall = null;
      _walls.Dispose();
      _swift.Dispose();
      base.Release();
    }

    // ---------------------------------------------------------------- palette

    double Blend => Math.Min(1, (_s.Time - _blendStart) / BlendSeconds);

    (Palette palette, Color wall, Color slab) Current
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
        var b = Chapters[_band];
        if (t >= 1) return (b.palette, b.wall, b.slab);
        var a = Chapters[_fromBand];
        return (Palette.Lerp(a.palette, b.palette, t), Lerp(a.wall, b.wall, t), Lerp(a.slab, b.slab, t));
      }
    }

    protected override Palette Theme => Current.palette;

    protected override (string keys, string meaning)[] KeyHints => KeyRow;

    protected override void IncludeBounds(CalculateBoundingBoxEventArgs e)
    {
      e.IncludeBoundingBox(new BoundingBox(new[] { _s.At(Left, -2, Skyline - 5), _s.At(Right, SwiftGame.Height + 2, Front + 1) }));
      if (_s.Boss && _s.Disguise != null) e.IncludeBoundingBox(_s.Disguise.Bounds);
    }

    protected override void DrawBoss(DrawEventArgs e) => _s.Disguise?.Draw(e.Display);

    // ---------------------------------------------------------------- building

    void Box(Mesh m, double x0, double y0, double z0, double x1, double y1, double z1, Color paint, Color shadow)
    {
      double s = _s.Size;
      Toon.AddBox(m, _s.Plane, x0 * s, y0 * s, z0 * s, x1 * s, y1 * s, z1 * s, paint, shadow);
    }

    /// <summary>
    /// The static meshes of a palette: the floor and ceiling slabs (their joints scroll instead), the skyline and the
    /// far wall, both built one period longer than the view so they can slide along X and still fill it.
    /// </summary>
    void BuildStatic((Palette palette, Color wall, Color slab) ch)
    {
      var sh = ch.palette.Shadow;
      // A chapter blend rebuilds these many times: free the old ones now, not at some later finalizer run.
      _slabs?.Dispose();
      _sky?.Dispose();
      _farWall?.Dispose();
      _slabs = new Mesh();
      Box(_slabs, Left, -1.3, FarWall, Right, 0, Front, ch.slab, sh);
      Box(_slabs, Left, SwiftGame.Height, FarWall, Right, SwiftGame.Height + 0.55, Front, ch.slab, sh);
      _sky = BuildSkyline(ch.palette);
      _farWall = BuildFarWall(ch.wall, ch.palette);
      _staticFor = ch;
    }

    /// <summary>The far skyline: one quiet tone between the horizon and the shadow; two periods, slid back by up to one.</summary>
    Mesh BuildSkyline(Palette p)
    {
      var m = new Mesh();
      var tone = Lerp(p.Horizon, p.Shadow, 0.1);
      foreach (var (x, w, h, z) in _skyline)
        for (int k = 0; k < 2; k++)
        {
          double x0 = x + k * 64 - 12;
          Box(m, x0, -1.3, z - 1.5, x0 + w, h, z, tone, Lerp(tone, p.Shadow, 0.3));
        }
      return m;
    }

    /// <summary>The hall's far wall beyond the cut, a little darker than the cross walls; one window more than the view, slid back by up to a pitch.</summary>
    Mesh BuildFarWall(Color wall, Palette p)
    {
      var m = new Mesh();
      var paint = Lerp(wall, p.Shadow, 0.14);
      var frame = Lerp(Color.White, p.Shadow, 0.08);
      double z0 = FarWall, z1 = FarWall + 0.3, h = SwiftGame.Height;
      for (double x = Left; x < Right + WindowPitch; x += WindowPitch)
      {
        double a = x + (WindowPitch - WindowWidth) / 2, b = a + WindowWidth;
        Box(m, x, 0, z0, a, h, z1, paint, p.Shadow);
        Box(m, b, 0, z0, x + WindowPitch, h, z1, paint, p.Shadow);
        Box(m, a, 0, z0, b, Sill, z1, paint, p.Shadow);
        Box(m, a, Head, z0, b, h, z1, paint, p.Shadow);
        // A slim frame: sill, a transom near the head and a central mullion.
        Box(m, a - 0.08, Sill - 0.1, z1, b + 0.08, Sill, z1 + 0.12, frame, p.Shadow);
        Box(m, a, Head - 1.9, z0 + 0.1, b, Head - 1.8, z0 + 0.18, frame, p.Shadow);
        Box(m, (a + b) / 2 - 0.05, Sill, z0 + 0.1, (a + b) / 2 + 0.05, Head, z0 + 0.18, frame, p.Shadow);
      }
      return m;
    }

    void AddWall(Mesh m, Wall w, double t, Color paint, Color trim, Color shadow)
    {
      double x0 = w.X - _s.Game.Distance, x1 = x0 + SwiftGame.WallThickness;
      if (x1 < Left || x0 > Right) return;
      double c = w.CentreAt(t), lo = c - w.Gap / 2, hi = c + w.Gap / 2;
      Box(m, x0, 0, Back, x1, lo, Front - 0.2, paint, shadow);
      Box(m, x0, hi, Back, x1, SwiftGame.Height, Front - 0.2, paint, shadow);
      // Sill and lintel stand proud of the wall, so the opening reads as a window.
      Box(m, x0 - 0.12, lo - 0.16, Back - 0.1, x1 + 0.12, lo, Front - 0.05, trim, shadow);
      Box(m, x0 - 0.12, hi, Back - 0.1, x1 + 0.12, hi + 0.16, Front - 0.05, trim, shadow);
      if (w.Moving)
      {
        // Shutter rails: two thin bars on the wall face, the full height of the opening's travel.
        double r0 = w.Centre - w.Gap / 2 - w.Swing - 0.2, r1 = w.Centre + w.Gap / 2 + w.Swing + 0.2;
        Box(m, x0 + 0.15, Math.Max(0, r0), Front - 0.2, x0 + 0.27, Math.Min(SwiftGame.Height, r1), Front - 0.06, trim, shadow);
        Box(m, x1 - 0.27, Math.Max(0, r0), Front - 0.2, x1 - 0.15, Math.Min(SwiftGame.Height, r1), Front - 0.06, trim, shadow);
      }
    }

    // ---------------------------------------------------------------- 3D

    protected override void DrawGame(DrawEventArgs e)
    {
      var d = e.Display;
      var g = _s.Game;
      var ch = Current;
      var p = ch.palette;
      // Rebuilt when a colour baked into them changes: most frames of a chapter blend, then not until the next one. BuildStatic frees the old meshes.
      var built = _staticFor;
      if (_slabs == null || !built.palette.Near(p) || built.palette.Shadow != p.Shadow || built.wall != ch.wall || built.slab != ch.slab) BuildStatic(ch);
      Slid(d, _sky, g.Distance * Parallax % 64);
      Slid(d, _farWall, g.Distance % WindowPitch);
      d.DrawMeshFalseColors(_slabs);

      // Joints in the floor and ceiling scroll with the course.
      var joint = Fade(p.Shadow, 0.18);
      double off = g.Distance % 2.4;
      for (double x = Left - off; x < Right; x += 2.4)
      {
        d.DrawLine(_s.At(x, 0.005, Back - 0.2), _s.At(x, 0.005, Front), joint, 1);
        d.DrawLine(_s.At(x, -1.3, Front + 0.005), _s.At(x, 0, Front + 0.005), joint, 1);
        d.DrawLine(_s.At(x, SwiftGame.Height, Front + 0.005), _s.At(x, SwiftGame.Height + 0.55, Front + 0.005), joint, 1);
      }

      _walls.Vertices.Clear();
      _walls.Faces.Clear();
      _walls.Normals.Clear();
      _walls.VertexColors.Clear();
      var trim = Lerp(Color.White, p.Accent, 0.12);
      foreach (var w in g.Walls) AddWall(_walls, w, g.Elapsed, w.Hit && g.Zen ? Lerp(ch.wall, Bump, 0.25) : ch.wall, trim, p.Shadow);
      if (_walls.Vertices.Count > 0) d.DrawMeshFalseColors(_walls);

      BeginShadows(d);
      var floor = new Plane(_s.At(0, 0.01, 0), _s.Plane.XAxis, -_s.Plane.ZAxis);
      double bx = SwiftGame.BirdX, by = g.Y;
      floor.Origin = _s.At(bx + 0.25, 0.012, -0.2);
      ContactShadow(d, floor, _s.Size * (0.9 + by * 0.05), Math.Max(0.15, 1 - by / 10));
      foreach (var f in g.FeatherList.Where(f => !f.Taken))
      {
        floor.Origin = _s.At(f.X - g.Distance + 0.2, 0.012, -0.2);
        ContactShadow(d, floor, _s.Size * 0.4, Math.Max(0.1, 0.6 - f.Y / 20));
      }
      EndShadows(d);

      foreach (var f in g.FeatherList) DrawFeather(d, f, g, p);
      DrawTrail(d, g, p);
      DrawSwift(d, g, p);
    }

    /// <summary>Draws a static mesh slid <paramref name="shift"/> course units back along the section's X.</summary>
    void Slid(DisplayPipeline d, Mesh m, double shift)
    {
      d.PushModelTransform(Transform.Translation(_s.Plane.XAxis * (-shift * _s.Size)));
      d.DrawMeshFalseColors(m);
      d.PopModelTransform();
    }

    void DrawFeather(DisplayPipeline d, Feather f, SwiftGame g, Palette p)
    {
      double age = f.Taken ? g.Clock - f.TakenAt : 0;
      if (age > 0.6) return;
      double x = f.X - g.Distance;
      if (x < Left || x > Right) return;
      double scale = f.Taken ? 1 - age / 0.6 : 1, lift = f.Taken ? age * 2.5 : Math.Sin(g.Clock * 2.4 + f.X) * 0.12;
      double spin = 0.6 + Math.Sin(g.Clock * 1.7 + f.X) * 0.25;
      var outline = new List<Point2d>();
      for (int i = 0; i <= 12; i++)
      {
        double u = i / 12.0, half = Math.Sin(u * Math.PI) * 0.17 * (1 - 0.35 * u);
        outline.Add(new Point2d(u - 0.5, half));
      }
      for (int i = 11; i >= 1; i--)
      {
        double u = i / 12.0, half = Math.Sin(u * Math.PI) * 0.17 * (1 - 0.35 * u);
        outline.Add(new Point2d(u - 0.5, -half));
      }
      var frame = new Plane(_s.At(x, f.Y + lift, 0.2), _s.Plane.XAxis, _s.Plane.YAxis);
      frame.Rotate(spin, frame.ZAxis);
      var pts = outline.Select(q => new Point2d(q.X * 1.0 * scale * _s.Size, q.Y * scale * _s.Size)).ToList();
      using (var m = new Mesh())
      {
        Toon.AddPrism(m, frame, pts, -0.035 * _s.Size, 0.035 * _s.Size, FeatherPaint, p.Shadow);
        d.DrawMeshFalseColors(m);
      }
      d.DrawLine(frame.PointAt(-0.55 * scale * _s.Size, 0, 0.05 * _s.Size), frame.PointAt(0.45 * scale * _s.Size, 0, 0.05 * _s.Size), Lerp(FeatherPaint, p.Shadow, 0.45), 1);
    }

    /// <summary>Seconds since the crash. The game's clock stops when the round ends (at once on a floor landing), so the result screen's time carries it on.</summary>
    double CrashAge(SwiftGame g) => g.Clock - g.CrashTime + (g.IsOver ? _s.OverTime : 0);

    /// <summary>A wisp of air behind the swift: small fading beads along its last few metres, dying away as it falls.</summary>
    void DrawTrail(DisplayPipeline d, SwiftGame g, Palette p)
    {
      if (g.State == SwiftState.Ready) return;
      double fade = g.State == SwiftState.Flying || g.State == SwiftState.Paused ? 1 : 1 - CrashAge(g) / SwiftGame.CrashSeconds;
      if (fade <= 0) return;
      var trail = g.Trail;
      int n = Math.Min(12, trail.Count);
      for (int i = 0; i < n; i++)
      {
        var q = trail[trail.Count - 1 - i];
        double t = (i + 1) / (double)(n + 1);
        Ball(d, _s.AtCourse(q.X - 0.4, q.Y, -0.1), _s.Size * 0.09 * (1 - t) * fade, Lerp(Color.White, p.Sky, t));
      }
      double since = g.Clock - g.LastFlap;
      if (since < 0.35)
        for (int k = 0; k < 3; k++)
        {
          double a = -2.2 + k * 0.5, r = 0.3 + since * 3;
          Ball(d, _s.At(SwiftGame.BirdX - 0.3 + Math.Cos(a) * r, g.Y - 0.1 + Math.Sin(a) * r * 0.6, 0), _s.Size * 0.07 * (1 - since / 0.35), Color.White);
        }
    }

    // ---------------------------------------------------------------- the swift

    /// <summary>Wing angle about the body (radians, up positive): a quick downstroke after each flap, then a glide.</summary>
    static double WingAngle(SwiftGame g)
    {
      if (g.State == SwiftState.Ready) return 0.35 * Math.Sin(g.Clock * 9);
      if (g.State == SwiftState.Crashed || g.State == SwiftState.Over) return -0.2;
      double t = g.Clock - g.LastFlap;
      if (t < 0.06) return 0.95 - t / 0.06 * 0.3;
      if (t < 0.18) return 0.65 - (t - 0.06) / 0.12 * 1.35;
      if (t < 0.45) return -0.7 + (t - 0.18) / 0.27 * 0.85;
      return 0.15 + 0.06 * Math.Sin(g.Clock * 5);
    }

    static double Tilt(SwiftGame g)
    {
      switch (g.State)
      {
        case SwiftState.Ready: return 0.05 * Math.Sin(g.Clock * 4);
        // Nose down to the landing tilt and hold it, so nothing snaps when the fall ends.
        case SwiftState.Crashed: return Math.Max(-1.4, -0.6 - (g.Clock - g.CrashTime) * 3);
        case SwiftState.Over: return g.HitWall ? -1.4 : -0.35;
        default: return Math.Max(-1.2, Math.Min(0.5, g.Vy * 0.055));
      }
    }

    void DrawSwift(DisplayPipeline d, SwiftGame g, Palette p)
    {
      double tilt = Tilt(g), wing = WingAngle(g), s = _s.Size * 1.4;
      var cam = d.Viewport.CameraLocation;
      var origin = _s.At(SwiftGame.BirdX, g.Y, 0);
      Point3d W(double x, double y, double z)
      {
        double c = Math.Cos(tilt), sn = Math.Sin(tilt);
        return origin + _s.Plane.XAxis * ((x * c - y * sn) * s) + _s.Plane.YAxis * ((x * sn + y * c) * s) + _s.Plane.ZAxis * (z * s);
      }
      var m = _swift;
      m.Vertices.Clear();
      m.Faces.Clear();
      m.Normals.Clear();
      m.VertexColors.Clear();
      void Tri(Point3d a, Point3d b, Point3d c, Color paint)
      {
        var n = Vector3d.CrossProduct(b - a, c - a);
        if (!n.Unitize()) return;
        if (n * (cam - a) < 0) n = -n;
        var col = Toon.Shade(paint, p.Shadow, n);
        int i = m.Vertices.Count;
        foreach (var v in new[] { a, b, c })
        {
          m.Vertices.Add(v);
          m.VertexColors.Add(col);
        }
        m.Faces.AddFace(i, i + 1, i + 2);
      }
      // Body: a faceted spindle, dark above and pale below.
      Point3d nose = W(0.55, 0.02, 0), head = W(0.26, 0.21, 0), back = W(-0.12, 0.19, 0), tail = W(-0.48, 0.07, 0), belly = W(0.02, -0.2, 0);
      foreach (double side in new[] { -1.0, 1.0 })
      {
        var flank = W(0.05, 0.03, 0.2 * side);
        Tri(nose, head, flank, Body);
        Tri(head, back, flank, Body);
        Tri(back, tail, flank, Body);
        Tri(nose, flank, belly, Belly);
        Tri(belly, flank, tail, Lerp(Belly, Body, 0.35));
        // Forked tail.
        Tri(W(-0.42, 0.08, 0.03 * side), W(-0.98, 0.2, 0.2 * side), W(-0.78, 0.03, 0.09 * side), Body);
        // Scythe wings, hinged at the shoulder and swept back.
        double c = Math.Cos(wing), sn = Math.Sin(wing);
        Point3d Span(double x, double span) => W(x, 0.13 + span * sn, side * (0.1 + span * c));
        var shoulder = W(0.1, 0.13, 0.1 * side);
        var root = W(-0.28, 0.11, 0.12 * side);
        var mid = Span(-0.02, 0.55);
        var tip = Span(-0.42, 1.08);
        var trail = Span(-0.32, 0.4);
        Tri(shoulder, mid, trail, Wing);
        Tri(shoulder, trail, root, Lerp(Wing, Body, 0.4));
        Tri(mid, tip, trail, Lerp(Wing, Color.White, 0.08));
      }
      // Beak.
      Tri(W(0.53, 0.07, 0.04), W(0.72, 0.0, 0), W(0.53, -0.03, -0.04), Beak);
      Tri(W(0.53, 0.07, -0.04), W(0.72, 0.0, 0), W(0.53, -0.03, 0.04), Beak);
      d.DrawMeshFalseColors(m);
      // Eye on the near side.
      Ball(d, W(0.32, 0.11, 0.13), s * 0.065, Color.White);
      Ball(d, W(0.345, 0.11, 0.17), s * 0.035, Body);
      if (g.Zen && g.Clock - g.LastBump < 0.4)
        Ball(d, origin, s * (0.5 + (g.Clock - g.LastBump) * 2), Color.FromArgb(40, Bump));
    }

    // ---------------------------------------------------------------- HUD

    protected override void HudGame(DrawEventArgs e)
    {
      var d = e.Display;
      var g = _s.Game;
      var p = Theme;
      var frame = d.FrameSize;
      double k = Ui(d);
      var third = g.Zen ? ("BUMPS", g.Bumps.ToString(), (Color?)null) : ("BEST", Math.Max(_s.Best, g.Score).ToString("N0"), (Color?)null);
      ScoreBlock(d, "Swift", g.Score, ("FEATHERS", g.Feathers.ToString(), null), ("SPEED", $"{g.Speed * _s.Metres:F1}", null), third);
      int band = Palette.Band(g.Level);
      LevelBlock(d, g.Level, Chapters[band].name, g.Score % 10 / 10.0);

      double y = 222 * k;
      var next = g.NextWall;
      if (g.State == SwiftState.Flying && next != null && next.Moving && next.X - g.BirdCourseX < 14)
        Status(d, ref y, p.Accent, T("滑动窗扇  · 看准再过", "Sliding sash  · time it"), -1);
      if (g.Zen) Status(d, ref y, Lighten(p.Accent, 0.3), T("Zen  · 撞墙不结束", "Zen  · walls do not end the flight"), -1);

      if (g.State == SwiftState.Ready && _s.PlayTime > 0.2)
      {
        var at = e.Viewport.WorldToClient(_s.At(SwiftGame.BirdX, g.Y + 1.4));
        Text(d, T("空格 / 点一下  起飞", "Space or click  to take off"), Fade(p.Muted, 0.6 + 0.4 * Math.Sin(_s.Time * 4)), at.X, at.Y, (int)Math.Round(15 * k), TextFace, true);
      }
      // Each wall passed pops a small +1 over its opening.
      foreach (var w in g.Walls.Where(w => w.Passed && !w.Hit))
      {
        double since = (g.BirdCourseX - w.Middle) / g.Speed;
        if (since < 0 || since > 0.7) continue;
        var at = e.Viewport.WorldToClient(_s.AtCourse(w.Middle, w.CentreAt(g.Elapsed) + w.Gap / 2 + 0.6));
        Text(d, "+1", Fade(p.Ink, 1.2 - since / 0.7), at.X, at.Y - 30 * k * since, (int)Math.Round(22 * k), Display, true);
      }
      foreach (var f in g.FeatherList.Where(f => f.Taken && g.Clock - f.TakenAt < 0.8))
      {
        double t = (g.Clock - f.TakenAt) / 0.8;
        var at = e.Viewport.WorldToClient(_s.AtCourse(f.X, f.Y + 0.8));
        Text(d, T("羽毛 +1", "Feather +1"), Fade(p.Accent, 1.2 - t), at.X, at.Y - 26 * k * t, (int)Math.Round(16 * k), TextFace, true);
      }
      double crashAge = CrashAge(g);
      if ((g.State == SwiftState.Crashed || g.IsOver) && crashAge < 0.3)
        d.Draw2dRectangle(new Rectangle(0, 0, frame.Width, frame.Height), Color.Transparent, 0, Color.FromArgb((int)(55 * (1 - crashAge / 0.3)), 255, 110, 90));

      // Chapter card as each new level begins; a new round starts at level 1 without one.
      if (!ReferenceEquals(g, _levelGame))
      {
        _levelGame = g;
        _levelSeen = 1;
        _levelAt = double.NegativeInfinity;
      }
      if (g.Level != _levelSeen)
      {
        _levelSeen = g.Level;
        _levelAt = _s.Time;
      }
      double since2 = _s.Time - _levelAt;
      if (since2 < 2.4)
      {
        double a = Math.Min(1, Math.Min(since2 * 5, (2.4 - since2) * 2));
        double cx = frame.Width / 2.0, cy = frame.Height * 0.2;
        Text(d, Spaced($"Level {g.Level:00}"), Fade(p.Accent, a), cx, cy, (int)Math.Round(14 * k), UiStrong, true);
        Text(d, Named(Chapters[band].name), Fade(p.Ink, a), cx, cy + 34 * k, (int)Math.Round(32 * k), TextFace, true);
      }
    }

    protected override (string title, string big, string line, string badge) ResultText
    {
      get
      {
        var g = _s.Game;
        string title = g.HitWall ? T("撞墙了", "Hit a wall") : T("落地了", "Landed");
        string line = Zh
          ? $"第 {g.Level} 层    ·    羽毛 {g.Feathers}    ·    飞了 {g.Distance * _s.Metres:F0} m" + (_s.NewBest ? "" : $"    ·    最高 {_s.Best:N0}")
          : $"Level {g.Level}    ·    {g.Feathers} feathers    ·    flew {g.Distance * _s.Metres:F0} m" + (_s.NewBest ? "" : $"    ·    best {_s.Best:N0}");
        return (title, $"{g.Score:N0}", line, _s.NewBest ? "NEW BEST" : null);
      }
    }

    protected override void HudBoss(DrawEventArgs e) => _s.Disguise?.DrawOverlay(e.Display, e.Viewport, _s.BossTime);
  }
}
