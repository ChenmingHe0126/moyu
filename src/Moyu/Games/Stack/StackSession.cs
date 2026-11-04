using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Moyu.Games.Common;
using Moyu.Games.Stack.Core;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Moyu.Games.Stack
{
  /// <summary>
  /// One VoxStack game: the falling-block rules, screen-relative moves with key repeat, view turns, and the
  /// massing-study disguise. Boss key, pause, countdown and camera handling come from <see cref="GameSession"/>.
  /// </summary>
  public sealed class StackSession : GameSession
  {
    const double Das = 0.17, Arr = 0.05, TurnSeconds = 0.15;
    const string ScoreFile = "voxstack";

    public StackGame Game { get; private set; }
    public readonly StackSettings Settings;
    public readonly Plane Plane;
    public readonly double VoxelSize;
    public readonly Transform GridToWorld;
    public readonly string ScoreKey;
    /// <summary>The rotation rings beside the well: drag one to turn the piece about that axis.</summary>
    public readonly Gumball Gumball;
    public long Best { get; private set; }
    public bool NewBest { get; private set; }
    public MassingDisguise Disguise { get; private set; }

    readonly Dictionary<Keys, (Int3 dir, double next)> _moves = new Dictionary<Keys, (Int3, double)>();
    ViewportInfo _beforeTopView;
    double _turnLeft;
    int _disguiseVersion = -1;

    StackSession(StackSettings settings, Plane plane, double voxelSize, object owner, Action<GameSession> changed, RhinoDoc doc, RhinoView view)
      : base(owner, changed, doc, view)
    {
      Settings = settings;
      Plane = plane;
      VoxelSize = voxelSize;
      GridToWorld = Transform.PlaneToPlane(Plane.WorldXY, plane) * Transform.Scale(Point3d.Origin, voxelSize);
      ScoreKey = $"{settings.Mode}-{settings.Nx}x{settings.Ny}x{settings.Nz}";
      Best = GameScores.Best(ScoreFile, ScoreKey);
      Game = new StackGame(settings);
      Gumball = new Gumball(this);
    }

    /// <summary>Starts a game in the active viewport. Returns null and an error when it cannot.</summary>
    public static StackSession Start(StackSettings settings, Plane plane, double voxelSize, object owner, Action<GameSession> changed, out string error)
    {
      var view = PickView(out var doc, out error);
      if (view == null) return null;
      StopCurrent();
      var session = new StackSession(settings, plane, voxelSize, owner, changed, doc, view);
      return session.Begin(new StackConduit(session), out error) ? session : null;
    }

    protected override string GameName => "VoxStack";
    protected override Vector3d Up => Plane.ZAxis;
    protected override bool IsOver => Game.IsOver;
    protected override bool IsGamePaused => Game.State == StackState.Paused;
    protected override bool IsGameActive => Game.State == StackState.Playing || Game.State == StackState.Clearing;
    protected override void PauseGame() => Game.Pause();
    protected override void ResumeGame() => Game.Resume();
    protected override bool NeedsRedraw => base.NeedsRedraw || _turnLeft != 0;

    public Point3d WellPoint(double x, double y, double z)
    {
      var p = new Point3d(x, y, z);
      p.Transform(GridToWorld);
      return p;
    }

    protected override void SetStartCamera(RhinoViewport vp)
    {
      var s = Settings;
      var target = WellPoint(s.Nx / 2.0, s.Ny / 2.0, s.Nz * 0.42);
      double k = s.Nz / 12.0 * VoxelSize * Math.Max(1.0, Math.Max(s.Nx, s.Ny) / 5.0);
      var offset = Plane.XAxis * (14 * k) + Plane.YAxis * (-17 * k) + Plane.ZAxis * (17.5 * k);
      vp.SetCameraLocations(target, target + offset);
      vp.CameraUp = Plane.ZAxis;
      vp.Camera35mmLensLength = 28;
    }

    protected override void TickAlways(double dt)
    {
      if (_turnLeft != 0) AnimateTurn(dt);
    }

    protected override void TickGame(double dt)
    {
      RepeatMoves();
      Game.Tick(dt);
      KeepCameraAbove();
    }

    protected override void NewGame()
    {
      Settings.Seed = System.Environment.TickCount;
      Game = new StackGame(Settings);
      Disguise = null;
      _disguiseVersion = -1;
      NewBest = false;
      Best = GameScores.Best(ScoreFile, ScoreKey);
    }

    /// <summary>The start camera also ends a Z / X turn in progress and forgets the camera the V top view would go back to.</summary>
    protected override void ResetCamera()
    {
      _turnLeft = 0;
      _beforeTopView = null;
      base.ResetCamera();
    }

    protected override void OnGameOver()
    {
      NewBest = GameScores.Record(ScoreFile, ScoreKey, Game.Score);
      Best = Math.Max(Best, Game.Score);
    }

    protected override void ClearInput()
    {
      _moves.Clear();
      Game.SoftDrop = false;
      _turnLeft = 0;
      Gumball.End();
    }

    // ---------------------------------------------------------------- mouse

    /// <summary>The gumball: hovering lights a ring, dragging it turns the piece; a press anywhere else orbits as usual.</summary>
    protected override bool GameMouse(MouseAction action, System.Drawing.Point client, Rhino.UI.MouseButton button)
    {
      switch (action)
      {
        case MouseAction.Hover:
          if (Gumball.Hover(Viewport, client)) Redraw();
          return false;
        case MouseAction.Down:
          if (button != Rhino.UI.MouseButton.Left) return false;
          // Paused or counting down: a press on the gumball does nothing (rather than swinging the camera).
          if (IsGamePaused) return Gumball.Covers(Viewport, client);
          if (!Gumball.Begin(Viewport, client)) return false;
          Redraw();
          return true;
        case MouseAction.Drag:
          Gumball.Drag(client);
          Redraw();
          return true;
        default:
          Gumball.End();
          Redraw();
          return true;
      }
    }

    // ---------------------------------------------------------------- keyboard

    protected override void GameKey(Keys key, bool down, bool repeat)
    {
      if (key == Keys.ShiftKey)
      {
        Game.SoftDrop = down;
        return;
      }
      if (!down)
      {
        _moves.Remove(key);
        return;
      }
      if (repeat) return;
      Axes(out var forward, out var right);
      switch (key)
      {
        case Keys.Up: StartMove(key, forward); break;
        case Keys.Down: StartMove(key, -forward); break;
        case Keys.Right: StartMove(key, right); break;
        case Keys.Left: StartMove(key, -right); break;
        case Keys.Q: Game.Rotate(Int3.UnitZ, true); break;
        case Keys.E: Game.Rotate(Int3.UnitZ, false); break;
        // W tips the top away from you, S towards you; A tips it left, D right.
        case Keys.W: Game.Rotate(right, Int3.Cross(right, Int3.UnitZ) == forward); break;
        case Keys.S: Game.Rotate(right, Int3.Cross(right, Int3.UnitZ) != forward); break;
        case Keys.A: Game.Rotate(forward, Int3.Cross(forward, Int3.UnitZ) == -right); break;
        case Keys.D: Game.Rotate(forward, Int3.Cross(forward, Int3.UnitZ) != -right); break;
        case Keys.Space: Game.HardDrop(); break;
        case Keys.C: Game.Hold(); break;
        case Keys.Z: _turnLeft += Math.PI / 2; break;
        case Keys.X: _turnLeft -= Math.PI / 2; break;
        case Keys.V: ToggleTopView(); break;
      }
    }

    void StartMove(Keys key, Int3 dir)
    {
      Game.Move(dir);
      // The direction is fixed while the key is held, even if the view swings past 45° meanwhile.
      _moves[key] = (dir, Time + Das);
    }

    void RepeatMoves()
    {
      foreach (var key in _moves.Keys.ToList())
      {
        var (dir, next) = _moves[key];
        if (!Held.Contains(key)) _moves.Remove(key);
        else if (Time >= next)
        {
          Game.Move(dir);
          _moves[key] = (dir, Time + Arr);
        }
      }
    }

    /// <summary>Screen "away" and "right" snapped to the well's grid axes.</summary>
    public void Axes(out Int3 forward, out Int3 right)
    {
      var vp = Viewport;
      var dir = vp.CameraDirection;
      double lx = dir * Plane.XAxis, ly = dir * Plane.YAxis;
      if (Math.Abs(lx) + Math.Abs(ly) < 1e-6)
      {
        var up = vp.CameraUp;
        lx = up * Plane.XAxis;
        ly = up * Plane.YAxis;
      }
      forward = Math.Abs(lx) >= Math.Abs(ly) ? new Int3(Math.Sign(lx), 0, 0) : new Int3(0, Math.Sign(ly), 0);
      if (forward == Int3.Zero) forward = Int3.UnitY;
      right = Int3.Cross(forward, Int3.UnitZ);
    }

    // ---------------------------------------------------------------- camera

    public override void Orbit(int dx, int dy)
    {
      _beforeTopView = null;
      base.Orbit(dx, dy);
    }

    void AnimateTurn(double dt)
    {
      double step = Math.Sign(_turnLeft) * Math.Min(Math.Abs(_turnLeft), Math.PI / 2 * dt / TurnSeconds);
      _turnLeft -= step;
      if (Math.Abs(_turnLeft) < 1e-9) _turnLeft = 0;
      var vp = Viewport;
      var target = vp.CameraTarget;
      var v = vp.CameraLocation - target;
      v.Transform(Transform.Rotation(step, Plane.ZAxis, Point3d.Origin));
      vp.SetCameraLocations(target, target + v);
      vp.CameraUp = Plane.ZAxis;
    }

    void ToggleTopView()
    {
      var vp = Viewport;
      if (_beforeTopView != null)
      {
        vp.SetViewProjection(_beforeTopView, true);
        _beforeTopView = null;
        return;
      }
      _beforeTopView = new ViewportInfo(vp);
      var target = WellPoint(Settings.Nx / 2.0, Settings.Ny / 2.0, 0);
      double dist = (vp.CameraLocation - vp.CameraTarget).Length;
      Axes(out var f, out _);
      var forward = Plane.XAxis * f.X + Plane.YAxis * f.Y;
      vp.SetCameraLocations(target, target + Plane.ZAxis * dist - forward * dist * 1e-3);
      vp.CameraUp = forward;
    }

    // ---------------------------------------------------------------- boss mode

    protected override string EnterDisguise(RhinoViewport vp)
    {
      if (Disguise == null || _disguiseVersion != Game.Well.Version)
      {
        Disguise = new MassingDisguise(Game.Well.Cells().Select(c => c.cell), Settings.Nx, Settings.Ny, Settings.Nz, Plane, VoxelSize, Settings.Seed);
        _disguiseVersion = Game.Well.Version;
      }
      Disguise.CameraFor(Settings.Nx, Settings.Ny, out var location, out var target);
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Plane.ZAxis;
      vp.Camera35mmLensLength = 50;
      var m = Disguise.Metrics;
      return $"MassingStudy: Option B updated · {m.Floors}F · GFA {m.Gfa:N0} m² · FAR {m.Far:F2}";
    }
  }
}
