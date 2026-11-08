using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using Moyu.Games.Common.Core;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Games.Common
{
  /// <summary>
  /// Drawing shared by every game, in one quiet, minimal style: a soft painted sky behind the scene, unlit toon
  /// shading under a fixed light, light typography with no boxes, key hints as keycaps that fade once you are
  /// playing, and pale washes for the countdown, pause and result screens. Draws only into the game's viewport and
  /// adds nothing to the document. The boss-mode disguises keep Rhino's own sober look. The HUD is laid out in
  /// 96-dpi units and scaled by <see cref="Ui"/>; the 3D scene is never scaled.
  /// </summary>
  public abstract class GameConduit : DisplayConduit
  {
    /// <summary>Bahnschrift: a DIN-style face, at home on drawings.</summary>
    public const string Display = "Bahnschrift Light", UiLight = "Bahnschrift SemiLight", UiStrong = "Bahnschrift",
      CjkLight = "Microsoft YaHei UI Light", Cjk = "Microsoft YaHei UI", Latin = "Segoe UI", LatinBold = "Segoe UI Semibold";
    const double HintSeconds = 7;
    const int MaxSprites = 200, MaxBalls = 400, MaxFlats = 300;

    /// <summary>
    /// Pixels per HUD unit. The HUD is laid out for a 96-dpi screen and grows with the display's DPI scale, as Rhino's
    /// own UI does, but never past what the frame's height allows, so a 720-px view capture keeps the 1:1 layout.
    /// </summary>
    public static double UiScale(double dpiScale, Size frame) => Math.Max(1, Math.Min(dpiScale, frame.Height / 720.0));

    protected static double Ui(DisplayPipeline d) => UiScale(d.DpiScale, d.FrameSize);

    /// <summary>A HUD length in whole pixels (text heights must be integers).</summary>
    protected static int Px(double v) => (int)Math.Round(v);

    readonly GameSession _session;
    readonly Dictionary<string, (DisplayBitmap bitmap, Size size)> _sprites = new Dictionary<string, (DisplayBitmap, Size)>();
    readonly Dictionary<(int paint, int shadow), Mesh> _balls = new Dictionary<(int, int), Mesh>();
    readonly Dictionary<(int, int), DisplayMaterial> _flats = new Dictionary<(int, int), DisplayMaterial>();
    readonly Stopwatch _backgroundClock = Stopwatch.StartNew();
    readonly Mesh _disc;
    DisplayBitmap _background;
    (Size size, bool boss, Palette palette) _backgroundKey;

    protected GameConduit(GameSession session)
    {
      _session = session;
      _disc = new Mesh();
      _disc.Vertices.Add(Point3d.Origin);
      const int n = 28;
      for (int i = 0; i < n; i++) _disc.Vertices.Add(Math.Cos(i * 2 * Math.PI / n), Math.Sin(i * 2 * Math.PI / n), 0);
      for (int i = 0; i < n; i++) _disc.Faces.AddFace(0, 1 + i, 1 + (i + 1) % n);
      _disc.Normals.ComputeNormals();
    }

    /// <summary>
    /// Frees the native bitmaps, materials and meshes the caches hold; called when the session stops, since a new
    /// conduit is made per game. Subclasses dispose their own meshes in an override and call the base.
    /// </summary>
    public virtual void Release()
    {
      _background?.Dispose();
      _background = null;
      foreach (var s in _sprites.Values) s.bitmap.Dispose();
      _sprites.Clear();
      foreach (var m in _flats.Values) m.Dispose();
      _flats.Clear();
      foreach (var m in _balls.Values) m.Dispose();
      _balls.Clear();
      _disc.Dispose();
    }

    protected bool Mine(DrawEventArgs e) => !_session.Stopped && e.Viewport.Id == _session.ViewportId;

    /// <summary>The HUD speaks Chinese (else English), as the component's Language input says.</summary>
    protected bool Zh => _session.Language == UiLanguage.Chinese;

    /// <summary>The Chinese or the English wording, in the session's language.</summary>
    protected string T(string zh, string en) => Zh ? zh : en;

    /// <summary>A bilingual name such as "庭院 Courtyard": whole in Chinese (the English is part of the look), only its English part otherwise.</summary>
    protected string Named(string both) => Zh ? both : both.Substring(both.IndexOf(' ') + 1);

    /// <summary>The face for running text: YaHei for Chinese, Bahnschrift for English.</summary>
    protected string TextFace => Zh ? CjkLight : UiLight;

    /// <summary>The boss-key hint every game ends its key row with.</summary>
    protected (string keys, string meaning) BossHint => ("`", T("老板键（1 左边的键）", "boss key (left of 1)"));

    /// <summary>The palette right now (games blend between chapter palettes as the level rises).</summary>
    protected abstract Palette Theme { get; }
    /// <summary>Background gradient in boss mode: the light grey of a presentation viewport.</summary>
    protected virtual (Color top, Color bottom) BossBackground => (Color.FromArgb(206, 212, 218), Color.FromArgb(246, 247, 249));
    /// <summary>The key-hint row: keys (space-separated caps, "+" for chords) and what they do.</summary>
    protected abstract (string keys, string meaning)[] KeyHints { get; }

    protected abstract void IncludeBounds(CalculateBoundingBoxEventArgs e);
    protected abstract void DrawGame(DrawEventArgs e);
    protected abstract void DrawBoss(DrawEventArgs e);
    protected abstract void HudGame(DrawEventArgs e);
    protected abstract void HudBoss(DrawEventArgs e);

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      if (!Mine(e)) return;
      try
      {
        IncludeBounds(e);
        // Keep the near clipping plane in front of anything floating between the camera and the scene.
        var vp = e.Viewport;
        var mid = vp.CameraLocation + (vp.CameraTarget - vp.CameraLocation) * 0.6;
        double r = vp.CameraLocation.DistanceTo(vp.CameraTarget) * 0.3;
        e.IncludeBoundingBox(new BoundingBox(mid - new Vector3d(r, r, r), mid + new Vector3d(r, r, r)));
      }
      catch (Exception ex) { Rhino.RhinoApp.WriteLine($"Moyu bounds: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- background

    protected override void PreDrawObjects(DrawEventArgs e)
    {
      if (!Mine(e)) return;
      try
      {
        var size = e.Display.FrameSize;
        var theme = Theme;
        bool boss = _session.Boss;
        bool stale = _background == null || _backgroundKey.size != size || _backgroundKey.boss != boss || (!boss && !_backgroundKey.palette.Near(theme));
        // While the palette glides to the next chapter, repaint the sky a few times a second, not every frame.
        if (stale && (_background == null || _backgroundKey.size != size || _backgroundKey.boss != boss || _backgroundClock.ElapsedMilliseconds > 120))
        {
          _background?.Dispose();
          using (var bmp = boss ? BossSky(size) : Sky(size, theme))
            _background = new DisplayBitmap(bmp);
          _backgroundKey = (size, boss, theme);
          _backgroundClock.Restart();
        }
        if (_background == null) return;
        e.Display.EnableDepthWriting(false);
        e.Display.DrawBitmap(_background, 0, 0);
        e.Display.EnableDepthWriting(true);
      }
      catch (Exception ex) { Rhino.RhinoApp.WriteLine($"Moyu background: {ex.Message}"); }
    }

    Bitmap BossSky(Size size)
    {
      var (top, bottom) = BossBackground;
      var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
      using (var g = Graphics.FromImage(bmp))
      using (var brush = new LinearGradientBrush(new Rectangle(0, -1, bmp.Width, bmp.Height + 2), top, bottom, 90f))
        g.FillRectangle(brush, 0, 0, bmp.Width, bmp.Height);
      return bmp;
    }

    /// <summary>
    /// Sky to horizon, a soft glow high in the middle, and corners settling into the shadow tint. The glow and
    /// vignette are painted small and stretched over the frame: they are soft anyway, and a path gradient over a 4K
    /// frame costs most of a frame on its own.
    /// </summary>
    static Bitmap Sky(Size size, Palette p)
    {
      int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
      var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
      using (var g = Graphics.FromImage(bmp))
      {
        using (var brush = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), p.Sky, p.Horizon, 90f))
          g.FillRectangle(brush, 0, 0, w, h);
        using (var light = SkyLight(p))
        using (var attrs = new ImageAttributes())
        {
          attrs.SetWrapMode(WrapMode.TileFlipXY);
          g.InterpolationMode = InterpolationMode.HighQualityBilinear;
          g.PixelOffsetMode = PixelOffsetMode.Half;
          g.DrawImage(light, new Rectangle(0, 0, w, h), 0, 0, light.Width, light.Height, GraphicsUnit.Pixel, attrs);
        }
      }
      return bmp;
    }

    static Bitmap SkyLight(Palette p)
    {
      const int w = 480, h = 270;
      var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
      using (var g = Graphics.FromImage(bmp))
      {
        g.Clear(Color.Transparent);
        using (var glow = new GraphicsPath())
        {
          glow.AddEllipse(w * 0.1f, -h * 0.55f, w * 0.8f, h * 1.1f);
          using (var brush = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
            g.FillPath(brush, glow);
        }
        using (var vignette = new GraphicsPath())
        {
          vignette.AddEllipse(-w * 0.35f, -h * 0.45f, w * 1.7f, h * 1.9f);
          using (var brush = new PathGradientBrush(vignette) { CenterColor = Color.FromArgb(0, p.Shadow), SurroundColors = new[] { Color.FromArgb(70, p.Shadow) } })
          {
            brush.FocusScales = new PointF(0.55f, 0.5f);
            g.FillRectangle(brush, 0, 0, w, h);
          }
        }
      }
      return bmp;
    }

    // ---------------------------------------------------------------- 3D helpers

    /// <summary>
    /// A colour at 5 bits per channel, for cache keys: a palette blend then lands on a handful of entries instead of
    /// one per frame, and the step is below what the eye picks up on a toon sphere or a sprite.
    /// </summary>
    static int Q(Color c) => (c.A >> 3) << 15 | (c.R >> 3) << 10 | (c.G >> 3) << 5 | (c.B >> 3);

    /// <summary>A toon-shaded ball (cached per colour; only moved and scaled, so its shading stays true).</summary>
    protected void Ball(DisplayPipeline d, Point3d at, double radius, Color paint)
    {
      if (radius <= 0) return;
      var shadow = Theme.Shadow;
      var key = (Q(paint), Q(shadow));
      if (!_balls.TryGetValue(key, out var mesh))
      {
        mesh = Mesh.CreateFromSphere(new Sphere(Point3d.Origin, 1), 40, 26);
        Toon.Paint(mesh, paint, shadow);
        if (_balls.Count > MaxBalls)
        {
          foreach (var m in _balls.Values) m.Dispose();
          _balls.Clear();
        }
        _balls[key] = mesh;
      }
      d.PushModelTransform(Transform.Translation((Vector3d)at) * Transform.Scale(Point3d.Origin, radius));
      d.DrawMeshFalseColors(mesh);
      d.PopModelTransform();
    }

    /// <summary>A flat, unlit, see-through colour (contact shadows, glows, ghosts).</summary>
    protected DisplayMaterial Flat(Color c, double transparency)
    {
      var key = (Q(c), (int)Math.Round(transparency * 100));
      if (!_flats.TryGetValue(key, out var m))
      {
        if (_flats.Count > MaxFlats)
        {
          foreach (var old in _flats.Values) old.Dispose();
          _flats.Clear();
        }
        _flats[key] = m = new DisplayMaterial(Color.Black, transparency) { Emission = c, Specular = Color.Black, Shine = 0 };
      }
      return m;
    }

    /// <summary>A soft round shadow lying in <paramref name="plane"/>. Call between <see cref="BeginShadows"/> and <see cref="EndShadows"/>.</summary>
    protected void ContactShadow(DisplayPipeline d, Plane plane, double radius, double strength = 1)
    {
      var shadow = Theme.Shadow;
      d.PushModelTransform(Transform.PlaneToPlane(Plane.WorldXY, plane) * Transform.Scale(Point3d.Origin, radius));
      d.DrawMeshShaded(_disc, Flat(shadow, 1 - 0.16 * strength));
      d.DrawMeshShaded(_disc, Flat(shadow, 1 - 0.1 * strength));
      d.PopModelTransform();
      d.PushModelTransform(Transform.PlaneToPlane(Plane.WorldXY, plane) * Transform.Scale(Point3d.Origin, radius * 0.62));
      d.DrawMeshShaded(_disc, Flat(shadow, 1 - 0.14 * strength));
      d.PopModelTransform();
    }

    /// <summary>Shadows overlap without fighting each other for depth.</summary>
    protected static void BeginShadows(DisplayPipeline d) => d.EnableDepthWriting(false);
    protected static void EndShadows(DisplayPipeline d) => d.EnableDepthWriting(true);

    // ---------------------------------------------------------------- passes

    protected override void PostDrawObjects(DrawEventArgs e)
    {
      if (!Mine(e)) return;
      try
      {
        if (_session.Boss) DrawBoss(e);
        else DrawGame(e);
      }
      catch (Exception ex)
      {
        // A throw between BeginShadows and EndShadows would leave depth writing off for the rest of the frame.
        e.Display.EnableDepthWriting(true);
        Rhino.RhinoApp.WriteLine($"Moyu display: {ex.Message}");
      }
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
      if (!Mine(e)) return;
      try
      {
        if (_session.Boss) HudBoss(e);
        else
        {
          HudGame(e);
          Overlays(e.Display);
        }
      }
      catch (Exception ex) { Rhino.RhinoApp.WriteLine($"Moyu HUD: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- overlays

    /// <summary>Result screen: a small title, the big score, one line of figures and an optional badge ("NEW BEST").</summary>
    protected abstract (string title, string big, string line, string badge) ResultText { get; }

    void Overlays(DisplayPipeline d)
    {
      var frame = d.FrameSize;
      var p = Theme;
      double k = Ui(d);
      double cx = frame.Width / 2.0, cy = frame.Height / 2.0;
      if (_session.Countdown > 0)
      {
        int n = (int)Math.Ceiling(_session.Countdown);
        double t = n - _session.Countdown;
        Wash(d, frame, p, 0.35);
        Ring(d, cx, cy, 120 * k, 1, Fade(p.Ink, 0.12), (float)(2 * k));
        Ring(d, cx, cy, 120 * k, _session.Countdown / 3.0, p.Accent, (float)(3 * k));
        Text(d, n.ToString(), Fade(p.Ink, 1 - t * 0.5), cx, cy - 6 * k, Px((140 - 24 * t) * k), Display, true);
        Text(d, _session.CountdownIsStart ? T("准备开始", "Get ready") : T("回到游戏", "Back to the game"), Fade(p.Muted, 0.9), cx, cy + 160 * k, Px(18 * k), TextFace, true);
        Hints(d, KeyHints, 1);
        return;
      }
      if (_session.IsPaused)
      {
        Wash(d, frame, p, 0.72);
        Text(d, Spaced("PAUSED"), p.Accent, cx, cy - 70 * k, Px(16 * k), UiStrong, true);
        Text(d, T("已暂停", "Paused"), p.Ink, cx, cy - 22 * k, Px(44 * k), TextFace, true);
        Text(d, T("暂停时键盘交给 Rhino，可以正常敲命令", "While paused, the keyboard is Rhino's: type commands as usual"), p.Muted, cx, cy + 36 * k, Px(14 * k), TextFace, true);
        Hints(d, new[] { ("Esc", T("继续", "resume")), ("Shift+Esc", T("退出", "quit")), BossHint }, 1, cy + 96 * k);
        Hints(d, KeyHints, 0.85);
        return;
      }
      if (_session.ShowsResult)
      {
        var (title, big, line, badge) = ResultText;
        Wash(d, frame, p, Math.Min(0.78, _session.OverTime * 1.6));
        double a = Math.Min(1, _session.OverTime * 2.5);
        Text(d, title, Fade(p.Accent, a), cx, cy - 128 * k, Px(20 * k), TextFace, true);
        Text(d, big, Fade(p.Ink, a), cx, cy - 40 * k, Px(112 * k), Display, true);
        if (badge != null) Text(d, Spaced(badge), Fade(p.Accent, a), cx, cy + 40 * k, Px(15 * k), UiStrong, true);
        Text(d, line, Fade(p.Muted, a), cx, cy + 76 * k, Px(15 * k), TextFace, true);
        if (_session.OverTime > 0.8) Hints(d, new[] { ("Enter", T("再来一局", "play again")), ("Esc", T("退出", "quit")) }, Math.Min(1, (_session.OverTime - 0.8) * 3), cy + 132 * k);
        return;
      }
      // While playing, the hints stay for the first few seconds and then get out of the way.
      double fade = 1 - (_session.PlayTime - HintSeconds) / 1.5;
      if (fade > 0) Hints(d, KeyHints, Math.Min(1, fade));
    }

    static void Wash(DisplayPipeline d, Size frame, Palette p, double strength)
    {
      if (strength <= 0) return;
      var c = Toon.Mix(p.Sky, Color.White, 0.35);
      d.Draw2dRectangle(new Rectangle(0, 0, frame.Width, frame.Height), Color.Transparent, 0, Color.FromArgb((int)(255 * strength), c));
    }

    /// <summary>A circular arc around (cx, cy) in pixels, <paramref name="fraction"/> of a full turn clockwise from twelve o'clock.</summary>
    protected static void Ring(DisplayPipeline d, double cx, double cy, double r, double fraction, Color c, float thickness)
    {
      int n = Math.Max(2, (int)(120 * Math.Max(0, Math.Min(1, fraction))));
      double sweep = 2 * Math.PI * Math.Max(0, Math.Min(1, fraction));
      PointF P(double a) => new PointF((float)(cx + r * Math.Sin(a)), (float)(cy - r * Math.Cos(a)));
      for (int i = 0; i < n; i++) d.Draw2dLine(P(sweep * i / n), P(sweep * (i + 1) / n), c, thickness);
    }

    // ---------------------------------------------------------------- HUD pieces

    protected static void Text(DisplayPipeline d, string s, Color c, double x, double y, int height, string font = UiLight, bool middle = false) =>
      d.Draw2dText(s, c, new Point2d(x, y), middle, height, font);

    /// <summary>Right-aligned to <paramref name="right"/>.</summary>
    protected static void TextRight(DisplayPipeline d, string s, Color c, double right, double y, int height, string font = UiLight)
    {
      var r = d.Measure2dText(s, new Point2d(0, 0), false, 0, height, font);
      d.Draw2dText(s, c, new Point2d(right - r.Width, y), false, height, font);
    }

    /// <summary>Letter-spaced capitals for small labels.</summary>
    protected static string Spaced(string s) => string.Join(" ", s.ToUpperInvariant().ToCharArray());

    protected static void Label(DisplayPipeline d, string s, Color c, double x, double y, int height = 11) => Text(d, Spaced(s), c, x, y, height, UiStrong);

    /// <summary>Top-left block: the game's name, the score large and light, and a row of small figures.</summary>
    protected void ScoreBlock(DisplayPipeline d, string game, long score, params (string label, string value, Color? colour)[] figures)
    {
      var p = Theme;
      double k = Ui(d);
      Label(d, game, p.Accent, 52 * k, 44 * k, Px(12 * k));
      Text(d, score.ToString("N0"), p.Ink, 48 * k, 62 * k, Px(68 * k), Display);
      for (int i = 0; i < figures.Length; i++)
      {
        double x = (52 + i * 108) * k;
        Label(d, figures[i].label, p.Muted, x, 150 * k, Px(11 * k));
        Text(d, figures[i].value, figures[i].colour ?? p.Ink, x - k, 168 * k, Px(28 * k), Display);
      }
    }

    /// <summary>Top-right block: the chapter (level) with a hairline showing the way to the next one.</summary>
    protected void LevelBlock(DisplayPipeline d, int level, string chapter, double progress)
    {
      var p = Theme;
      double k = Ui(d);
      double right = d.FrameSize.Width - 52 * k;
      TextRight(d, Spaced($"LEVEL {level:00}"), p.Muted, right, 44 * k, Px(12 * k), UiStrong);
      TextRight(d, Named(chapter), p.Ink, right, 64 * k, Px(22 * k), TextFace);
      double w = 150 * k;
      float y = (float)(104 * k), thick = (float)(2 * k);
      d.Draw2dLine(new PointF((float)(right - w), y), new PointF((float)right, y), Fade(p.Ink, 0.15), thick);
      d.Draw2dLine(new PointF((float)(right - w), y), new PointF((float)(right - w + w * Math.Max(0, Math.Min(1, progress))), y), p.Accent, thick);
    }

    /// <summary>A status line with a coloured dot and, under it, a hairline that runs out with the time left.</summary>
    protected void Status(DisplayPipeline d, ref double y, Color colour, string text, double fraction)
    {
      var p = Theme;
      double k = Ui(d);
      Dot(d, 58 * k, y + 9 * k, 5, colour);
      Text(d, text, p.Ink, 72 * k, y, Px(15 * k), TextFace);
      if (fraction >= 0)
      {
        float x0 = (float)(72 * k), x1 = (float)(222 * k), ly = (float)(y + 26 * k), thick = (float)(2 * k);
        d.Draw2dLine(new PointF(x0, ly), new PointF(x1, ly), Fade(p.Ink, 0.12), thick);
        d.Draw2dLine(new PointF(x0, ly), new PointF((float)(x0 + (x1 - x0) * Math.Max(0, Math.Min(1, fraction))), ly), colour, thick);
      }
      y += 38 * k;
    }

    /// <summary>A small filled dot (a cached sprite) of <paramref name="radius"/> HUD units at pixel (cx, cy).</summary>
    protected void Dot(DisplayPipeline d, double cx, double cy, int radius, Color c)
    {
      int r = Math.Max(1, Px(radius * Ui(d)));
      var (bmp, size) = Sprite($"dot{r}:{Q(c)}", () =>
      {
        var b = new Bitmap(r * 2 + 2, r * 2 + 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        using (var brush = new SolidBrush(c))
        {
          g.SmoothingMode = SmoothingMode.AntiAlias;
          g.FillEllipse(brush, 1, 1, r * 2, r * 2);
        }
        return b;
      });
      d.DrawBitmap(bmp, (int)(cx - size.Width / 2.0), (int)(cy - size.Height / 2.0));
    }

    /// <summary>
    /// Keycaps with what they do, centred on <paramref name="y"/> (default: just above the bottom edge). A row wider
    /// than the frame wraps onto further rows, which stack upwards from <paramref name="y"/>.
    /// </summary>
    protected void Hints(DisplayPipeline d, (string keys, string meaning)[] hints, double alpha, double y = double.NaN)
    {
      if (hints == null || hints.Length == 0 || alpha <= 0.02) return;
      var p = Theme;
      double k = Ui(d);
      var frame = d.FrameSize;
      int step = (int)Math.Round(Math.Min(1, alpha) * 8);
      // The width is rounded down to 16-px steps so a live resize does not lay the row out again every frame.
      int maxW = Math.Max(64, (int)(frame.Width - 48 * k) / 16 * 16);
      string font = TextFace;
      string key = "hints:" + string.Join("|", hints.Select(h => h.keys + "=" + h.meaning)) + $":{font}:{Q(p.Ink)}:{step}:{Math.Round(k, 2)}:{maxW}";
      var (bmp, size) = Sprite(key, () => KeycapRow(hints, p, step / 8.0, k, maxW, font));
      double cy = double.IsNaN(y) ? frame.Height - 40 * k : y;
      int rowH = Px(30 * k);
      d.DrawBitmap(bmp, (int)(frame.Width / 2.0 - size.Width / 2.0), (int)(cy + rowH / 2.0 - size.Height));
    }

    static Bitmap KeycapRow((string keys, string meaning)[] hints, Palette p, double alpha, double k, int maxW, string meaningFont)
    {
      Color A(Color c, double f = 1) => Color.FromArgb((int)(c.A * alpha * f), c);
      int capH = Px(24 * k), rowH = Px(30 * k);
      float pad = (float)(8 * k), gap = (float)(30 * k), inner = (float)(8 * k), plus = (float)(4 * k), radius = (float)(6 * k);
      using (var capFont = new Font(UiStrong, (float)(13 * k), GraphicsUnit.Pixel))
      using (var textFont = new Font(meaningFont, (float)(14 * k), GraphicsUnit.Pixel))
      using (var measure = new Bitmap(1, 1))
      using (var mg = Graphics.FromImage(measure))
      {
        mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var fmt = StringFormat.GenericTypographic;
        // Text is measured from a point, like DrawString draws it: a width would wrap a long hint at a large UI scale
        // and give a two-line size for text drawn on one line. Rows break only between whole hints.
        // Each hint (its caps and meaning) is laid out as one unit, then the units are packed into rows.
        var units = new List<(List<(string text, bool cap, float x, float w)> items, float width)>();
        foreach (var (keys, meaning) in hints)
        {
          var items = new List<(string text, bool cap, float x, float w)>();
          float x = 0;
          foreach (var tok in keys.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
          {
            var parts = tok.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
              if (i > 0)
              {
                float pw = mg.MeasureString("+", capFont, PointF.Empty, fmt).Width;
                items.Add(("+", false, x, pw));
                x += pw + plus;
              }
              float w = Math.Max(capH, mg.MeasureString(parts[i], capFont, PointF.Empty, fmt).Width + pad * 2);
              items.Add((parts[i], true, x, w));
              x += w + plus;
            }
          }
          x += inner - plus;
          float tw = mg.MeasureString(meaning, textFont, PointF.Empty, fmt).Width;
          items.Add((meaning, false, x, tw));
          units.Add((items, x + tw));
        }
        var rows = new List<(List<(List<(string text, bool cap, float x, float w)> items, float x)> units, float width)>();
        var row = new List<(List<(string text, bool cap, float x, float w)> items, float x)>();
        float rx = 0;
        foreach (var (items, width) in units)
        {
          if (row.Count > 0 && rx + width > maxW - 4)
          {
            rows.Add((row, rx - gap));
            row = new List<(List<(string text, bool cap, float x, float w)> items, float x)>();
            rx = 0;
          }
          row.Add((items, rx));
          rx += width + gap;
        }
        rows.Add((row, rx - gap));
        int bmpW = (int)Math.Ceiling(rows.Max(r => r.width)) + 4, bmpH = rowH * rows.Count;
        var bmp = new Bitmap(Math.Max(1, bmpW), Math.Max(1, bmpH), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
          g.SmoothingMode = SmoothingMode.AntiAlias;
          g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
          g.Clear(Color.Transparent);
          var capFill = A(Color.FromArgb(185, 255, 255, 255));
          var capEdge = A(Color.FromArgb(70, p.Ink));
          var capInk = A(p.Ink);
          var textInk = A(p.Ink, 0.75);
          for (int r = 0; r < rows.Count; r++)
          {
            // Rows are centred on each other; the top of the caps sits a few pixels into the row.
            float ox = 2 + (bmpW - 4 - rows[r].width) / 2, oy = r * rowH + (rowH - capH) / 2f;
            foreach (var (items, ux) in rows[r].units)
              foreach (var (text, cap, ix, w) in items)
              {
                float lx = ox + ux + ix;
                if (cap)
                {
                  var rc = new RectangleF(lx, oy, w, capH);
                  using (var path = Rounded(rc, radius))
                  using (var fill = new SolidBrush(capFill))
                  using (var edge = new Pen(capEdge, (float)k))
                  {
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                  }
                  using (var ink = new SolidBrush(capInk))
                  {
                    var tsz = g.MeasureString(text, capFont, PointF.Empty, fmt);
                    g.DrawString(text, capFont, ink, lx + (w - tsz.Width) / 2, oy + (capH - tsz.Height) / 2, fmt);
                  }
                }
                else
                  using (var ink = new SolidBrush(text == "+" ? capInk : textInk))
                  {
                    var font = text == "+" ? capFont : textFont;
                    var tsz = g.MeasureString(text, font, PointF.Empty, fmt);
                    g.DrawString(text, font, ink, lx, oy + (capH - tsz.Height) / 2, fmt);
                  }
              }
          }
        }
        return bmp;
      }
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
      var path = new GraphicsPath();
      float d = radius * 2;
      path.AddArc(r.X, r.Y, d, d, 180, 90);
      path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
      path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
      path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
      path.CloseFigure();
      return path;
    }

    (DisplayBitmap bitmap, Size size) Sprite(string key, Func<Bitmap> make)
    {
      if (_sprites.TryGetValue(key, out var s)) return s;
      if (_sprites.Count > MaxSprites)
      {
        foreach (var old in _sprites.Values) old.bitmap.Dispose();
        _sprites.Clear();
      }
      using (var bmp = make())
        s = (new DisplayBitmap(bmp), bmp.Size);
      _sprites[key] = s;
      return s;
    }

    protected static Color Fade(Color c, double alpha) => Color.FromArgb((int)(255 * Math.Max(0, Math.Min(1, alpha))), c);

    protected static Color Lerp(Color a, Color b, double t) => Toon.Mix(a, b, t);

    protected static Color Lighten(Color c, double t) => Toon.Mix(c, Color.White, t);

    // ---------------------------------------------------------------- boss mode

    /// <summary>The disguise's metrics card: title, subtitle, label / value rows and a slowly filling progress bar.</summary>
    protected static void MetricsCard(DisplayPipeline d, string title, string subtitle, (string label, string value)[] rows, string progressLabel, double progress)
    {
      var ink = Color.FromArgb(52, 58, 64);
      var grey = Color.FromArgb(120, 126, 134);
      double k = Ui(d);
      int x = Px(24 * k), y = Px(24 * k), w = Px(300 * k), inset = Px(16 * k), rowH = Px(22 * k);
      int h = Px(112 * k) + rows.Length * rowH;
      d.Draw2dRectangle(new Rectangle(x, y, w, h), Color.FromArgb(206, 212, 218), Math.Max(1, Px(k)), Color.FromArgb(235, 255, 255, 255));
      Text(d, title, ink, x + inset, y + 14 * k, Px(15 * k), LatinBold);
      Text(d, subtitle, grey, x + inset, y + 38 * k, Px(11 * k), Latin);
      for (int r = 0; r < rows.Length; r++)
      {
        Text(d, rows[r].label, grey, x + inset, y + 64 * k + r * rowH, Px(13 * k), Latin);
        Text(d, rows[r].value, ink, x + 170 * k, y + 64 * k + r * rowH, Px(13 * k), LatinBold);
      }
      int py = Px(y + 64 * k + rows.Length * rowH + 10 * k), bar = Px(5 * k);
      Text(d, progressLabel, grey, x + inset, py, Px(11 * k), Latin);
      d.Draw2dRectangle(new Rectangle(x + inset, py + Px(20 * k), w - 2 * inset, bar), Color.FromArgb(222, 226, 230), 0, Color.FromArgb(222, 226, 230));
      d.Draw2dRectangle(new Rectangle(x + inset, py + Px(20 * k), (int)((w - 2 * inset) * Math.Max(0, Math.Min(1, progress))), bar), Color.FromArgb(73, 80, 87), 0, Color.FromArgb(73, 80, 87));
    }

    /// <summary>A north arrow in the bottom-right corner pointing along <paramref name="north"/> as it appears on screen.</summary>
    protected static void NorthArrow(DisplayPipeline d, RhinoViewport vp, Point3d origin, Vector3d north)
    {
      var frame = d.FrameSize;
      var ink = Color.FromArgb(52, 58, 64);
      double k = Ui(d);
      var c0 = vp.WorldToClient(origin);
      var c1 = vp.WorldToClient(origin + north);
      var n = new Vector2d(c1.X - c0.X, c1.Y - c0.Y);
      if (!n.Unitize()) n = new Vector2d(0, -1);
      var side = new Vector2d(-n.Y, n.X);
      double ax = frame.Width - 60 * k, ay = frame.Height - 70 * k;
      float thick = (float)(2 * k);
      PointF P(double along, double across) => new PointF((float)(ax + (n.X * along + side.X * across) * k), (float)(ay + (n.Y * along + side.Y * across) * k));
      d.Draw2dLine(P(-24, 0), P(24, 0), ink, thick);
      d.Draw2dLine(P(12, -8), P(24, 0), ink, thick);
      d.Draw2dLine(P(12, 8), P(24, 0), ink, thick);
      var label = P(40, 0);
      Text(d, "N", ink, label.X, label.Y, Px(13 * k), LatinBold, true);
    }
  }
}
