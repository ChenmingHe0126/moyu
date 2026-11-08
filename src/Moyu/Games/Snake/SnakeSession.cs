using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Moyu.Games.Common;
using Moyu.Games.Snake.Core;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Moyu.Games.Snake
{
  /// <summary>
  /// One Serpentine game on a surface: screen-relative and snake-relative turns, sprint, a follow camera that rides
  /// over the surface (round the back of a torus too), and the circulation-study disguise. Boss key, pause,
  /// countdown and the user's view come from <see cref="GameSession"/>.
  /// </summary>
  public sealed class SnakeSession : GameSession
  {
    const string ScoreFile = "serpentine";
    static readonly Keys[] SprintKeys = { Keys.ShiftKey, Keys.Up, Keys.W };

    public SnakeGame Game { get; private set; }
    public readonly SnakeSettings Settings;
    public readonly SurfaceField Field;
    public readonly string ScoreKey;
    /// <summary>Model units → metres, for the disguise's figures.</summary>
    public readonly double ToMetres;
    public long Best { get; private set; }
    public bool NewBest { get; private set; }
    /// <summary>
    /// First person: the camera is locked behind the head and turns with it. Otherwise the view is free: drag to turn
    /// the model. Either way ← → steer relative to the snake itself, never to the screen.
    /// </summary>
    public bool Follow { get; private set; } = true;
    public CirculationDisguise Disguise { get; private set; }

    SiteAnalysis _site;
    Point3d _camTarget;
    Vector3d _camNormal, _camUp;
    double _tilt, _camDist;
    bool _camReady;

    SnakeSession(SurfaceField field, SnakeSettings settings, object owner, Action<GameSession> changed, RhinoDoc doc, RhinoView view)
      : base(owner, changed, doc, view)
    {
      Field = field;
      Settings = settings;
      ToMetres = RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);
      ScoreKey = $"{settings.Mode}-{field.Grid.Nu}x{field.Grid.Nv}";
      Best = GameScores.Best(ScoreFile, ScoreKey);
      Game = new SnakeGame(field.Grid, settings);
      // Over a closed form look down more steeply: the surface curves away, so there is nothing far ahead to see.
      _tilt = field.Closed ? 0.95 : 1.0;
    }

    /// <summary>Starts a game in the active viewport. Returns null and an error when it cannot.</summary>
    public static SnakeSession Start(SurfaceField field, SnakeSettings settings, object owner, Action<GameSession> changed, out string error)
    {
      var view = PickView(out var doc, out error);
      if (view == null) return null;
      StopCurrent();
      SnakeSession session;
      try { session = new SnakeSession(field, settings, owner, changed, doc, view); }
      catch (ArgumentException ex)
      {
        error = ex.Message;
        return null;
      }
      return session.Begin(new SnakeConduit(session), out error) ? session : null;
    }

    protected override string GameName => "Serpentine";
    protected override bool IsOver => Game.IsOver;
    protected override bool IsGamePaused => Game.State == SnakeState.Paused;
    protected override bool IsGameActive => Game.State == SnakeState.Playing;
    protected override void PauseGame() => Game.Pause();
    protected override void ResumeGame() => Game.Resume();
    protected override bool ClampElevation => !Field.Closed;

    protected override void TickGame(double dt)
    {
      Game.Tick(dt);
      if (!Follow && !Field.Closed) KeepCameraAbove();
    }

    protected override void TickAlways(double dt)
    {
      if (Follow) FollowCamera(dt);
    }

    protected override void NewGame()
    {
      Settings.Seed = System.Environment.TickCount;
      Game = new SnakeGame(Field.Grid, Settings);
      NewBest = false;
      Best = GameScores.Best(ScoreFile, ScoreKey);
      _camReady = false;
    }

    protected override void OnGameOver()
    {
      NewBest = GameScores.Record(ScoreFile, ScoreKey, Game.Score);
      Best = Math.Max(Best, Game.Score);
    }

    protected override void ClearInput()
    {
      Game.Boost = false;
      // A sprint key held into a pause counts again only when pressed again: a held Shift must not turn the Esc that
      // resumes into Shift+Esc (quit), and a release missed while away must not leave the sprint on.
      foreach (var k in SprintKeys) Held.Remove(k);
    }

    // ---------------------------------------------------------------- where things are drawn

    /// <summary>0 → 1 from the previous cell to the current one. The picture runs one step behind the rules so it can glide.</summary>
    public double Progress => Game.IsOver || !Game.Moved ? 1 : Game.StepProgress;

    /// <summary>Where segment <paramref name="index"/> is drawn, <paramref name="lift"/> above the surface.</summary>
    public Point3d SegmentAt(IReadOnlyList<Cell> body, int index, double lift, out Vector3d normal)
    {
      var cell = body[index];
      var from = index + 1 < body.Count ? body[index + 1] : Game.RemovedTail ?? cell;
      double p = Progress;
      var a = Field.Lifted(from, lift);
      var b = Field.Lifted(cell, lift);
      normal = Field.Normal(from) * (1 - p) + Field.Normal(cell) * p;
      if (!normal.Unitize()) normal = Field.Normal(cell);
      return a + (b - a) * p;
    }

    /// <summary>The way the head is facing as drawn.</summary>
    public Vector3d HeadForward(IReadOnlyList<Cell> body)
    {
      var h = Game.Heading;
      var f = Field.Direction(body[0], h.du, h.dv);
      if (body.Count > 1 && Progress < 1)
      {
        var moving = Field.Centre(body[0]) - Field.Centre(body[1]);
        // A step across a seam is still a short hop in 3D; anything longer is not a real step.
        if (moving.Unitize() && Field.Centre(body[0]).DistanceTo(Field.Centre(body[1])) < Field.Step * 3) f = moving;
      }
      return f;
    }

    // ---------------------------------------------------------------- keyboard

    protected override void GameKey(Keys key, bool down, bool repeat)
    {
      // Shift, ↑ or W sprint while any of them is still held: letting go of one keeps the sprint if another is down
      // (Held already has this press or release).
      if (Array.IndexOf(SprintKeys, key) >= 0)
      {
        Game.Boost = SprintKeys.Any(k => Held.Contains(k));
        return;
      }
      if (!down || repeat) return;
      switch (key)
      {
        case Keys.Left:
        case Keys.A:
          SideTurn(true);
          break;
        case Keys.Right:
        case Keys.D:
          SideTurn(false);
          break;
        case Keys.C:
          ToggleCamera();
          break;
      }
    }

    /// <summary>Left or right of the snake's own heading, judged on the outward side of the surface.</summary>
    void SideTurn(bool left)
    {
      var h = Game.PlannedHeading;
      var head = Game.Head;
      var leftDir = Vector3d.CrossProduct(Field.Normal(head), Field.Direction(head, h.du, h.dv));
      var a = (du: -h.dv, dv: h.du);
      bool aIsLeft = Field.Direction(head, a.du, a.dv) * leftDir >= 0;
      var turn = aIsLeft == left ? a : (du: h.dv, dv: -h.du);
      Game.Turn(turn.du, turn.dv);
    }

    // ---------------------------------------------------------------- camera

    void ToggleCamera()
    {
      Follow = !Follow;
      if (Follow) _camReady = false;
      else
      {
        Overview(Viewport);
        Redraw();
      }
    }

    protected override void SetStartCamera(RhinoViewport vp)
    {
      vp.Camera35mmLensLength = 35;
      if (Follow)
      {
        _camReady = false;
        FollowCamera(0);
      }
      else Overview(vp);
    }

    void Overview(RhinoViewport vp)
    {
      CirculationDisguise.Overview(Field, 1.05, out var location, out var target);
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Vector3d.ZAxis;
    }

    /// <summary>
    /// First person: rides behind and above the head, and swings round with it on every turn (over a fifth of a
    /// second or so), so ahead is always up the screen and ← → always mean the snake's own left and right.
    /// </summary>
    void FollowCamera(double dt)
    {
      var body = Game.Body.ToList();
      var target = SegmentAt(body, 0, Field.Step * 0.4, out var normal);
      // On terrain lean the camera towards world up, so bumps do not rock it (not on a wall-like site, which it faces).
      if (Field.Upright)
      {
        normal = normal + Vector3d.ZAxis * 1.5;
        normal.Unitize();
      }
      if (!_camReady)
      {
        _camTarget = target;
        _camNormal = normal;
        _camUp = HeadForward(body);
        _camDist = 0;
        _camReady = true;
      }
      else
      {
        double k = 1 - Math.Exp(-dt * 7);
        _camTarget += (target - _camTarget) * k;
        var n = _camNormal + (normal - _camNormal) * (1 - Math.Exp(-dt * 4));
        if (n.Unitize()) _camNormal = n;
        // Turn with the snake: ease "up the screen" towards where the head is going.
        var ahead = HeadForward(body);
        ahead -= (ahead * _camNormal) * _camNormal;
        if (ahead.Unitize())
        {
          var blend = _camUp + (ahead - _camUp) * (1 - Math.Exp(-dt * 9));
          _camUp = blend.Unitize() ? blend : ahead;
        }
      }
      var up = _camUp - (_camUp * _camNormal) * _camNormal;
      if (!up.Unitize())
      {
        up = HeadForward(body);
        up = up - (up * _camNormal) * _camNormal;
        if (!up.Unitize()) up = Vector3d.CrossProduct(_camNormal, Vector3d.XAxis);
        up.Unitize();
      }
      _camUp = up;
      ApplyFollow(dt);
    }

    void ApplyFollow(double dt)
    {
      double want = Field.Step * (Field.Closed ? 11 : 14);
      var dir = _camNormal * Math.Cos(_tilt) - _camUp * Math.Sin(_tilt);
      dir.Unitize();
      // Like a third-person camera, never end up behind a fold of the surface (across a torus's hole, behind a hill).
      double hit = Intersection.MeshRay(Field.Mesh, new Ray3d(_camTarget + dir * (Field.Step * 0.5), dir));
      if (hit >= 0) want = Math.Min(want, Math.Max(Field.Step * 3, (hit + Field.Step * 0.5) * 0.8));
      if (_camDist <= 0 || dt <= 0) _camDist = want;
      else _camDist += (want - _camDist) * (1 - Math.Exp(-dt * (want < _camDist ? 12 : 2.5)));
      var location = _camTarget + dir * _camDist;
      // On an open site look a little ahead, so the snake sits in the lower half with room to see where it is going.
      var look = _camTarget + _camUp * (Field.Step * (Field.Closed ? 2.5 : 5));
      var vp = Viewport;
      vp.SetCameraLocations(look, location);
      var camUp = Vector3d.CrossProduct(Vector3d.CrossProduct(look - location, _camUp), look - location);
      vp.CameraUp = camUp.Unitize() ? camUp : _camUp;
    }

    public override void Orbit(int dx, int dy)
    {
      // First person is locked behind the head; only the free view turns with the mouse.
      if (!Follow) base.Orbit(dx, dy);
    }

    // ---------------------------------------------------------------- boss mode

    protected override string EnterDisguise(RhinoViewport vp)
    {
      if (_site == null) _site = new SiteAnalysis(Field, Doc.ModelAbsoluteTolerance);
      Disguise = new CirculationDisguise(Field, _site, Game.Body, Game.Items.Select(i => i.Cell), Settings.Seed, ToMetres);
      CirculationDisguise.Overview(Field, 1.15, out var location, out var target);
      vp.SetCameraLocations(target, location);
      vp.CameraUp = Vector3d.ZAxis;
      vp.Camera35mmLensLength = 40;
      var m = Disguise.Metrics;
      return $"CirculationStudy: Route A updated · L = {m.Length:F1} m · avg slope {m.AverageSlope * 100:F1} %";
    }

    /// <summary>The disguise for the component's outputs, whether or not boss mode is on.</summary>
    public CirculationDisguise CurrentDisguise()
    {
      if (Boss && Disguise != null) return Disguise;
      if (_site == null) _site = new SiteAnalysis(Field, Doc.ModelAbsoluteTolerance);
      return new CirculationDisguise(Field, _site, Game.Body, Game.Items.Select(i => i.Cell), Settings.Seed, ToMetres);
    }
  }
}
