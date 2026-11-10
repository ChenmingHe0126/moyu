using System;
using System.Linq;
using Moyu.Components.Facade;
using Moyu.Games.Breakout;
using Moyu.Games.Breakout.Core;
using Moyu.Games.Common.Core;
using Rhino;
using Rhino.Geometry;

namespace MoyuRec
{
  /// <summary>
  /// The opening shot: Rebound (brick breaker) in a multi-ball melee from frame 0, pierce at `pierce` s, the boss key at
  /// `boss` s, the tower elevation re-framed for portrait and held to the end.
  /// args: seed, level, preroll (s of play before frame 0), pierce, boss, back (s; negative = never), lens, ty (field
  /// units: camera target height), bx / by (metres: shift of the boss camera target), blens (boss lens).
  /// </summary>
  public sealed class Rebound : IShot
  {
    BreakoutSession _s;
    double _aim, _serveWait;
    int _returns;
    double _pierceAt, _bossAt, _backAt;
    bool _pierce, _boss, _back;

    public int Frames => 60 * 12;

    public void Setup(Rec rec)
    {
      var settings = new BreakoutSettings
      {
        Seed = (int)rec.Arg("seed", 11), Mode = BreakoutMode.Classic, StartLevel = (int)rec.Arg("level", 1),
      };
      _s = BreakoutSession.Start(settings, FacadeStudyComponent.DefaultPlane, FacadeStudyComponent.DefaultSize(), null, null, out string error);
      if (_s == null) throw new InvalidOperationException("Rebound did not start: " + error);
      rec.Adopt(_s);
      _s.Language = rec.Arg("lang", "zh") == "en" ? UiLanguage.English : UiLanguage.Chinese;
      rec.SetCountdown(0);
      // The key-hint row fades between 7 and 8.5 s of play time.
      rec.SSet("PlayTime", 9.0);
      Frame(rec);
      _pierceAt = rec.Arg("pierce", 1.5);
      _bossAt = rec.Arg("boss", 5.0);
      _backAt = rec.Arg("back", -1.0);

      // Pre-roll: serve, split the ball twice, play on until frame 0 is in the middle of the melee.
      var g = _s.Game;
      double preroll = rec.Arg("preroll", 3.2);
      bool split1 = false, split2 = false;
      for (double t = 0; t < preroll; t += rec.Dt)
      {
        if (!split1 && t > 0.7) { split1 = true; g.Apply(PowerKind.Multi); }
        if (!split2 && t > 1.3) { split2 = true; g.Apply(PowerKind.Multi); }
        Bot(_s.Game, rec.Dt);
        rec.Step();
      }
      rec.Note($"Rebound seed {settings.Seed}: after pre-roll {g.Balls.Count} balls, score {g.Score}, {g.Remaining}/{g.Total} bricks");
    }

    void Frame(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      var target = _s.At(_s.Settings.Width / 2, rec.Arg("ty", 8.0));
      var location = target + (_s.Plane.ZAxis * 31 + _s.Plane.XAxis * 5 + _s.Plane.YAxis * 6) * _s.Size;
      vp.SetCameraLocations(target, location);
      vp.CameraUp = _s.Plane.YAxis;
      vp.Camera35mmLensLength = rec.Arg("lens", 39);
    }

    /// <summary>The tower elevation is composed for a landscape view: move the camera so the sheet reads in portrait.</summary>
    void FrameBoss(Rec rec)
    {
      var vp = rec.View.ActiveViewport;
      double m = RhinoMath.UnitScale(UnitSystem.Meters, rec.Doc.ModelUnitSystem);
      var shift = (_s.Plane.XAxis * rec.Arg("bx", 10.0) + _s.Plane.YAxis * rec.Arg("by", 1.0)) * m;
      var target = vp.CameraTarget + shift;
      var location = vp.CameraLocation + shift;
      vp.SetCameraLocations(target, location);
      vp.CameraUp = _s.Plane.YAxis;
      vp.Camera35mmLensLength = rec.Arg("blens", 77);
    }

    public void Frame(Rec rec, int i)
    {
      double t = i * rec.Dt;
      var g = _s.Game;
      if (!_boss && _bossAt >= 0 && t >= _bossAt - 1e-9) { _boss = true; _s.ToggleBoss(); FrameBoss(rec); }
      if (_boss && !_back && _backAt >= 0 && t >= _backAt - 1e-9) { _back = true; _s.ToggleBoss(); }
      if (!_s.Boss && _s.Countdown <= 0)
      {
        if (!_pierce && _pierceAt >= 0 && t >= _pierceAt) { _pierce = true; g.Apply(PowerKind.Pierce); }
        Bot(g, rec.Dt);
      }
      rec.Step();
    }

    void Bot(BreakoutGame g, double dt)
    {
      double r = BreakoutGame.BallRadius, w = g.Settings.Width, half = g.PaddleWidthNow / 2;
      double top = BreakoutGame.PaddleY + BreakoutGame.PaddleHeight / 2 + r;
      if (g.State == BreakoutState.Serving || g.Balls.Any(b => b.Held))
      {
        _serveWait += dt;
        if (_serveWait > 0.35) { g.Launch(); _serveWait = 0; }
      }
      Ball urgent = null;
      double soonest = double.MaxValue, landing = g.PaddleX;
      foreach (var b in g.Balls)
      {
        if (b.Held || b.Vy >= 0) continue;
        double time = (b.Y - top) / -b.Vy;
        if (time < -0.02 || time >= soonest) continue;
        soonest = time;
        urgent = b;
        landing = Fold(b.X + b.Vx * time, r, w - r);
      }
      double target;
      if (urgent != null)
      {
        // Meet the ball a little off-centre, so returns fan out across the wall instead of going straight back up.
        target = landing - _aim * (half + r);
        if (soonest < dt * 1.5)
        {
          _returns++;
          _aim = 0.55 * Math.Sin(_returns * 2.4 + 0.7);
        }
      }
      else
      {
        var power = g.Powers.Where(p => p.Kind != PowerKind.Narrow).OrderBy(p => p.Y).FirstOrDefault();
        var low = g.Balls.Where(b => !b.Held).OrderBy(b => b.Y).FirstOrDefault();
        target = power != null ? power.X : low != null ? low.X : g.PaddleX;
      }
      // Ease towards far-off targets, snap when the ball is nearly there.
      double ease = soonest < 0.3 ? 1 : Math.Min(1, dt * 9);
      double current = double.IsNaN(g.TargetX) ? g.PaddleX : g.TargetX;
      g.TargetX = Math.Max(half, Math.Min(w - half, current + (target - current) * ease));
    }

    /// <summary>Reflects x into [lo, hi] as a ball bouncing between the side walls would.</summary>
    static double Fold(double x, double lo, double hi)
    {
      double span = hi - lo, u = (x - lo) % (2 * span);
      if (u < 0) u += 2 * span;
      return lo + (u > span ? 2 * span - u : u);
    }

    public void Teardown(Rec rec) => _s?.Stop();
  }
}
