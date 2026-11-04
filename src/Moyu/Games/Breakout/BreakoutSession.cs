using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Moyu.Games.Breakout.Core;
using Moyu.Games.Common;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.UI;

namespace Moyu.Games.Breakout
{
  /// <summary>
  /// One Rebound game on a facade plane: arrow keys or the mouse steer the paddle, space launches, and the boss key
  /// turns the wall into a facade elevation study. Boss key, pause, countdown and the user's view come from
  /// <see cref="GameSession"/>.
  /// </summary>
  public sealed class BreakoutSession : GameSession
  {
    const string ScoreFile = "rebound";

    public BreakoutGame Game { get; private set; }
    public readonly BreakoutSettings Settings;
    /// <summary>The facade plane: X along the wall, Y up, Z towards the viewer.</summary>
    public readonly Plane Plane;
    /// <summary>Model units per field unit.</summary>
    public readonly double Size;
    public readonly string ScoreKey;
    public readonly double ToMetres;
    public long Best { get; private set; }
    public bool NewBest { get; private set; }
    public FacadeDisguise Disguise { get; private set; }

    BreakoutSession(BreakoutSettings settings, Plane plane, double size, object owner, Action<GameSession> changed, RhinoDoc doc, RhinoView view)
      : base(owner, changed, doc, view)
    {
      Settings = settings;
      Plane = plane;
      Size = size;
      ToMetres = RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);
      ScoreKey = $"{settings.Mode}-{(settings.Custom != null ? "custom" : settings.Bond?.ToString() ?? "bonds")}";
      Best = GameScores.Best(ScoreFile, ScoreKey);
      Game = new BreakoutGame(settings);
    }

    /// <summary>Starts a game in the active viewport. Returns null and an error when it cannot.</summary>
    public static BreakoutSession Start(BreakoutSettings settings, Plane plane, double size, object owner, Action<GameSession> changed, out string error)
    {
      var view = PickView(out var doc, out error);
      if (view == null) return null;
      StopCurrent();
      var session = new BreakoutSession(settings, plane, size, owner, changed, doc, view);
      return session.Begin(new BreakoutConduit(session), out error) ? session : null;
    }

    protected override string GameName => "Rebound";
    protected override Vector3d Up => Plane.YAxis;
    protected override bool IsOver => Game.IsOver;
    protected override bool IsGamePaused => Game.State == BreakoutState.Paused;
    protected override bool IsGameActive => Game.State == BreakoutState.Serving || Game.State == BreakoutState.Playing || Game.State == BreakoutState.Cleared;
    protected override void PauseGame() => Game.Pause();
    protected override void ResumeGame() => Game.Resume();
    protected override bool NeedsRedraw => base.NeedsRedraw || Game.State == BreakoutState.Cleared;

    /// <summary>A point on the facade: field units along the wall (x), up (y) and towards the viewer (z).</summary>
    public Point3d At(double x, double y, double z = 0) => Plane.Origin + (Plane.XAxis * x + Plane.YAxis * y + Plane.ZAxis * z) * Size;

    protected override void TickGame(double dt)
    {
      Game.Tick(dt);
      KeepCameraAbove();
    }

    protected override void NewGame()
    {
      Settings.Seed = System.Environment.TickCount;
      Game = new BreakoutGame(Settings);
      NewBest = false;
      Best = GameScores.Best(ScoreFile, ScoreKey);
    }

    protected override void OnGameOver()
    {
      NewBest = GameScores.Record(ScoreFile, ScoreKey, Game.Score);
      Best = Math.Max(Best, Game.Score);
    }

    protected override void ClearInput() => Game.Steer = 0;

    // ---------------------------------------------------------------- input

    protected override void GameKey(Keys key, bool down, bool repeat)
    {
      switch (key)
      {
        case Keys.Left:
        case Keys.Right:
        case Keys.A:
        case Keys.D:
          Game.TargetX = double.NaN;
          bool left = Held.Contains(Keys.Left) || Held.Contains(Keys.A), right = Held.Contains(Keys.Right) || Held.Contains(Keys.D);
          Game.Steer = (right ? 1 : 0) - (left ? 1 : 0);
          break;
        case Keys.Space:
          if (down && !repeat) Game.Launch();
          break;
      }
    }

    /// <summary>Moving the mouse over the viewport hands the paddle to the mouse until an arrow key is pressed.</summary>
    protected override bool GameMouse(MouseAction action, System.Drawing.Point client, MouseButton button)
    {
      if (action != MouseAction.Hover || Game.IsOver || IsGamePaused) return false;
      if (!ClientToPlane(client, Plane, out var p)) return false;
      Plane.ClosestParameter(p, out double u, out _);
      Game.TargetX = u / Size;
      Game.Steer = 0;
      return false;
    }

    // ---------------------------------------------------------------- camera

    protected override void SetStartCamera(RhinoViewport vp)
    {
      var target = At(Settings.Width / 2, Settings.Height / 2 - 0.5);
      var location = target + (Plane.ZAxis * 31 + Plane.XAxis * 5 + Plane.YAxis * 6) * Size;
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Plane.YAxis;
      vp.Camera35mmLensLength = 35;
    }

    // ---------------------------------------------------------------- boss mode

    protected override string EnterDisguise(RhinoViewport vp)
    {
      Disguise = CurrentDisguise();
      Disguise.CameraFor(out var location, out var target);
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Plane.YAxis;
      vp.Camera35mmLensLength = 100;
      var t = Disguise.Tower;
      return $"FacadeStudy: Elevation B updated · WWR {t.Wwr * 100:F0} % · {t.PanelCount} panels · U {t.UValue:F2} W/m²K";
    }

    /// <summary>The tower elevation of the wall as it stands (also for the component's outputs).</summary>
    public FacadeDisguise CurrentDisguise() => new FacadeDisguise(this, new TowerFacade(Game.Bricks, Settings.Width));

    /// <summary>Brick outlines on the facade plane (component output).</summary>
    public IEnumerable<Curve> BrickCurves(IEnumerable<Brick> bricks) =>
      bricks.Select(b => (Curve)new PolylineCurve(new[] { At(b.X0, b.Y0), At(b.X1, b.Y0), At(b.X1, b.Y1), At(b.X0, b.Y1), At(b.X0, b.Y0) }));
  }
}
