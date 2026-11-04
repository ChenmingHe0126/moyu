using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using Moyu.Games.Common;
using Moyu.Games.Stack.Core;
using Rhino.Display;
using Rhino.Geometry;
using Point = System.Drawing.Point;

namespace Moyu.Games.Stack
{
  /// <summary>
  /// A gumball pinned to the bottom-right corner of the view, as Rhino has round a selected object: three rings, X red,
  /// Y green, Z blue, round a small copy of the falling piece. It stays put on screen and keeps its size while the piece
  /// falls (only turning with the camera, so the rings still show the well's axes), so it is always under the hand. Drag
  /// a ring along itself: about 30° of drag turns the piece a quarter turn about that ring's axis (either way), and each
  /// further 90° another. Where rings cross on screen, the first few pixels of the drag decide which ring it is. Works in
  /// grid units (one voxel = 1).
  /// </summary>
  public sealed class Gumball
  {
    const int Samples = 72;
    const double HitPixels = 16, TurnAt = 0.55, DecidePixels = 6;
    /// <summary>The gumball is shown as if seen from 15°–28° above (the camera's own pitch, clamped), always from the camera's heading.</summary>
    const double LowestPitch = 0.26, HighestPitch = 0.49;
    public static readonly Int3[] Axes = { Int3.UnitX, Int3.UnitY, Int3.UnitZ };
    public static readonly Color[] Colours = { Color.FromArgb(226, 88, 88), Color.FromArgb(74, 170, 100), Color.FromArgb(74, 122, 224) };

    readonly StackSession _s;
    Point2d _grab;
    Vector2d _tangent;
    double _pixelsPerRadian, _scale = 1, _reach = HitPixels;
    int _applied;
    bool _decided;
    readonly List<(int axis, double t, Vector2d tangent, double perRadian, double dist)> _candidates = new List<(int, double, Vector2d, double, double)>();

    /// <summary>The ring under the mouse, or -1.</summary>
    public int Hot { get; private set; } = -1;
    /// <summary>The ring being dragged, or -1.</summary>
    public int Dragging { get; private set; } = -1;
    /// <summary>Where the drag took hold on the ring (its angle) and how far it has gone since, in radians.</summary>
    public double GrabAngle { get; private set; }
    public double DragAngle { get; private set; }
    /// <summary>The last quarter turn: which ring, and when (session time).</summary>
    public int LastAxis { get; private set; } = -1;
    public double LastTurnTime { get; private set; } = double.NegativeInfinity;

    public Gumball(StackSession session) => _s = session;

    /// <summary>The two in-plane directions of ring <paramref name="axis"/>, ordered so that u × v points along the axis.</summary>
    static (Vector3d u, Vector3d v) Frame(int axis) =>
      axis == 0 ? (Vector3d.YAxis, Vector3d.ZAxis) : axis == 1 ? (Vector3d.ZAxis, Vector3d.XAxis) : (Vector3d.XAxis, Vector3d.YAxis);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// The HUD scale (<see cref="GameConduit.UiScale"/>) from the view window's DPI, so the gumball is sized and
    /// hit-tested at the same scale the conduit draws the HUD; the hit test has no display pipeline to ask.
    /// </summary>
    public static double Scale(RhinoViewport vp, Size frame)
    {
      double dpi;
      try
      {
        var view = vp.ParentView;
        uint raw = view != null ? GetDpiForWindow(view.Handle) : 0;
        dpi = raw > 0 ? raw / 96.0 : 1;
      }
      catch (Exception)
      {
        dpi = 1;
      }
      return GameConduit.UiScale(dpi, frame);
    }

    /// <summary>
    /// Where the gumball sits in a frame of the view (bottom right, under the NEXT column, clear of the key hints) and its
    /// ring radius in pixels. The frame is the view's own size for the mouse, and the pipeline's frame when drawing (a
    /// view capture draws at its own size while the viewport keeps reporting the window's).
    /// </summary>
    public static (Point2d client, double pixels) Anchor(RhinoViewport vp, Size frame)
    {
      double s = Scale(vp, frame);
      double r = Math.Max(52 * s, Math.Min(80 * s, Math.Min(frame.Width, frame.Height) * 0.105));
      return (new Point2d(frame.Width - 60 * s - r * 1.25, frame.Height - 70 * s - r * 1.35), r);
    }

    /// <summary>
    /// Centre and ring radius in grid units: the point a little in front of the camera target that shows at the
    /// <see cref="Anchor"/>, and the radius that looks the anchor's size there.
    /// </summary>
    public (Point3d centre, double radius) Place(RhinoViewport vp, Size frame)
    {
      var (at, pixels) = Anchor(vp, frame);
      var ray = vp.ClientToWorld(at);
      // The ray runs far → near; aim from the camera towards whichever end is farther.
      var far = ray.From.DistanceTo(vp.CameraLocation) > ray.To.DistanceTo(vp.CameraLocation) ? ray.From : ray.To;
      var dir = far - vp.CameraLocation;
      if (!dir.Unitize()) dir = vp.CameraDirection;
      var world = vp.CameraLocation + dir * (vp.CameraLocation.DistanceTo(vp.CameraTarget) * 0.6);
      if (!vp.GetWorldToScreenScale(world, out double ppu) || ppu <= 0) ppu = 1;
      var unit = Vector3d.XAxis;
      unit.Transform(_s.GridToWorld);
      _s.GridToWorld.TryGetInverse(out var toGrid);
      world.Transform(toGrid);
      return (world, pixels / (ppu * Math.Max(1e-9, unit.Length)));
    }

    /// <summary>
    /// A turn of the gumball about the screen's horizontal axis through its centre, so that it reads as seen from at most
    /// 28° above whatever the camera's pitch. From the game's usual 45° the Y and Z rings would draw as the same ellipse
    /// (one over the other along their lower half); a little lower they separate clearly. The heading stays the
    /// camera's, so each ring still lies the way its axis runs in the well.
    /// </summary>
    public Transform Tilt(RhinoViewport vp, Point3d centre)
    {
      _s.GridToWorld.TryGetInverse(out var toGrid);
      // The line of sight to the gumball itself: in a perspective view, down in the corner it looks more steeply down
      // than the camera does.
      var world = centre;
      world.Transform(_s.GridToWorld);
      var dir = vp.IsPerspectiveProjection ? world - vp.CameraLocation : vp.CameraDirection;
      dir.Transform(toGrid);
      if (!dir.Unitize()) return Transform.Identity;
      var right = Vector3d.CrossProduct(dir, Vector3d.ZAxis);
      if (!right.Unitize())
      {
        right = vp.CameraX;
        right.Transform(toGrid);
        right.Z = 0;
        if (!right.Unitize()) return Transform.Identity;
      }
      double pitch = Math.Asin(Math.Max(-1, Math.Min(1, -dir.Z)));
      double shown = Math.Max(LowestPitch, Math.Min(HighestPitch, pitch));
      if (Math.Abs(shown - pitch) < 1e-6) return Transform.Identity;
      // Heading on the ground: forward is away from the eye for a line of sight whose right is `right`.
      var forward = Vector3d.CrossProduct(Vector3d.ZAxis, right);
      var seen = forward * Math.Cos(shown) - Vector3d.ZAxis * Math.Sin(shown);
      // Shown turned this way, the gumball looks to the real camera as it would to one looking along `seen`.
      return Transform.Rotation(seen, dir, centre);
    }

    /// <summary>A point on ring <paramref name="axis"/> at angle <paramref name="t"/>, in grid units.</summary>
    public static Point3d On(Point3d centre, int axis, double t, double radius)
    {
      var (u, v) = Frame(axis);
      return centre + (u * Math.Cos(t) + v * Math.Sin(t)) * radius;
    }

    public static Polyline Ring(Point3d centre, int axis, double radius, double from = 0, double sweep = 2 * Math.PI)
    {
      int n = Math.Max(2, (int)(Samples * Math.Abs(sweep) / (2 * Math.PI)) + 1);
      return new Polyline(Enumerable.Range(0, n + 1).Select(i => On(centre, axis, from + sweep * i / n, radius)));
    }

    /// <summary>
    /// Ring <paramref name="axis"/> split into the half nearer the eye and the half behind the centre (null when the ring
    /// faces the eye nearly square on, so it has no back).
    /// </summary>
    public static (Polyline front, Polyline back) Halves(Point3d centre, int axis, double radius, Point3d eye)
    {
      var (u, v) = Frame(axis);
      var e = eye - centre;
      double eu = e * u, ev = e * v;
      if (Math.Sqrt(eu * eu + ev * ev) < 0.15 * e.Length) return (Ring(centre, axis, radius), null);
      double mid = Math.Atan2(ev, eu);
      return (Ring(centre, axis, radius, mid - Math.PI / 2, Math.PI), Ring(centre, axis, radius, mid + Math.PI / 2, Math.PI));
    }

    Point2d Screen(RhinoViewport vp, Transform tilt, Point3d grid)
    {
      grid.Transform(tilt);
      grid.Transform(_s.GridToWorld);
      return vp.WorldToClient(grid);
    }

    /// <summary>Each ring that passes within reach of the pixel: the angle on it there and how far off it is.</summary>
    List<(int axis, double t, double dist)> Hits(RhinoViewport vp, Point2d pixel)
    {
      var (c, r) = Place(vp, vp.Size);
      var tilt = Tilt(vp, c);
      double reach = HitPixels * Scale(vp, vp.Size);
      var hits = new List<(int, double, double)>();
      for (int a = 0; a < 3; a++)
      {
        double bestD = reach, bestT = 0;
        var prev = Screen(vp, tilt, On(c, a, 0, r));
        for (int i = 1; i <= Samples; i++)
        {
          double t = i * 2 * Math.PI / Samples;
          var p = Screen(vp, tilt, On(c, a, t, r));
          var seg = p - prev;
          double len2 = seg.X * seg.X + seg.Y * seg.Y;
          double k = len2 > 1e-9 ? Math.Max(0, Math.Min(1, ((pixel.X - prev.X) * seg.X + (pixel.Y - prev.Y) * seg.Y) / len2)) : 0;
          double dx = prev.X + seg.X * k - pixel.X, dy = prev.Y + seg.Y * k - pixel.Y, dist = Math.Sqrt(dx * dx + dy * dy);
          if (dist < bestD)
          {
            bestD = dist;
            bestT = t - (1 - k) * 2 * Math.PI / Samples;
          }
          prev = p;
        }
        if (bestD < reach) hits.Add((a, bestT, bestD));
      }
      return hits.OrderBy(h => h.Item3).ToList();
    }

    /// <summary>True when the pixel is inside the gumball's circle on screen (a press there never orbits the camera).</summary>
    public static bool Covers(RhinoViewport vp, Point pixel)
    {
      var (at, r) = Anchor(vp, vp.Size);
      return (new Point2d(pixel.X, pixel.Y) - at).Length < r * 1.2;
    }

    /// <summary>Highlights the ring under the mouse; true when that changed (the view needs a redraw).</summary>
    public bool Hover(RhinoViewport vp, Point pixel)
    {
      if (Dragging >= 0) return false;
      var hits = _s.Game.Current != null ? Hits(vp, new Point2d(pixel.X, pixel.Y)) : null;
      int hot = hits != null && hits.Count > 0 ? hits[0].axis : -1;
      if (hot == Hot) return false;
      Hot = hot;
      return true;
    }

    /// <summary>
    /// Takes hold of the ring(s) under the mouse. False when the press is clear of the gumball (so it orbits as usual);
    /// a press inside its circle that misses every ring does nothing, rather than swinging the camera and the rings.
    /// </summary>
    public bool Begin(RhinoViewport vp, Point pixel)
    {
      if (_s.Game.Current == null) return false;
      var at = new Point2d(pixel.X, pixel.Y);
      var hits = Hits(vp, at);
      if (hits.Count == 0) return Covers(vp, pixel);
      var (c, radius) = Place(vp, vp.Size);
      var tilt = Tilt(vp, c);
      var cs = Screen(vp, tilt, c);
      _scale = Scale(vp, vp.Size);
      _reach = HitPixels * _scale;
      _candidates.Clear();
      foreach (var (axis, t, dist) in hits)
      {
        // How the ring runs on screen where it was grabbed: its direction, and how many pixels one radian covers there.
        const double step = 0.15;
        var p = Screen(vp, tilt, On(c, axis, t, radius));
        var tangent = Screen(vp, tilt, On(c, axis, t + step, radius)) - p;
        double local = tangent.Length / step;
        double widest = Enumerable.Range(0, 24).Max(i => (Screen(vp, tilt, On(c, axis, i * Math.PI / 12, radius)) - cs).Length);
        // Seen edge-on there the ring barely moves on screen: fall back to across the radius, and keep the drag from being twitchy.
        if (tangent.Length < 2)
        {
          var r = p - cs;
          tangent = new Vector2d(-r.Y, r.X);
        }
        if (!tangent.Unitize()) tangent = new Vector2d(1, 0);
        _candidates.Add((axis, t, tangent, Math.Max(Math.Max(18 * _scale, widest * 0.35), local), dist));
      }
      _grab = at;
      _applied = 0;
      _decided = _candidates.Count == 1;
      Take(_candidates[0]);
      return true;
    }

    void Take((int axis, double t, Vector2d tangent, double perRadian, double dist) c)
    {
      Dragging = c.axis;
      Hot = c.axis;
      GrabAngle = c.t;
      _tangent = c.tangent;
      _pixelsPerRadian = c.perRadian;
      DragAngle = 0;
    }

    /// <summary>Follows the drag: past about 30° of travel along the ring the piece turns a quarter, then again every 90°.</summary>
    public void Drag(Point pixel)
    {
      if (Dragging < 0) return;
      var d = new Vector2d(pixel.X - _grab.X, pixel.Y - _grab.Y);
      if (!_decided)
      {
        if (d.Length < DecidePixels * _scale) return;
        // Rings cross or run side by side here: the one running the way the hand moves is the one meant, and where two
        // run alike, the one the press was nearest (the one lit under the cursor).
        var dir = d;
        dir.Unitize();
        Take(_candidates.OrderByDescending(c => Math.Abs(dir * c.tangent) - 0.6 * c.dist / _reach).First());
        _decided = true;
      }
      DragAngle = (d * _tangent) / _pixelsPerRadian;
      // Quarter turns earned so far: one past ±TurnAt, another every 90° beyond. Comparing against a count (not the
      // last turn) keeps a turn from being undone at once when TurnAt is under 45°.
      int earned = TurnsFor(DragAngle);
      while (Dragging >= 0 && _applied < earned) Turn(true);
      while (Dragging >= 0 && _applied > earned) Turn(false);
    }

    /// <summary>How many quarter turns a drag of <paramref name="angle"/> radians along a ring makes (signed).</summary>
    public static int TurnsFor(double angle)
    {
      double a = Math.Abs(angle);
      int n = a <= TurnAt ? 0 : 1 + (int)Math.Floor((a - TurnAt) / (Math.PI / 2));
      return angle < 0 ? -n : n;
    }

    /// <summary>A refused turn (nothing fits, or the game is between pieces) lets go of the ring, so no later undo turns the piece the other way.</summary>
    void Turn(bool positive)
    {
      if (!_s.Game.Rotate(Axes[Dragging], positive))
      {
        End();
        return;
      }
      _applied += positive ? 1 : -1;
      LastAxis = Dragging;
      LastTurnTime = _s.Time;
    }

    public void End()
    {
      Dragging = -1;
      DragAngle = 0;
    }

    /// <summary>The keys that turn the piece about each ring's axis, as the view stands (Q E about Z; W S and A D about the screen's right and forward axes).</summary>
    public string[] KeyLabels()
    {
      _s.Axes(out var forward, out var right);
      var labels = new string[3];
      for (int a = 0; a < 3; a++)
        labels[a] = a == 2 ? "Q E" : Math.Abs(Axes[a].X * right.X + Axes[a].Y * right.Y) == 1 ? "W S" : "A D";
      return labels;
    }
  }
}
