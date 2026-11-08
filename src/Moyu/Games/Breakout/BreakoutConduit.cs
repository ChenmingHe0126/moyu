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
  /// Draws Rebound as a small painted model: the wall in its frame on a backboard, bricks with one flat tone per face
  /// and a soft shadow on the board, cracks on damaged bricks, a pill paddle, balls with trails, falling power-ups and
  /// debris, chapter palettes and the quiet HUD; or, in boss mode, the facade elevation.
  /// </summary>
  sealed class BreakoutConduit : GameConduit
  {
    const double BlendSeconds = 1.6, Depth = 0.55, BoardZ = -0.75;

    static readonly (Palette palette, Color frame, Color board)[] Chapters =
    {
      (new Palette(Palette.Hex(0xF8F0E7), Palette.Hex(0xEAD6CE), Palette.Hex(0x43405A), Palette.Hex(0x998F9E), Palette.Hex(0xE36F5A), Palette.Hex(0x5C5A8C)), Palette.Hex(0xEFE2D2), Palette.Hex(0xF6EDE2)),
      (new Palette(Palette.Hex(0xEDF6F1), Palette.Hex(0xCAE3DA), Palette.Hex(0x2F4858), Palette.Hex(0x7C9A93), Palette.Hex(0xF08A62), Palette.Hex(0x46668A)), Palette.Hex(0xDCEBE3), Palette.Hex(0xF2F8F4)),
      (new Palette(Palette.Hex(0xF0ECF8), Palette.Hex(0xD1C9EA), Palette.Hex(0x3B3558), Palette.Hex(0x8A82A6), Palette.Hex(0xEDA546), Palette.Hex(0x554C8A)), Palette.Hex(0xE3DDF1), Palette.Hex(0xF6F3FB)),
      (new Palette(Palette.Hex(0xFCEADB), Palette.Hex(0xEFBE9F), Palette.Hex(0x4A2F35), Palette.Hex(0xA27565), Palette.Hex(0x3E7CD6), Palette.Hex(0x74496E)), Palette.Hex(0xF3D9C5), Palette.Hex(0xFBEFE5)),
      (new Palette(Palette.Hex(0xDCE5F1), Palette.Hex(0x9AAFCB), Palette.Hex(0x20304A), Palette.Hex(0x5D7591), Palette.Hex(0xFFB24A), Palette.Hex(0x2E3E66)), Palette.Hex(0xCCD8E6), Palette.Hex(0xEEF3F8)),
    };

    static readonly Color[] MaterialColours = { Palette.Hex(0xA8D8EA), Palette.Hex(0xE8957A), Palette.Hex(0xD9D2CA), Palette.Hex(0x6B7487) };
    static readonly Dictionary<PowerKind, (Color colour, string label)> PowerLooks = new Dictionary<PowerKind, (Color, string)>
    {
      [PowerKind.Wide] = (Palette.Hex(0x8FD3A3), "加宽"), [PowerKind.Multi] = (Palette.Hex(0xF0D277), "多球"), [PowerKind.Slow] = (Palette.Hex(0x96D3DE), "慢速"),
      [PowerKind.Sticky] = (Palette.Hex(0xC6A9E6), "粘板"), [PowerKind.Pierce] = (Palette.Hex(0xF2938C), "穿透"), [PowerKind.Life] = (Palette.Hex(0xF0AFCF), "+1 命"),
      [PowerKind.Narrow] = (Palette.Hex(0x6B7487), "缩短"),
    };

    static readonly Dictionary<PowerKind, string> PowerEnglish = new Dictionary<PowerKind, string>
    {
      [PowerKind.Wide] = "Wide", [PowerKind.Multi] = "Multiball", [PowerKind.Slow] = "Slow", [PowerKind.Sticky] = "Sticky",
      [PowerKind.Pierce] = "Pierce", [PowerKind.Life] = "+1 life", [PowerKind.Narrow] = "Narrow",
    };

    /// <summary>One line on what a power-up does, shown as it is caught.</summary>
    static readonly Dictionary<PowerKind, string> PowerHelp = new Dictionary<PowerKind, string>
    {
      [PowerKind.Wide] = $"挡板变宽 · 接 {BreakoutGame.WideHits} 次球", [PowerKind.Multi] = $"每个球分成三个 · 最多 {BreakoutGame.MaxBalls} 个", [PowerKind.Slow] = $"球变慢 · 接 {BreakoutGame.SlowHits} 次球",
      [PowerKind.Sticky] = $"球粘在挡板上，空格放出 · {BreakoutGame.StickyCatches} 次", [PowerKind.Pierce] = $"直接穿过 {BreakoutGame.PierceBricks} 块砖",
      [PowerKind.Life] = "多一条命", [PowerKind.Narrow] = $"坏道具：挡板变短 · {BreakoutGame.NarrowHits} 次接球",
    };

    static readonly Dictionary<PowerKind, string> PowerHelpEnglish = new Dictionary<PowerKind, string>
    {
      [PowerKind.Wide] = $"Wider paddle · {BreakoutGame.WideHits} catches", [PowerKind.Multi] = $"Every ball splits in three · up to {BreakoutGame.MaxBalls}",
      [PowerKind.Slow] = $"Slower balls · {BreakoutGame.SlowHits} catches", [PowerKind.Sticky] = $"Balls stick to the paddle, Space lets go · {BreakoutGame.StickyCatches} times",
      [PowerKind.Pierce] = $"Straight through {BreakoutGame.PierceBricks} bricks", [PowerKind.Life] = "One more life",
      [PowerKind.Narrow] = $"Bad luck: a shorter paddle · {BreakoutGame.NarrowHits} catches",
    };

    string PowerLabel(PowerKind kind) => T(PowerLooks[kind].label, PowerEnglish[kind]);

    sealed class Fragment
    {
      public double X, Y, Z, Vx, Vy, Vz, Born, Size;
      public Color Colour;
    }

    (string keys, string meaning)[] KeyRow => new[]
    {
      ("← →", T("移动挡板", "move the paddle")), (T("鼠标", "Mouse"), T("挡板跟着走", "the paddle follows")), ("Space", T("发球", "serve")),
      ("Esc", T("暂停", "pause")), ("Home", T("复位视角", "reset view")), BossHint,
    };

    readonly BreakoutSession _s;
    readonly Dictionary<Ball, List<Point3d>> _trails = new Dictionary<Ball, List<Point3d>>();
    readonly List<Fragment> _debris = new List<Fragment>();
    readonly Random _rng = new Random(7);
    // Meshes wrap native memory that only a finalizer frees, so each is kept and rebuilt in place rather than made per frame.
    Mesh _frame, _wall, _shadows, _flash, _paddle, _paddleShadow, _power, _debrisMesh;
    Line[] _cracks = Array.Empty<Line>();
    (BreakoutGame game, int version, Palette palette) _wallKey;
    Palette _framedFor;
    HitEvent _seenHit;
    int _band = -1, _fromBand;
    double _blendStart = double.NegativeInfinity, _lastTime;

    public BreakoutConduit(BreakoutSession session) : base(session) => _s = session;

    // ---------------------------------------------------------------- palette

    double Blend => Math.Min(1, (_s.Time - _blendStart) / BlendSeconds);

    (Palette palette, Color frame, Color board) Current
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
        var (pa, fa, ba) = Chapters[_fromBand];
        var (pb, fb, bb) = Chapters[_band];
        return (Palette.Lerp(pa, pb, t), Lerp(fa, fb, t), Lerp(ba, bb, t));
      }
    }

    protected override Palette Theme => Current.palette;

    protected override (string keys, string meaning)[] KeyHints => KeyRow;

    protected override void IncludeBounds(CalculateBoundingBoxEventArgs e)
    {
      var st = _s.Settings;
      e.IncludeBoundingBox(new BoundingBox(new[] { _s.At(-1.5, -1.5, BoardZ - 0.5), _s.At(st.Width + 1.5, st.Height + 1.5, 2) }));
      if (_s.Boss && _s.Disguise != null) e.IncludeBoundingBox(_s.Disguise.Bounds);
    }

    protected override void DrawBoss(DrawEventArgs e) => _s.Disguise?.Draw(e.Display);

    // ---------------------------------------------------------------- building

    void Box(Mesh m, double x0, double y0, double z0, double x1, double y1, double z1, Color paint, Color shadow)
    {
      double s = _s.Size;
      Toon.AddBox(m, _s.Plane, x0 * s, y0 * s, z0 * s, x1 * s, y1 * s, z1 * s, paint, shadow);
    }

    /// <summary>The mesh emptied for rebuilding (made on first use).</summary>
    static Mesh Fresh(ref Mesh m)
    {
      if (m == null) return m = new Mesh();
      m.Vertices.Clear();
      m.Faces.Clear();
      m.Normals.Clear();
      m.VertexColors.Clear();
      return m;
    }

    void BuildFrame((Palette palette, Color frame, Color board) ch)
    {
      var st = _s.Settings;
      double w = st.Width, h = st.Height, t = 0.6;
      var sh = ch.palette.Shadow;
      Fresh(ref _frame);
      Box(_frame, -t, -t, BoardZ - 0.35, w + t, 0, 0.35, ch.frame, sh);
      Box(_frame, -t, 0, BoardZ - 0.35, 0, h + t, 0.35, ch.frame, sh);
      Box(_frame, w, 0, BoardZ - 0.35, w + t, h + t, 0.35, ch.frame, sh);
      Box(_frame, 0, h, BoardZ - 0.35, w, h + t, 0.35, ch.frame, sh);
      Box(_frame, 0, 0, BoardZ - 0.3, w, h, BoardZ, ch.board, sh);
      _framedFor = ch.palette;
    }

    /// <summary>Damage darkens a brick and cracks it; the game's wall version changes whenever any brick's state does.</summary>
    void BuildWall(BreakoutGame g, Palette p)
    {
      if (_wall != null && ReferenceEquals(_wallKey.game, g) && _wallKey.version == g.WallVersion && _wallKey.palette.Shadow == p.Shadow && Blend >= 1) return;
      _wallKey = (g, g.WallVersion, p);
      Fresh(ref _wall);
      Fresh(ref _shadows);
      var cracks = new List<Line>();
      foreach (var b in g.Bricks)
      {
        if (!b.Alive) continue;
        var paint = MaterialColours[(int)b.Material];
        if (b.Breakable && b.Hits < b.MaxHits) paint = Lerp(paint, p.Shadow, 0.18 * (b.MaxHits - b.Hits));
        Box(_wall, b.X0, b.Y0, -Depth, b.X1, b.Y1, 0, paint, p.Shadow);
        // A soft offset shadow on the board, away from the light.
        Box(_shadows, b.X0 + 0.14, b.Y0 - 0.16, BoardZ + 0.005, b.X1 + 0.14, b.Y1 - 0.16, BoardZ + 0.01, p.Shadow, p.Shadow);
        if (b.Breakable && b.Hits < b.MaxHits) cracks.AddRange(Crack(b));
      }
      _cracks = cracks.ToArray();
    }

    IEnumerable<Line> Crack(Brick b)
    {
      var rng = new Random((int)(b.X0 * 100) * 31 + (int)(b.Y0 * 100) + b.Hits);
      double x = b.X0 + b.Width * (0.3 + 0.4 * rng.NextDouble()), y = b.Y1;
      int n = 3 + (b.MaxHits - b.Hits);
      for (int i = 0; i < n; i++)
      {
        double nx = x + (rng.NextDouble() - 0.5) * b.Width * 0.35, ny = b.Y1 - b.Height * (i + 1) / n;
        yield return new Line(_s.At(x, y, 0.005), _s.At(nx, ny, 0.005));
        x = nx;
        y = ny;
      }
    }

    // ---------------------------------------------------------------- 3D

    protected override void DrawGame(DrawEventArgs e)
    {
      var d = e.Display;
      var g = _s.Game;
      var ch = Current;
      var p = ch.palette;
      double dt = Math.Max(0, Math.Min(0.1, _s.Time - _lastTime));
      _lastTime = _s.Time;
      if (_frame == null || Blend < 1 || _framedFor.Shadow != p.Shadow) BuildFrame(ch);
      BuildWall(g, p);
      d.DrawMeshFalseColors(_frame);
      BeginShadows(d);
      d.DrawMeshShaded(_shadows, Flat(p.Shadow, 0.88));
      EndShadows(d);
      d.DrawMeshFalseColors(_wall);
      if (_cracks.Length > 0) d.DrawLines(_cracks, Lerp(p.Shadow, Color.Black, 0.2), 1);
      FlashHit(d, g, p);
      DrawPaddle(d, g, p);
      DrawBalls(d, g, p);
      DrawPowers(d, g, p);
      DrawDebris(d, g, p, dt);
    }

    void FlashHit(DisplayPipeline d, BreakoutGame g, Palette p)
    {
      var hit = g.LastHit;
      if (hit == null || hit.Broken || g.Elapsed - hit.Time > 0.1) return;
      var m = Fresh(ref _flash);
      var b = hit.Brick;
      Box(m, b.X0, b.Y0, 0.001, b.X1, b.Y1, 0.02, Color.White, Color.White);
      d.DrawMeshShaded(m, Flat(Color.White, 0.5));
    }

    void DrawPaddle(DisplayPipeline d, BreakoutGame g, Palette p)
    {
      double w = g.PaddleWidthNow, h = BreakoutGame.PaddleHeight, x = g.PaddleX, y = BreakoutGame.PaddleY;
      bool sticky = g.StickyLeft > 0, pierce = g.PierceLeft > 0;
      var colour = sticky ? Lerp(p.Accent, PowerLooks[PowerKind.Sticky].colour, 0.6) : p.Accent;
      var m = Fresh(ref _paddle);
      Box(m, x - w / 2, y - h / 2, -0.5, x + w / 2, y + h / 2, 0, colour, p.Shadow);
      d.DrawMeshFalseColors(m);
      Ball(d, _s.At(x - w / 2, y, -0.25), h * 0.62 * _s.Size, colour);
      Ball(d, _s.At(x + w / 2, y, -0.25), h * 0.62 * _s.Size, colour);
      // A soft shadow on the board under the paddle.
      var sh = Fresh(ref _paddleShadow);
      Box(sh, x - w / 2 + 0.1, y - h / 2 - 0.15, BoardZ + 0.005, x + w / 2 + 0.2, y + h / 2 - 0.15, BoardZ + 0.01, p.Shadow, p.Shadow);
      BeginShadows(d);
      d.DrawMeshShaded(sh, Flat(p.Shadow, 0.88));
      EndShadows(d);
      if (pierce) d.DrawLine(_s.At(x - w / 2, y + h / 2 + 0.08, 0.01), _s.At(x + w / 2, y + h / 2 + 0.08, 0.01), PowerLooks[PowerKind.Pierce].colour, 3);
    }

    void DrawBalls(DisplayPipeline d, BreakoutGame g, Palette p)
    {
      double r = BreakoutGame.BallRadius;
      bool pierce = g.PierceLeft > 0;
      var colour = pierce ? PowerLooks[PowerKind.Pierce].colour : Color.White;
      if (_trails.Count > 0) ForgetLostBalls(g);
      foreach (var b in g.Balls)
      {
        var at = _s.At(b.X, b.Y, -0.27);
        if (!_trails.TryGetValue(b, out var trail)) _trails[b] = trail = new List<Point3d>();
        if (!b.Held && g.State == BreakoutState.Playing && (trail.Count == 0 || trail[0].DistanceTo(at) > r * _s.Size * 0.5))
        {
          trail.Insert(0, at);
          if (trail.Count > 7) trail.RemoveAt(trail.Count - 1);
        }
        if (b.Held) trail.Clear();
        for (int i = 1; i < trail.Count; i++)
          Ball(d, trail[i], r * _s.Size * (1 - i * 0.12), Lerp(colour, p.Sky, i / 8.0));
        Ball(d, at, r * _s.Size, colour);
        BeginShadows(d);
        ContactShadow(d, new Plane(_s.At(b.X + 0.15, b.Y - 0.18, BoardZ + 0.012), _s.Plane.ZAxis), r * _s.Size * 1.1, 0.8);
        EndShadows(d);
      }
    }

    /// <summary>Drops the trails of balls no longer in play (without allocating when none is).</summary>
    void ForgetLostBalls(BreakoutGame g)
    {
      List<Ball> dead = null;
      foreach (var b in _trails.Keys)
        if (!g.Balls.Contains(b)) (dead ?? (dead = new List<Ball>())).Add(b);
      if (dead != null) foreach (var b in dead) _trails.Remove(b);
    }

    void DrawPowers(DisplayPipeline d, BreakoutGame g, Palette p)
    {
      if (g.Powers.Count == 0) return;
      var m = Fresh(ref _power);
      foreach (var pw in g.Powers)
      {
        var (colour, _) = PowerLooks[pw.Kind];
        double s = BreakoutGame.PowerSize, bob = Math.Sin(_s.Time * 6 + pw.X) * 0.04;
        Box(m, pw.X - s, pw.Y - s * 0.4 + bob, -0.45, pw.X + s, pw.Y + s * 0.4 + bob, -0.05, colour, p.Shadow);
      }
      d.DrawMeshFalseColors(m);
    }

    void DrawDebris(DisplayPipeline d, BreakoutGame g, Palette p, double dt)
    {
      var hit = g.LastHit;
      if (hit != null && !ReferenceEquals(hit, _seenHit))
      {
        _seenHit = hit;
        if (hit.Broken)
        {
          var b = hit.Brick;
          for (int i = 0; i < 8; i++)
            _debris.Add(new Fragment
            {
              X = b.X0 + b.Width * _rng.NextDouble(), Y = b.Y0 + b.Height * _rng.NextDouble(), Z = -0.25,
              Vx = (_rng.NextDouble() - 0.5) * 5, Vy = 1 + 4 * _rng.NextDouble(), Vz = 1 + 3 * _rng.NextDouble(),
              Born = _s.Time, Size = 0.1 + 0.12 * _rng.NextDouble(), Colour = MaterialColours[(int)b.Material],
            });
        }
      }
      _debris.RemoveAll(f => _s.Time - f.Born > 1.3);
      if (_debris.Count == 0) return;
      var m = Fresh(ref _debrisMesh);
      foreach (var f in _debris)
      {
        if (g.State != BreakoutState.Paused)
        {
          f.Vy -= 18 * dt;
          f.X += f.Vx * dt;
          f.Y += f.Vy * dt;
          f.Z += f.Vz * dt;
        }
        double s = f.Size * (1 - (_s.Time - f.Born) / 1.3);
        Box(m, f.X, f.Y, f.Z, f.X + s, f.Y + s, f.Z + s, f.Colour, p.Shadow);
      }
      d.DrawMeshFalseColors(m);
    }

    // ---------------------------------------------------------------- HUD

    protected override void HudGame(DrawEventArgs e)
    {
      var d = e.Display;
      var g = _s.Game;
      var p = Theme;
      var frame = d.FrameSize;
      double k = Ui(d);
      (string, string, Color?) third;
      switch (_s.Settings.Mode)
      {
        case BreakoutMode.Blitz:
          var left = TimeSpan.FromSeconds(Math.Ceiling(g.TimeLeft));
          third = ("TIME", $"{(int)left.TotalMinutes}:{left.Seconds:00}", g.TimeLeft < 15 ? p.Accent : (Color?)null);
          break;
        case BreakoutMode.Zen: third = ("BRICKS", g.BricksBroken.ToString(), null); break;
        default: third = ("BEST", Math.Max(_s.Best, g.Score).ToString("N0"), null); break;
      }
      ScoreBlock(d, "Rebound", g.Score, ("LIVES", _s.Settings.Mode == BreakoutMode.Zen ? "∞" : g.Lives.ToString(), null),
        ("COMBO", g.Combo >= 2 ? $"×{g.Combo}" : "—", g.Combo >= 2 ? p.Accent : (Color?)null), third);
      LevelBlock(d, g.Level, g.WallName, g.Progress);

      double y = 222 * k;
      // Power-ups wear off by use: the line shows the goes left and shortens with each one.
      void Uses(PowerKind kind, int left, int of, string unit)
      {
        if (left > 0)
          Status(d, ref y, PowerLooks[kind].colour, Zh ? $"{PowerLabel(kind)}  还剩 {left} {unit}" : $"{PowerLabel(kind)}  {left} {unit} left", left / (double)of);
      }
      Uses(PowerKind.Wide, g.WideLeft, BreakoutGame.WideHits, T("次接球", "catches"));
      Uses(PowerKind.Narrow, g.NarrowLeft, BreakoutGame.NarrowHits, T("次接球", "catches"));
      Uses(PowerKind.Slow, g.SlowLeft, BreakoutGame.SlowHits, T("次接球", "catches"));
      Uses(PowerKind.Sticky, g.StickyLeft, BreakoutGame.StickyCatches, T("次粘球", "sticky catches"));
      Uses(PowerKind.Pierce, g.PierceLeft, BreakoutGame.PierceBricks, T("块砖", "bricks"));
      if (g.Balls.Count > 1) Status(d, ref y, PowerLooks[PowerKind.Multi].colour, $"{g.Balls.Count} {T("个球", "balls")}", -1);
      // The launch prompt whenever a ball waits on the paddle: the serve, and a sticky catch mid-rally.
      bool held = false;
      foreach (var b in g.Balls) held |= b.Held;
      if (held && _s.PlayTime > 0.3)
      {
        var at = e.Viewport.WorldToClient(_s.At(g.PaddleX, BreakoutGame.PaddleY - 1.1));
        Text(d, T("空格 发球", "Space  to serve"), Fade(p.Muted, 0.6 + 0.4 * Math.Sin(_s.Time * 4)), at.X, at.Y, Px(15, k), TextFace, true);
      }

      foreach (var pw in g.Powers)
      {
        var at = e.Viewport.WorldToClient(_s.At(pw.X, pw.Y + 0.7));
        Text(d, PowerLabel(pw.Kind), p.Ink, at.X, at.Y, Px(13, k), TextFace, true);
      }
      Popups(e, g, p, frame, k);
    }

    static int Px(double height, double k) => (int)Math.Round(height * k);

    void Popups(DrawEventArgs e, BreakoutGame g, Palette p, Size frame, double k)
    {
      var d = e.Display;
      var hit = g.LastHit;
      if (hit != null && g.Elapsed - hit.Time < 0.9 && hit.Points > 0)
      {
        double t = (g.Elapsed - hit.Time) / 0.9;
        var b = hit.Brick;
        var at = e.Viewport.WorldToClient(_s.At((b.X0 + b.X1) / 2, b.Y1));
        Text(d, $"+{hit.Points}", Fade(hit.Combo > 1 ? p.Accent : p.Ink, 1.3 - t), at.X, at.Y - (10 + 34 * t) * k, Px(hit.Broken ? 26 : 20, k), Display, true);
      }
      if (g.LastPower is var lp && lp != null && g.Elapsed - lp.Value.time < 2.4)
      {
        double t = (g.Elapsed - lp.Value.time) / 2.4;
        var at = e.Viewport.WorldToClient(_s.At(g.PaddleX, BreakoutGame.PaddleY + 2.6));
        double rise = 26 * k * Math.Min(1, t * 2);
        Text(d, PowerLabel(lp.Value.kind), Fade(p.Ink, 1.6 - t * 1.4), at.X, at.Y - rise, Px(22, k), TextFace, true);
        Text(d, T(PowerHelp[lp.Value.kind], PowerHelpEnglish[lp.Value.kind]), Fade(p.Muted, 1.6 - t * 1.4), at.X, at.Y - rise + 26 * k, Px(14, k), TextFace, true);
      }
      if (g.Elapsed - g.LifeLostTime < 0.3)
        d.Draw2dRectangle(new Rectangle(0, 0, frame.Width, frame.Height), Color.Transparent, 0, Color.FromArgb((int)(60 * (1 - (g.Elapsed - g.LifeLostTime) / 0.3)), 255, 110, 90));
      // The chapter card while the cleared wall pauses and as the next one goes up.
      bool clearing = g.State == BreakoutState.Cleared;
      double since = g.Elapsed - g.LevelStartTime;
      if (clearing || (g.Level > _s.Settings.StartLevel && since < 2.4))
      {
        double a = clearing ? Math.Min(1, (g.Elapsed - g.ClearedTime) * 4) : Math.Min(1, (2.4 - since) * 2);
        double cx = frame.Width / 2.0, cy = frame.Height * 0.2;
        if (clearing)
        {
          Text(d, Spaced("Wall cleared"), Fade(p.Accent, a), cx, cy, Px(14, k), UiStrong, true);
          Text(d, $"+{500 * g.Level:N0}", Fade(p.Ink, a), cx, cy + 30 * k, Px(40, k), Display, true);
        }
        else
        {
          Text(d, Spaced($"Level {g.Level:00}"), Fade(p.Accent, a), cx, cy, Px(14, k), UiStrong, true);
          Text(d, Named(g.WallName), Fade(p.Ink, a), cx, cy + 34 * k, Px(32, k), TextFace, true);
        }
      }
    }

    protected override (string title, string big, string line, string badge) ResultText
    {
      get
      {
        var g = _s.Game;
        string title = g.State == BreakoutState.TimeUp ? T("时间到", "Time's up") : T("球用完了", "Out of balls");
        var played = TimeSpan.FromSeconds(g.Elapsed);
        string time = played.TotalHours >= 1 ? $"{(int)played.TotalHours}:{played.Minutes:00}:{played.Seconds:00}" : $"{played.Minutes}:{played.Seconds:00}";
        string line = Zh
          ? $"第 {g.Level} 关    ·    打掉 {g.BricksBroken} 块    ·    {time}" + (_s.NewBest ? "" : $"    ·    最高 {_s.Best:N0}")
          : $"level {g.Level}    ·    {g.BricksBroken} bricks    ·    {time}" + (_s.NewBest ? "" : $"    ·    best {_s.Best:N0}");
        return (title, $"{g.Score:N0}", line, _s.NewBest ? "NEW BEST" : null);
      }
    }

    static string BondEnglish(BreakoutGame g) =>
      g.Settings.Custom != null ? "custom panelling" :
      g.Bond == BondType.Stretcher ? "stretcher bond" : g.Bond == BondType.Stack ? "stack bond" : g.Bond == BondType.English ? "English bond" :
      g.Bond == BondType.Flemish ? "Flemish bond" : g.Bond == BondType.Screen ? "perforated screen" : "arched opening";

    protected override void HudBoss(DrawEventArgs e) => _s.Disguise?.DrawOverlay(e.Display, e.Viewport, _s.BossTime, BondEnglish(_s.Game));
  }
}
