using System;
using Moyu.Components.Section;
using Moyu.Games.Common.Core;
using Moyu.Games.Swift;
using Moyu.Games.Swift.Core;
using Rhino.Geometry;

namespace MoyuRec
{
  /// <summary>
  /// Swift flown by the repo's own test pilot (tests/Moyu.Core.Tests/TestsSwift.cs), a few walls into the run at frame 0;
  /// the boss key at `boss` s shows the house section, the key again at `back` s gives the real 3-2-1, then flight.
  /// args: seed, exposure (0 sheltered, 1 normal, 2 exposed), preroll, boss, back, tx / ty / dist (camera), bx / by /
  /// blens (boss camera: shift in course units, lens).
  /// </summary>
  public sealed class Swift : IShot
  {
    SwiftSession _s;
    SwiftGame G => _s.Game;
    double _boss, _back;
    bool _bossOn, _backOn;

    public int Frames => 60 * 17;

    public void Setup(Rec rec)
    {
      var settings = new SwiftSettings { Exposure = (Exposure)(int)rec.Arg("exposure", 1) };
      _s = SwiftSession.Start(settings, (int)rec.Arg("seed", 4), AirflowStudyComponent.DefaultPlane, AirflowStudyComponent.DefaultSize(), null, null, out string error);
      if (_s == null) throw new InvalidOperationException("Swift did not start: " + error);
      rec.Adopt(_s);
      _s.Language = rec.Arg("lang", "zh") == "en" ? UiLanguage.English : UiLanguage.Chinese;
      rec.SetCountdown(0);
      rec.SSet("PlayTime", 100.0);
      _boss = rec.Arg("boss", 5.0);
      _back = rec.Arg("back", 9.0);
      Camera(rec);
      // Take off, then fly until frame 0 is a few walls into the run.
      double preroll = rec.Arg("preroll", 7.0);
      for (double t = 0; t < preroll; t += rec.Dt)
      {
        if (t > 0.35 && G.State == SwiftState.Ready) G.Flap();
        Fly();
        rec.Step();
      }
      rec.Note($"after pre-roll: state {G.State} score {G.Score} level {G.Level} speed {G.Speed:F2} y {G.Y:F2}");
    }

    void Camera(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      var target = _s.At(rec.Arg("tx", 10.5), rec.Arg("ty", 6.4));
      var dir = _s.Plane.ZAxis * 40 + _s.Plane.XAxis * -3 + _s.Plane.YAxis * 5.7;
      dir.Unitize();
      vp.SetCameraLocations(target, target + dir * rec.Arg("dist", 30) * _s.Size);
      vp.CameraUp = _s.Plane.YAxis;
      vp.Camera35mmLensLength = 40;
    }

    void BossCamera(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      var shift = (_s.Plane.XAxis * rec.Arg("bx", 0.0) + _s.Plane.YAxis * rec.Arg("by", 0.0)) * _s.Size;
      vp.SetCameraLocations(vp.CameraTarget + shift, vp.CameraLocation + shift);
      vp.Camera35mmLensLength = rec.Arg("blens", 40);
    }

    /// <summary>The test suite's pilot: aim low in the next opening, flap when below the aim and falling.</summary>
    void Fly()
    {
      var g = G;
      if (g.State != SwiftState.Flying) return;
      double target = 6;
      var w = g.NextWall;
      if (w != null)
      {
        double enter = Math.Max(0, (w.X - SwiftGame.Radius - g.BirdCourseX) / g.Speed);
        double leave = Math.Max(0, (w.Right + SwiftGame.Radius - g.BirdCourseX) / g.Speed);
        double sill = double.MinValue, head = double.MaxValue;
        for (double t = enter; t <= leave + 1e-9; t += 0.02)
        {
          double c = w.CentreAt(g.Elapsed + t);
          sill = Math.Max(sill, c - w.Gap / 2);
          head = Math.Min(head, c + w.Gap / 2);
        }
        double lo = sill + SwiftGame.Radius + 0.15, hi = head - SwiftGame.Radius - 0.15, climb = 1.5;
        target = lo + climb < hi ? lo + Math.Min(0.3, (hi - lo - climb) / 2) : (lo + hi - climb) / 2;
      }
      bool flap = g.Y < target - 1.2 ? g.Vy < 3 : g.Y < target && g.Vy <= 0;
      if (flap) g.Flap();
    }

    public void Frame(Rec rec, int i)
    {
      double t = i * rec.Dt;
      if (!_bossOn && _boss >= 0 && t >= _boss - 1e-9) { _bossOn = true; _s.ToggleBoss(); BossCamera(rec); }
      if (_bossOn && !_backOn && _back >= 0 && t >= _back - 1e-9) { _backOn = true; _s.ToggleBoss(); }
      if (!_s.Boss && _s.Countdown <= 0) Fly();
      rec.Step();
      if (i % 60 == 0) rec.Note($"t {t:F1} state {G.State} score {G.Score} y {G.Y:F2} countdown {_s.Countdown:F2}");
    }

    public void Teardown(Rec rec) => _s?.Stop();
  }
}
