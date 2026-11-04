using System;
using System.Globalization;
using System.Windows.Forms;
using Moyu.Games.Common;
using Moyu.Games.Swift.Core;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.UI;

namespace Moyu.Games.Swift
{
  /// <summary>
  /// One Swift flight on a section plane: space, ↑, W or a left click flaps; the boss key turns the course into a
  /// house section with daylight and ventilation analysis. Boss key, pause, countdown and the user's view come from
  /// <see cref="GameSession"/>.
  /// </summary>
  public sealed class SwiftSession : GameSession
  {
    public const string ScoreFile = "swift";

    public SwiftGame Game { get; private set; }
    public readonly SwiftSettings Settings;
    /// <summary>A fixed seed from Storey Grid replays the same course; 0 draws a new one each flight.</summary>
    public readonly int FixedSeed;
    /// <summary>The section plane: X along the course, Y up, Z towards the viewer.</summary>
    public readonly Plane Plane;
    /// <summary>Model units per course unit.</summary>
    public readonly double Size;
    public readonly double ToMetres;
    public readonly string ScoreKey;
    public long Best { get; private set; }
    public bool NewBest { get; private set; }
    public SectionDisguise Disguise { get; private set; }

    SwiftSession(SwiftSettings settings, int fixedSeed, Plane plane, double size, object owner, Action<GameSession> changed, RhinoDoc doc, RhinoView view)
      : base(owner, changed, doc, view)
    {
      Settings = settings;
      FixedSeed = fixedSeed;
      Plane = plane;
      Size = size;
      ToMetres = RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);
      ScoreKey = ScoreKeyFor(settings);
      Best = GameScores.Best(ScoreFile, ScoreKey);
      Settings.Seed = fixedSeed != 0 ? fixedSeed : Environment.TickCount;
      Game = new SwiftGame(Settings);
    }

    /// <summary>The best-score key: a wider opening or bay is an easier course, so it keeps its own best.</summary>
    public static string ScoreKeyFor(SwiftSettings s) =>
      $"{s.Mode}-{s.Exposure}-o{s.Opening.ToString("F2", CultureInfo.InvariantCulture)}-b{s.Bay.ToString("F2", CultureInfo.InvariantCulture)}";

    /// <summary>Starts a flight in the active viewport. Returns null and an error when it cannot.</summary>
    public static SwiftSession Start(SwiftSettings settings, int fixedSeed, Plane plane, double size, object owner, Action<GameSession> changed, out string error)
    {
      var view = PickView(out var doc, out error);
      if (view == null) return null;
      StopCurrent();
      var session = new SwiftSession(settings, fixedSeed, plane, size, owner, changed, doc, view);
      return session.Begin(new SwiftConduit(session), out error) ? session : null;
    }

    protected override string GameName => "Swift";
    protected override Vector3d Up => Plane.YAxis;
    protected override bool IsOver => Game.IsOver;
    protected override bool IsGamePaused => Game.State == SwiftState.Paused;
    /// <summary>The fall after a wall hit is not active: a boss key then needs no countdown, the swift just lands.</summary>
    protected override bool IsGameActive => Game.State == SwiftState.Ready || Game.State == SwiftState.Flying;
    protected override void PauseGame() => Game.Pause();
    protected override void ResumeGame() => Game.Resume();

    /// <summary>Metres per course unit.</summary>
    public double Metres => Size * ToMetres;

    /// <summary>A point in view units: x across the screen (the swift flies at <see cref="SwiftGame.BirdX"/>), y up, z towards the viewer.</summary>
    public Point3d At(double x, double y, double z = 0) => Plane.Origin + (Plane.XAxis * x + Plane.YAxis * y + Plane.ZAxis * z) * Size;

    /// <summary>A point on the course: course x scrolls by as the swift flies.</summary>
    public Point3d AtCourse(double x, double y, double z = 0) => At(x - Game.Distance, y, z);

    protected override void TickGame(double dt)
    {
      Game.Tick(dt);
      KeepCameraAbove();
    }

    protected override void NewGame()
    {
      Settings.Seed = FixedSeed != 0 ? FixedSeed : Environment.TickCount;
      Game = new SwiftGame(Settings);
      NewBest = false;
      Best = GameScores.Best(ScoreFile, ScoreKey);
    }

    protected override void OnGameOver()
    {
      NewBest = GameScores.Record(ScoreFile, ScoreKey, Game.Score);
      Best = Math.Max(Best, Game.Score);
    }

    // ---------------------------------------------------------------- input

    protected override void GameKey(Keys key, bool down, bool repeat)
    {
      if (!down || repeat) return;
      if (key == Keys.Space || key == Keys.Up || key == Keys.W) Game.Flap();
    }

    /// <summary>A left click flaps too (and does not orbit); right-drag still turns the view.</summary>
    protected override bool GameMouse(MouseAction action, System.Drawing.Point client, MouseButton button)
    {
      if (button != MouseButton.Left) return false;
      if (action == MouseAction.Down) Game.Flap();
      return action != MouseAction.Hover;
    }

    // ---------------------------------------------------------------- camera

    protected override void SetStartCamera(RhinoViewport vp)
    {
      // The swift a quarter of the way in, the ceiling clear of the score block, the floor just above the key hints;
      // the camera just above the 8° elevation floor, so the first tick of play does not lift it.
      var target = At(14, 7.4);
      var location = target + (Plane.ZAxis * 40 + Plane.XAxis * -3 + Plane.YAxis * 5.7) * Size;
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Plane.YAxis;
      vp.Camera35mmLensLength = 40;
    }

    // ---------------------------------------------------------------- boss mode

    protected override string EnterDisguise(RhinoViewport vp)
    {
      Disguise = CurrentDisguise();
      Disguise.CameraFor(out var location, out var target);
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Plane.YAxis;
      vp.Camera35mmLensLength = 40;
      var m = Disguise.Model.Metrics;
      return $"AirflowStudy: Section A-A updated · cross {m.CrossAch:F1} ach · stack {m.StackAch:F1} ach · mean DF {m.MeanDf:F1} % · P1 {m.ProbeSpeed:F2} m/s";
    }

    /// <summary>The climate section of the flight as it stands (also for the component's outputs).</summary>
    public SectionDisguise CurrentDisguise() => new SectionDisguise(this, new SectionModel(SectionModel.FromGame(Game, Metres)));
  }
}
