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
  /// Draws Serpentine as a small painted world: the surface as a soft checkered garden (on an open site, standing on
  /// a plinth like a site model), a smooth toon snake with contact shadows, fruit and hazards as simple shapes,
  /// five chapter palettes that glide into each other as the level rises, and a quiet HUD. In boss mode, the
  /// circulation study.
  /// </summary>
  sealed class SnakeConduit : GameConduit
  {
    /// <summary>One chapter's look: the shared palette plus ground, plinth and snake colours.</summary>
    sealed class Chapter
    {
      public Palette Palette;
      public Color Ground, Plinth, Head, Tail;
    }

    static readonly Chapter[] Chapters =
    {
      // 庭院 — warm sand, a teal snake.
      new Chapter { Palette = new Palette(Palette.Hex(0xFBF0E4), Palette.Hex(0xF1D4C4), Palette.Hex(0x4A3F55), Palette.Hex(0x9A8A98), Palette.Hex(0xE0735F), Palette.Hex(0x6E5A8A)),
        Ground = Palette.Hex(0xF3DEC2), Plinth = Palette.Hex(0xE5B79C), Head = Palette.Hex(0x55BFB2), Tail = Palette.Hex(0x2F6F86) },
      // 草坡 — sage meadow, a peach snake.
      new Chapter { Palette = new Palette(Palette.Hex(0xEEF5E9), Palette.Hex(0xCDE2CF), Palette.Hex(0x34495E), Palette.Hex(0x7F9A8E), Palette.Hex(0xEE8559), Palette.Hex(0x4F6D8A)),
        Ground = Palette.Hex(0xC4E0B9), Plinth = Palette.Hex(0x95C0A2), Head = Palette.Hex(0xFFA07A), Tail = Palette.Hex(0xDE5F5C) },
      // 丘陵 — lavender dusk, a golden snake.
      new Chapter { Palette = new Palette(Palette.Hex(0xEFEAF7), Palette.Hex(0xCEC5E8), Palette.Hex(0x3D3557), Palette.Hex(0x8C84A8), Palette.Hex(0xEFA93C), Palette.Hex(0x5B4E8C)),
        Ground = Palette.Hex(0xDDD4EF), Plinth = Palette.Hex(0xB3A6D6), Head = Palette.Hex(0xFFCF5C), Tail = Palette.Hex(0xE7865B) },
      // 峡谷 — terracotta canyon, an ink-blue snake.
      new Chapter { Palette = new Palette(Palette.Hex(0xFCEBD9), Palette.Hex(0xF1BD97), Palette.Hex(0x4B2E2E), Palette.Hex(0xA0705E), Palette.Hex(0x3D7DD8), Palette.Hex(0x7A4B6E)),
        Ground = Palette.Hex(0xEEB892), Plinth = Palette.Hex(0xD58D6A), Head = Palette.Hex(0x4F78A8), Tail = Palette.Hex(0x263248) },
      // 山脊 — blue hour on the ridge, a sunlit snake.
      new Chapter { Palette = new Palette(Palette.Hex(0xDDE7F2), Palette.Hex(0x9CB3CE), Palette.Hex(0x22324A), Palette.Hex(0x5F7894), Palette.Hex(0xFFB347), Palette.Hex(0x2E3F66)),
        Ground = Palette.Hex(0xCAD8E7), Plinth = Palette.Hex(0x8EA7C4), Head = Palette.Hex(0xFFD36E), Tail = Palette.Hex(0xEF8354) },
    };

    static readonly string[] ChapterNames = { "庭院 Courtyard", "草坡 Meadow", "丘陵 Hills", "峡谷 Canyon", "山脊 Ridge" };
    static readonly Color AppleRed = Palette.Hex(0xE8574F), Leaf = Palette.Hex(0x6BBF59), Grape = Palette.Hex(0x9B7FD4), Melon = Palette.Hex(0x86D093),
      MelonStripe = Palette.Hex(0x3E8E5A), BombInk = Palette.Hex(0x3B3A4A), Danger = Palette.Hex(0xE85D5D), Frost = Palette.Hex(0xA8DCF0), Spirit = Palette.Hex(0xF4F1FF);

    const double BlendSeconds = 1.6;

    readonly SnakeSession _s;
    readonly Mesh _unit = Mesh.CreateFromSphere(new Sphere(Point3d.Origin, 1), 28, 18);
    Mesh _ground, _plinth, _crystal;
    Chapter _paintedFor;
    int _band = -1, _fromBand;
    double _blendStart = double.NegativeInfinity, _paintedAt = double.NegativeInfinity;

    public SnakeConduit(SnakeSession session) : base(session) => _s = session;

    // ---------------------------------------------------------------- palette

    double Blend => Math.Min(1, (_s.Time - _blendStart) / BlendSeconds);

    void TrackChapter()
    {
      int band = Palette.Band(_s.Game.Level);
      if (_band < 0) _band = _fromBand = band;
      else if (band != _band)
      {
        _fromBand = _band;
        _band = band;
        _blendStart = _s.Time;
      }
    }

    Chapter Current
    {
      get
      {
        TrackChapter();
        double t = Blend;
        if (t >= 1) return Chapters[_band];
        var a = Chapters[_fromBand];
        var b = Chapters[_band];
        return new Chapter
        {
          Palette = Palette.Lerp(a.Palette, b.Palette, t), Ground = Lerp(a.Ground, b.Ground, t), Plinth = Lerp(a.Plinth, b.Plinth, t),
          Head = Lerp(a.Head, b.Head, t), Tail = Lerp(a.Tail, b.Tail, t),
        };
      }
    }

    protected override Palette Theme => Current.Palette;

    // C switches the camera, so its hint names the view it switches to.
    protected override (string keys, string meaning)[] KeyHints => new[]
    {
      ("← →", T("左转 / 右转（以蛇头为准）", "turn left / right (as the head sees it)")), ("↑ / Shift", T("冲刺", "sprint")),
      ("C", _s.Follow ? T("自由视角", "free view") : T("第一人称", "first person")), ("Esc", T("暂停", "pause")),
      ("Home", T("复位视角", "reset view")), BossHint,
    };

    protected override void IncludeBounds(CalculateBoundingBoxEventArgs e)
    {
      var box = _s.Field.Bounds;
      // Room for the plinth below and, in boss mode, evergreen crowns (~3.9 Step) and 1.75 m figures on a small site.
      double figure = _s.ToMetres > 0 ? 2 / _s.ToMetres : 0;
      box.Inflate(Math.Max(_s.Field.Step * 4, figure));
      e.IncludeBoundingBox(box);
    }

    protected override void DrawBoss(DrawEventArgs e) => _s.Disguise?.Draw(e.Display);

    // ---------------------------------------------------------------- ground

    /// <summary>The playable cells with their own vertices, so alternate cells can take a slightly different tone.</summary>
    void PaintGround(Chapter ch)
    {
      var f = _s.Field;
      var grid = f.Grid;
      var shadow = ch.Palette.Shadow;
      var alt = Lerp(ch.Ground, Color.White, 0.16);
      double lift = f.Step * 0.01;
      bool build = _ground == null;
      if (build) _ground = new Mesh();
      int start = 0;
      foreach (var c in grid.FreeCells())
      {
        var paint = (c.U + c.V) % 2 == 0 ? ch.Ground : alt;
        int k0 = 2 * c.U, l0 = 2 * c.V;
        for (int b = 0; b <= 2; b++)
          for (int a = 0; a <= 2; a++)
          {
            var n = f.SampleNormal(k0 + a, l0 + b);
            var colour = GroundShade(paint, shadow, n, f.Closed);
            if (build)
            {
              _ground.Vertices.Add(f.Sample(k0 + a, l0 + b) + n * lift);
              _ground.Normals.Add(n);
              _ground.VertexColors.Add(colour);
            }
            else _ground.VertexColors.SetColor(start + b * 3 + a, colour);
          }
        if (build)
          for (int b = 0; b < 2; b++)
            for (int a = 0; a < 2; a++)
              _ground.Faces.AddFace(start + b * 3 + a, start + b * 3 + a + 1, start + (b + 1) * 3 + a + 1, start + (b + 1) * 3 + a);
        start += 9;
      }
      if (!f.Closed) PaintPlinth(ch);
      _paintedFor = ch;
      _paintedAt = _s.Time;
    }

    /// <summary>
    /// Ground colour. An open site is hill-shaded (slopes towards the sun lighter, away darker, exaggerated) so its
    /// relief reads from any height; a closed form gets a gentle wrap-around light so its far side is not a pit.
    /// </summary>
    static Color GroundShade(Color paint, Color shadow, Vector3d n, bool closed)
    {
      var lit = Lerp(paint, Color.White, 0.38);
      var dark = Lerp(paint, shadow, 0.34);
      if (closed)
      {
        double w = Math.Max(0, Math.Min(1, (n * Toon.Light + 0.6) / 1.5));
        return w > 0.55 ? Lerp(paint, lit, (w - 0.55) / 0.45) : Lerp(dark, paint, w / 0.55);
      }
      double flat = Toon.Light.Z;
      double t = Math.Max(-1, Math.Min(1, (n * Toon.Light - flat) / (1 - flat) * 2.6));
      return t >= 0 ? Lerp(paint, lit, t) : Lerp(paint, dark, -t);
    }

    /// <summary>An open site stands on a block, like a cut-out site model: walls from its edge down to a flat base.</summary>
    void PaintPlinth(Chapter ch)
    {
      var f = _s.Field;
      var grid = f.Grid;
      double baseZ = f.Bounds.Min.Z - f.Step * 1.6;
      _plinth = new Mesh();
      void Wall(Point3d a, Point3d b, Point3d inside)
      {
        var n = Vector3d.CrossProduct(b - a, Vector3d.ZAxis);
        if (!n.Unitize()) return;
        if (n * (a - inside) < 0) n = -n;
        var col = Toon.Shade(ch.Plinth, ch.Palette.Shadow, n);
        int i = _plinth.Vertices.Count;
        foreach (var p in new[] { a, b, new Point3d(b.X, b.Y, baseZ), new Point3d(a.X, a.Y, baseZ) })
        {
          _plinth.Vertices.Add(p);
          _plinth.Normals.Add(n);
          _plinth.VertexColors.Add(col);
        }
        _plinth.Faces.AddFace(i, i + 1, i + 2, i + 3);
      }
      void Side(Cell c, int k0, int l0, int dk, int dl)
      {
        var centre = f.Centre(c);
        for (int s = 0; s < 2; s++)
          Wall(f.Sample(k0 + dk * s, l0 + dl * s), f.Sample(k0 + dk * (s + 1), l0 + dl * (s + 1)), centre);
      }
      foreach (var c in grid.FreeCells())
      {
        int k = 2 * c.U, l = 2 * c.V;
        if (!grid.TryStep(c, -1, 0, out _)) Side(c, k, l, 0, 1);
        if (!grid.TryStep(c, 1, 0, out _)) Side(c, k + 2, l, 0, 1);
        if (!grid.TryStep(c, 0, -1, out _)) Side(c, k, l, 1, 0);
        if (!grid.TryStep(c, 0, 1, out _)) Side(c, k, l + 2, 1, 0);
      }
    }

    // ---------------------------------------------------------------- 3D

    protected override void DrawGame(DrawEventArgs e)
    {
      var d = e.Display;
      var ch = Current;
      // While the palette glides to the next chapter, repaint the ground a few times a second, not every frame.
      if (_ground == null || _paintedFor == null || (_paintedFor.Ground != ch.Ground && (Blend >= 1 || _s.Time - _paintedAt > 0.1))) PaintGround(ch);
      d.DrawMeshFalseColors(_ground);
      if (_plinth != null) d.DrawMeshFalseColors(_plinth);
      var g = _s.Game;
      var body = g.Body.ToList();
      bool blownUp = g.Cause == DeathCause.Bomb;

      BeginShadows(d);
      foreach (var item in g.Items) ItemShadow(d, item, g);
      if (!blownUp) SnakeShadows(d, body);
      EndShadows(d);

      foreach (var item in g.Items) DrawItem(d, item, g, ch);
      if (!blownUp) DrawSnake(d, body, g, ch);
      DrawBurst(d, g, ch);
    }

    /// <summary>The tangent plane just above the surface under a point, nudged away from the light.</summary>
    Plane ShadowPlane(Point3d above, Vector3d n, double height)
    {
      var away = -Toon.Light;
      away -= (away * n) * n;
      var foot = above - n * height + away * (height * 0.35) + n * (_s.Field.Step * 0.025);
      return new Plane(foot, n);
    }

    void SnakeShadows(DisplayPipeline d, List<Cell> body)
    {
      double r = _s.Field.Step * 0.42;
      for (int i = 0; i < body.Count; i++)
      {
        var at = _s.SegmentAt(body, i, r * 0.9, out var n);
        ContactShadow(d, ShadowPlane(at, n, r * 0.9), r * (i == 0 ? 1.15 : 1.0), 0.9);
      }
    }

    void DrawSnake(DisplayPipeline d, List<Cell> body, SnakeGame g, Chapter ch)
    {
      double step = _s.Field.Step, r = step * 0.42;
      bool ghost = g.GhostActive, ice = g.IceActive;
      double powerLeft = Math.Max(g.GhostUntil, g.IceUntil) - g.Elapsed;
      bool blink = powerLeft < 1.5 && (int)(_s.Time * 8) % 2 == 0;
      Color SegmentColour(int i)
      {
        var c = Lerp(ch.Head, ch.Tail, body.Count <= 1 ? 0 : Math.Round(i * 10.0 / (body.Count - 1)) / 10.0);
        if (ice && !blink) c = Lerp(c, Frost, 0.6);
        return c;
      }
      var positions = new Point3d[body.Count];
      for (int i = 0; i < body.Count; i++) positions[i] = _s.SegmentAt(body, i, r * 0.9, out _);
      for (int i = body.Count - 1; i >= 1; i--)
      {
        double ri = r * (1 - 0.3 * i / Math.Max(1.0, body.Count));
        var c = SegmentColour(i);
        if (ghost && !blink)
        {
          d.PushModelTransform(Transform.Translation((Vector3d)positions[i]) * Transform.Scale(Point3d.Origin, ri));
          d.DrawMeshShaded(_unit, Flat(Lerp(c, Spirit, 0.5), 0.55));
          d.PopModelTransform();
          continue;
        }
        Ball(d, positions[i], ri, c);
        // A bead between neighbours keeps the body reading as one smooth tube.
        var next = positions[i - 1];
        if (next.DistanceTo(positions[i]) < step * 1.5) Ball(d, (positions[i] + next) / 2, ri * 0.92, Lerp(c, SegmentColour(i - 1), 0.5));
      }
      var head = _s.SegmentAt(body, 0, r * 0.98, out var n);
      var fwd = _s.HeadForward(body);
      fwd -= (fwd * n) * n;
      if (!fwd.Unitize()) fwd = _s.Field.Direction(body[0], 1, 0);
      var side = Vector3d.CrossProduct(n, fwd);
      bool dead = g.IsOver && g.Cause != DeathCause.None;
      double R = step * 0.5;
      var headColour = dead && (int)(_s.Time * 6) % 2 == 0 ? Danger : SegmentColour(0);
      Ball(d, head, R, headColour);
      foreach (int s in new[] { -1, 1 })
      {
        var eye = head + fwd * (R * 0.52) + side * (R * 0.42 * s) + n * (R * 0.45);
        Ball(d, eye, R * 0.25, Color.White);
        var front = eye + fwd * (R * 0.26);
        if (dead)
        {
          d.DrawLine(front + (side + n) * R * 0.12, front - (side + n) * R * 0.12, ch.Palette.Ink, 2);
          d.DrawLine(front + (side - n) * R * 0.12, front - (side - n) * R * 0.12, ch.Palette.Ink, 2);
        }
        else Ball(d, eye + fwd * (R * 0.14) + n * (R * 0.03), R * 0.13, ch.Palette.Ink);
      }
      if (!dead && (int)(_s.Time * 4) % 4 == 0)
      {
        var tip = head + fwd * (R * 1.55);
        d.DrawLine(head + fwd * R * 0.95, tip, Danger, 2);
        d.DrawLine(tip, tip + (fwd + side * 0.7) * (R * 0.22), Danger, 2);
        d.DrawLine(tip, tip + (fwd - side * 0.7) * (R * 0.22), Danger, 2);
      }
    }

    (Point3d at, Vector3d n, double r, double scale) ItemPose(Item item, SnakeGame g)
    {
      var f = _s.Field;
      double step = f.Step;
      double age = g.Elapsed - item.Born, left = item.Expires - g.Elapsed;
      double grow = Math.Min(1, age / 0.35);
      double scale = grow < 1 ? 1 - Math.Pow(1 - grow, 3) + Math.Sin(grow * Math.PI) * 0.22 : 1;
      if (item.Kind != ItemKind.Bomb && left < 1) scale *= Math.Max(0, left);
      var n = f.Normal(item.Cell);
      double bob = (Math.Sin(_s.Time * 2.4 + item.Cell.U * 1.3 + item.Cell.V * 0.7) + 1) * step * 0.05;
      double r = step * 0.34 * scale;
      return (f.Lifted(item.Cell, step * 0.38 + bob), n, r, scale);
    }

    void ItemShadow(DisplayPipeline d, Item item, SnakeGame g)
    {
      var (at, n, r, scale) = ItemPose(item, g);
      if (scale <= 0) return;
      ContactShadow(d, ShadowPlane(at, n, _s.Field.Step * 0.38), r * (item.Kind == ItemKind.Melon ? 1.3 : 1.0), 0.75);
    }

    void DrawItem(DisplayPipeline d, Item item, SnakeGame g, Chapter ch)
    {
      var f = _s.Field;
      double step = f.Step;
      var (at, n, r, scale) = ItemPose(item, g);
      if (scale <= 0) return;
      double left = item.Expires - g.Elapsed;
      var tangent = f.Direction(item.Cell, 1, 0);
      var side = Vector3d.CrossProduct(n, tangent);
      switch (item.Kind)
      {
        case ItemKind.Apple:
          Ball(d, at, r, AppleRed);
          d.DrawLine(at + n * r * 0.85, at + n * r * 1.35, Palette.Hex(0x8A5A3C), 3);
          Ball(d, at + n * r * 1.2 + tangent * r * 0.38, r * 0.26, Leaf);
          break;
        case ItemKind.Grape:
          for (int k = 0; k < 4; k++)
          {
            double a = k * Math.PI / 2 + _s.Time * 0.8;
            Ball(d, at + (tangent * Math.Cos(a) + side * Math.Sin(a)) * r * 0.46 - n * r * 0.15, r * 0.48, Grape);
          }
          Ball(d, at + n * r * 0.42, r * 0.48, Lighten(Grape, 0.1));
          break;
        case ItemKind.Melon:
          double big = r * 1.28;
          var melonAt = at + n * (big - r);
          Ball(d, melonAt, big, Melon);
          for (int k = 0; k < 3; k++)
          {
            var axis = tangent * Math.Cos(k * Math.PI / 3) + side * Math.Sin(k * Math.PI / 3);
            d.DrawCircle(new Circle(new Plane(melonAt, axis), big * 1.01), MelonStripe, 2);
          }
          break;
        case ItemKind.Bomb:
          bool blink = left < 3 && (int)(_s.Time * 8) % 2 == 0;
          double pulse = 0.62 + 0.08 * Math.Sin(_s.Time * 5);
          var ring = new Plane(f.Lifted(item.Cell, step * 0.04), n);
          d.DrawCircle(new Circle(ring, step * pulse), blink ? Color.White : Danger, 2);
          d.DrawCircle(new Circle(ring, step * pulse * 0.78), Fade(Danger, 0.45), 1);
          if (!blink)
          {
            Ball(d, at, r, BombInk);
            var fuse = at + n * r * 1.45 + tangent * r * 0.3;
            d.DrawLine(at + n * r * 0.9, fuse, Palette.Hex(0xC9A37A), 3);
            Ball(d, fuse, r * (0.16 + 0.06 * Math.Abs(Math.Sin(_s.Time * 15))), ch.Palette.Accent);
          }
          break;
        case ItemKind.Ice:
          var plane = new Plane(at, tangent, side);
          plane.Rotate(_s.Time * 1.2, n);
          d.DrawMeshFalseColors(Crystal(plane, r * 0.85, ch.Palette.Shadow));
          break;
        case ItemKind.Ghost:
          Ball(d, at, r, Spirit);
          foreach (int s in new[] { -1, 1 })
            Ball(d, at + n * r * 0.25 + side * r * 0.36 * s + tangent * r * 0.78, r * 0.15, ch.Palette.Ink);
          break;
      }
    }

    static readonly (int su, int sv)[] CubeCorners = { (-1, -1), (1, -1), (1, 1), (-1, 1) };

    /// <summary>
    /// An ice cube with one flat tone per face, as lit in the world. One mesh is kept and its vertices and colours
    /// moved each frame, since it turns as it hovers.
    /// </summary>
    Mesh Crystal(Plane p, double h, Color shadow)
    {
      bool build = _crystal == null;
      if (build) _crystal = new Mesh();
      var m = _crystal;
      var axes = new[] { p.XAxis, p.YAxis, p.ZAxis };
      int i = 0;
      for (int a = 0; a < 3; a++)
        foreach (int s in new[] { -1, 1 })
        {
          var n = axes[a] * s;
          var u = axes[(a + 1) % 3];
          var v = axes[(a + 2) % 3];
          var c = p.Origin + n * h;
          var col = Toon.Shade(Frost, shadow, n);
          foreach (var (su, sv) in CubeCorners)
          {
            var at = c + u * (h * su) + v * (h * sv);
            if (build)
            {
              m.Vertices.Add(at);
              m.VertexColors.Add(col);
            }
            else
            {
              m.Vertices.SetVertex(i, at);
              m.VertexColors.SetColor(i, col);
            }
            i++;
          }
          if (!build) continue;
          int f = i - 4;
          if (s > 0) m.Faces.AddFace(f, f + 1, f + 2, f + 3);
          else m.Faces.AddFace(f, f + 3, f + 2, f + 1);
        }
      return m;
    }

    /// <summary>Little balls bursting from what was eaten; bits of tail tumbling off a bomb.</summary>
    void DrawBurst(DisplayPipeline d, SnakeGame g, Chapter ch)
    {
      var ev = g.LastEvent;
      if (ev == null) return;
      var f = _s.Field;
      double t = g.Elapsed - ev.Time, step = f.Step;
      var centre = f.Lifted(ev.Cell, step * 0.4);
      var n = f.Normal(ev.Cell);
      var tangent = f.Direction(ev.Cell, 1, 0);
      var side = Vector3d.CrossProduct(n, tangent);
      var rng = new Random(ev.Cell.U * 131 + ev.Cell.V * 7 + (int)(ev.Time * 10));
      if (ev.Kind == ItemKind.Bomb)
      {
        if (t > 1.4) return;
        double p = t / 1.4;
        for (int i = 0; i < 26; i++)
        {
          double a = rng.NextDouble() * Math.PI * 2, reach = step * (0.6 + 2.4 * rng.NextDouble()) * Math.Sqrt(p);
          var at = centre + (tangent * Math.Cos(a) + side * Math.Sin(a)) * reach + n * step * rng.NextDouble() * 1.4 * p;
          Ball(d, at, step * 0.09 * (1 - p), i % 3 == 0 ? ch.Palette.Accent : Danger);
        }
        foreach (var lost in ev.Lost)
        {
          var dir = (tangent * (rng.NextDouble() - 0.5) + side * (rng.NextDouble() - 0.5)) * 3 + n;
          double up = step * (3.5 * t - 3.0 * t * t);
          Ball(d, f.Lifted(lost, step * 0.36) + dir * step * 1.2 * t + n * up, step * 0.36 * (1 - p), Lerp(ch.Tail, ch.Palette.Shadow, 0.3));
        }
        return;
      }
      if (t > 0.7) return;
      double q = t / 0.7;
      var colour = SparkColour(ev.Kind);
      for (int i = 0; i < 14; i++)
      {
        double a = i * Math.PI * 2 / 14 + rng.NextDouble() * 0.3, reach = step * (0.3 + 1.6 * q) * (0.8 + 0.4 * rng.NextDouble());
        Ball(d, centre + (tangent * Math.Cos(a) + side * Math.Sin(a)) * reach + n * step * 0.8 * Math.Sin(q * Math.PI), step * 0.08 * (1 - q), colour);
      }
    }

    static Color SparkColour(ItemKind kind)
    {
      switch (kind)
      {
        case ItemKind.Apple: return AppleRed;
        case ItemKind.Grape: return Grape;
        case ItemKind.Melon: return Melon;
        case ItemKind.Ice: return Frost;
        default: return Spirit;
      }
    }

    // ---------------------------------------------------------------- HUD

    protected override void HudGame(DrawEventArgs e)
    {
      var d = e.Display;
      var g = _s.Game;
      var p = Theme;
      (string, string, Color?) third;
      switch (_s.Settings.Mode)
      {
        case SnakeMode.Blitz:
          var left = TimeSpan.FromSeconds(Math.Ceiling(g.TimeLeft));
          third = ("TIME", $"{(int)left.TotalMinutes}:{left.Seconds:00}", g.TimeLeft < 15 ? Danger : (Color?)null);
          break;
        case SnakeMode.Zen: third = ("FRUIT", g.FruitsEaten.ToString(), null); break;
        default: third = ("BEST", Math.Max(_s.Best, g.Score).ToString("N0"), null); break;
      }
      ScoreBlock(d, "Serpentine", g.Score, ("LENGTH", g.Length.ToString(), null), ("SPEED", g.Speed.ToString("F1"), g.Boost ? p.Accent : (Color?)null), third);
      LevelBlock(d, g.Level, ChapterNames[Palette.Band(g.Level)], (g.FruitsEaten % SnakeGame.FruitsPerLevel) / (double)SnakeGame.FruitsPerLevel);

      double y = 222 * Ui(d);
      double comboLeft = SnakeGame.ComboWindow - (g.Elapsed - g.LastEatTime);
      if (g.Combo >= 2 && comboLeft > 0) Status(d, ref y, p.Accent, $"{T("连击", "Combo")} ×{g.Combo}", comboLeft / SnakeGame.ComboWindow);
      if (g.IceActive) Status(d, ref y, Frost, $"{T("冰冻 · 减速", "Ice · slower")}  {g.IceUntil - g.Elapsed:F1}s", (g.IceUntil - g.Elapsed) / SnakeGame.PowerSeconds);
      if (g.GhostActive) Status(d, ref y, Lerp(Spirit, p.Muted, 0.4), $"{T("幽灵 · 可以穿过自己", "Ghost · pass through yourself")}  {g.GhostUntil - g.Elapsed:F1}s", (g.GhostUntil - g.Elapsed) / SnakeGame.PowerSeconds);
      if (g.Boost && !g.IsOver) Status(d, ref y, p.Accent, T("冲刺 ×1.8", "Sprint ×1.8"), -1);
      if (g.SlopeFactor > 1.03) Status(d, ref y, Palette.Hex(0xF2B544), $"{T("下坡", "Downhill")}  +{(g.SlopeFactor - 1) * 100:F0}%", -1);
      else if (g.SlopeFactor < 0.97) Status(d, ref y, Palette.Hex(0x7FA7D9), $"{T("上坡", "Uphill")}  −{(1 - g.SlopeFactor) * 100:F0}%", -1);
      Status(d, ref y, p.Muted, _s.Follow ? T("第一人称 · 视角跟着蛇头", "First person · the view follows the head") : T("自由视角 · 拖动鼠标转模型", "Free view · drag to turn the model"), -1);
      Popups(e, g, p);
    }

    void Popups(DrawEventArgs e, SnakeGame g, Palette p)
    {
      var d = e.Display;
      var frame = d.FrameSize;
      double k = Ui(d);
      int Px(double v) => (int)Math.Round(v * k);
      var ev = g.LastEvent;
      if (ev != null && g.Elapsed - ev.Time < 1.2)
      {
        double t = (g.Elapsed - ev.Time) / 1.2;
        var at = e.Viewport.WorldToClient(_s.Field.Lifted(ev.Cell, _s.Field.Step));
        double y = at.Y - (26 + 46 * t) * k;
        switch (ev.Kind)
        {
          case ItemKind.Bomb:
            Text(d, g.Cause == DeathCause.Bomb ? "BOOM" : $"−{ev.Lost.Length}", Fade(Danger, 1.4 - t), at.X, y, Px(34), Display, true);
            if (ev.Points < 0) Text(d, $"{ev.Points}", Fade(Danger, 1.2 - t), at.X, y + 34 * k, Px(18), Display, true);
            if (t < 0.25)
              d.Draw2dRectangle(new Rectangle(0, 0, frame.Width, frame.Height), Color.Transparent, 0, Color.FromArgb((int)(70 * (1 - t / 0.25)), 255, 120, 100));
            break;
          case ItemKind.Ice: Text(d, T("冰冻", "Ice"), Fade(p.Ink, 1.4 - t), at.X, y, Px(22), TextFace, true); break;
          case ItemKind.Ghost: Text(d, T("幽灵", "Ghost"), Fade(p.Ink, 1.4 - t), at.X, y, Px(22), TextFace, true); break;
          default:
            Text(d, $"+{ev.Points}", Fade(ev.Combo > 1 ? p.Accent : p.Ink, 1.4 - t), at.X, y, Px(ev.Kind == ItemKind.Melon ? 40 : 30), Display, true);
            if (ev.Combo > 1) Text(d, $"×{ev.Combo}", Fade(p.Accent, 1.3 - t), at.X, y + 38 * k, Px(18), UiStrong, true);
            break;
        }
      }
      if (g.Elapsed - g.LevelUpTime < 2.6 && g.Level > 1)
      {
        // A chapter card, the way a level is named in a storybook game.
        double t = (g.Elapsed - g.LevelUpTime) / 2.6;
        double a = Math.Min(1, Math.Min(t * 5, (1 - t) * 3));
        double cx = frame.Width / 2.0, top = frame.Height * 0.2;
        Text(d, Spaced($"LEVEL {g.Level:00}"), Fade(p.Accent, a), cx, top, Px(14), UiStrong, true);
        Text(d, Named(ChapterNames[Palette.Band(g.Level)]), Fade(p.Ink, a), cx, top + 36 * k, Px(34), TextFace, true);
        Text(d, T("更快了", "Faster now"), Fade(p.Muted, a), cx, top + 84 * k, Px(15), TextFace, true);
      }
    }

    protected override (string title, string big, string line, string badge) ResultText
    {
      get
      {
        var g = _s.Game;
        string title;
        switch (g.Cause)
        {
          case DeathCause.Wall: title = T("撞到了边缘", "Ran into the edge"); break;
          case DeathCause.Self: title = T("咬到了自己", "Bit yourself"); break;
          case DeathCause.Bomb: title = T("被炸没了", "Blown up"); break;
          default: title = T("时间到", "Time's up"); break;
        }
        string time = $"{TimeSpan.FromSeconds(g.Elapsed):m\\:ss}";
        string line = Zh
          ? $"长度 {g.Length}    ·    第 {g.Level} 级    ·    {g.FruitsEaten} 个水果    ·    {time}" + (_s.NewBest ? "" : $"    ·    最高 {_s.Best:N0}")
          : $"length {g.Length}    ·    level {g.Level}    ·    {g.FruitsEaten} fruit    ·    {time}" + (_s.NewBest ? "" : $"    ·    best {_s.Best:N0}");
        return (title, $"{g.Score:N0}", line, _s.NewBest ? "NEW BEST" : null);
      }
    }

    protected override void HudBoss(DrawEventArgs e)
    {
      var dz = _s.Disguise;
      if (dz == null) return;
      var d = e.Display;
      dz.DrawLabels(d, e.Viewport);
      var m = dz.Metrics;
      int pass = (11 + (int)(_s.BossTime / 1.2)) % 40 + 1;
      string kind = _s.Field.Closed ? "Landscape loop over a closed form" : "Landscape walk over site terrain";
      MetricsCard(d, "Landscape Plan  ·  Route A", $"{kind}, {_s.Field.Step * _s.ToMetres:F1} m grid", new[]
      {
        ("Route length", $"{m.Length:F1} m"), ("Elevation gain", $"{m.Gain:F1} m"), ("Average slope", $"{m.AverageSlope * 100:F1} %"),
        ("Max slope", $"{m.MaxSlope * 100:F1} %"), ("≤ 5 % (accessible)", $"{m.Accessible * 100:F0} %"),
        ("Trees", $"{dz.Plan.Trees.Count}  ({dz.Plan.Trees.Count(t => t.Evergreen)} evergreen)"), ("Benches · plazas", $"{dz.Plan.Benches.Count} · {dz.Nodes.Length}"),
        ("Route in shade", $"{dz.Plan.ShadeShare * 100:F0} %"),
      }, $"Analysing slope…  pass {pass} / 40", pass / 40.0);
      SlopeLegend(d);
    }

    /// <summary>The slope colour scale, bottom right; on a narrow viewport it moves right, clear of the metrics card.</summary>
    static void SlopeLegend(DisplayPipeline d)
    {
      var frame = d.FrameSize;
      var grey = Color.FromArgb(110, 116, 124);
      double k = Ui(d);
      int x0 = (int)Math.Max((24 + 300 + 24) * k, frame.Width - 290 * k), y0 = (int)(frame.Height - 62 * k), w = (int)(200 * k), h = (int)Math.Round(10 * k);
      int label = (int)Math.Round(11 * k);
      for (int i = 0; i < 40; i++)
      {
        var c = SiteAnalysis.Colour(i / 39.0);
        d.Draw2dRectangle(new Rectangle(x0 + i * w / 40, y0, w / 40 + 1, h), c, 0, c);
      }
      Text(d, "0 %", grey, x0, y0 + 24 * k, label, Latin, true);
      Text(d, "slope", grey, x0 + w / 2.0, y0 + 24 * k, label, Latin, true);
      Text(d, $"≥ {SiteAnalysis.MaxSlope * 100:F0} %", grey, x0 + w, y0 + 24 * k, label, Latin, true);
    }
  }
}
