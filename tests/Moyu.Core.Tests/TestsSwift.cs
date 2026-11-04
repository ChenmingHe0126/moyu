using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Games.Swift.Core;

namespace Moyu.Core.Tests
{
  /// <summary>Rules of the Swift flyer and its boss-key climate section (no Rhino needed).</summary>
  static class TestsSwift
  {
    const double Frame = 1.0 / 60;

    public static void Run(Action<bool, string> check)
    {
      Flight(check);
      Crashes(check);
      Course(check);
      Autopilot(check);
      Zen(check);
      PauseAndReplay(check);
      Section(check);
    }

    static SwiftGame New(int seed = 1, Exposure e = Exposure.Normal, SwiftMode mode = SwiftMode.Classic) =>
      new SwiftGame(new SwiftSettings { Seed = seed, Exposure = e, Mode = mode });

    /// <summary>Flaps, as a player would, whenever the swift sinks below a point just above the next opening's sill.</summary>
    static bool Pilot(SwiftGame g)
    {
      var w = g.NextWall;
      double target = SwiftGame.Height / 2;
      if (w != null)
      {
        // The band that stays open all the while the swift is inside the wall (a shutter moves meanwhile).
        double enter = Math.Max(0, (w.X - SwiftGame.Radius - g.BirdCourseX) / g.Speed), leave = Math.Max(0, (w.Right + SwiftGame.Radius - g.BirdCourseX) / g.Speed);
        double sill = double.MinValue, head = double.MaxValue;
        for (double t = enter; t <= leave + 1e-9; t += 0.02)
        {
          double c = w.CentreAt(g.Elapsed + t);
          sill = Math.Max(sill, c - w.Gap / 2);
          head = Math.Min(head, c + w.Gap / 2);
        }
        double lo = sill + SwiftGame.Radius + 0.15, hi = head - SwiftGame.Radius - 0.15;
        const double climb = SwiftGame.FlapSpeed * SwiftGame.FlapSpeed / (2 * SwiftGame.Gravity);
        target = lo + climb < hi ? lo + Math.Min(0.3, (hi - lo - climb) / 2) : (lo + hi - climb) / 2;
      }
      // Far below: tap quickly to climb; otherwise flap each time it starts to sink below the target.
      if (g.Y < target - 1.2) return g.Vy < 3;
      return g.Y < target && g.Vy <= 0;
    }

    static void Fly(SwiftGame g, double seconds, Func<SwiftGame, bool> pilot, List<Wall> seen = null)
    {
      for (double t = 0; t < seconds && !g.IsOver; t += Frame)
      {
        if (pilot != null && pilot(g)) g.Flap();
        g.Tick(Frame);
        if (seen != null)
          foreach (var w in g.Walls)
            if (seen.Count == 0 || w.Index > seen[seen.Count - 1].Index) seen.Add(w);
      }
    }

    static void Flight(Action<bool, string> check)
    {
      Console.WriteLine("Swift: flight");
      var g = New();
      g.Tick(1);
      check(g.State == SwiftState.Ready && g.Distance == 0 && Math.Abs(g.Y - SwiftGame.Height / 2) < 0.3, "waits, bobbing, until the first flap");
      g.Flap();
      check(g.State == SwiftState.Flying && g.Vy == SwiftGame.FlapSpeed && g.Flaps == 1, "the first flap takes off");
      double y0 = g.Y, top = g.Y;
      for (int i = 0; i < 30; i++)
      {
        g.Tick(Frame);
        top = Math.Max(top, g.Y);
      }
      double apex = SwiftGame.FlapSpeed * SwiftGame.FlapSpeed / (2 * SwiftGame.Gravity);
      check(Math.Abs(top - y0 - apex) < 0.05, $"one flap climbs {top - y0:F2} units (v²/2g = {apex:F2}), then falls");
      check(g.Distance > 0 && Math.Abs(g.Distance - g.Elapsed * g.Speed) < 1e-6, "the course scrolls at the course speed");
      for (int i = 0; i < 120; i++)
      {
        g.Flap();
        g.Tick(Frame);
      }
      check(g.Y <= SwiftGame.Height - SwiftGame.Radius + 1e-9 && g.State == SwiftState.Flying, "flapping against the ceiling just holds the swift under it");
    }

    static void Crashes(Action<bool, string> check)
    {
      Console.WriteLine("Swift: crashes");
      var g = New();
      g.Flap();
      Fly(g, 5, null);
      check(g.IsOver && !g.HitWall && g.Score == 0 && Math.Abs(g.Y - SwiftGame.Radius) < 1e-9, "never flapping again: down to the floor, flight over");

      // Hover well above the first opening and fly into the wall over it.
      g = New(3);
      g.Flap();
      var first = g.Walls[0];
      double high = Math.Min(SwiftGame.Height - 0.6, first.Centre + first.Gap / 2 + 1.2);
      bool crashed = false;
      for (double t = 0; t < 8 && !g.IsOver; t += Frame)
      {
        double next = g.Y + g.Vy * 0.15 - 0.5 * SwiftGame.Gravity * 0.0225;
        if (next < high && g.Vy < 1) g.Flap();
        g.Tick(Frame);
        crashed |= g.State == SwiftState.Crashed;
      }
      check(crashed && g.IsOver && g.HitWall && first.Hit && g.Score == 0, "hitting a wall knocks the swift down and ends the flight");
      check(g.BirdCourseX + SwiftGame.Radius >= first.X - 0.02 && g.BirdCourseX < first.Right, $"it stopped at the wall face (x {g.BirdCourseX:F2}, wall {first.X:F2})");
    }

    static void Course(Action<bool, string> check)
    {
      Console.WriteLine("Swift: the course");
      var g = New(7);
      check(Enumerable.Range(0, 60).All(i => g.GapAt(i + 1) <= g.GapAt(i) && g.SpeedAt(i + 1) >= g.SpeedAt(i) && g.PitchAt(i) / g.SpeedAt(i) >= g.PitchAt(i + 1) / g.SpeedAt(i + 1) - 1e-9),
        $"openings shrink ({g.GapAt(0):F1} → {g.GapAt(60):F1}), speed rises ({g.SpeedAt(0):F1} → {g.SpeedAt(60):F1}), walls come quicker ({g.PitchAt(0) / g.SpeedAt(0):F2} → {g.PitchAt(60) / g.SpeedAt(60):F2} s apart)");
      var easy = New(7, Exposure.Sheltered);
      var hard = New(7, Exposure.Exposed);
      check(easy.GapAt(0) > g.GapAt(0) && g.GapAt(0) > hard.GapAt(0) && easy.SpeedAt(0) < hard.SpeedAt(0), "sheltered is roomier and slower than exposed");
      var wide = new SwiftGame(new SwiftSettings { Seed = 7, Opening = 1.2, Bay = 1.3 });
      check(Math.Abs(wide.GapAt(0) - g.GapAt(0) * 1.2) < 1e-9 && Math.Abs(wide.PitchAt(0) - g.PitchAt(0) * 1.3) < 1e-9, "Storey Grid's opening and bay factors scale the course");
      var nan = new SwiftGame(new SwiftSettings { Seed = 7, Opening = double.NaN, Bay = 0.0 / 0 });
      check(nan.Settings.Opening == 1 && nan.Settings.Bay == 1 && nan.Walls.Count == g.Walls.Count && nan.Walls.All(w => double.IsFinite(w.X) && double.IsFinite(w.Centre)),
        $"NaN factors fall back to the standard course ({nan.Walls.Count} walls, all finite)");

      // Long autopiloted flights in every exposure, collecting every wall generated on the way.
      var seen = new List<Wall>();
      foreach (var e in new[] { Exposure.Sheltered, Exposure.Normal, Exposure.Exposed })
      {
        var run = New(11, e, SwiftMode.Zen);
        run.Flap();
        var walls = new List<Wall>();
        Fly(run, 240, Pilot, walls);
        seen.AddRange(walls);
        bool reachable = walls.Zip(walls.Skip(1), (a, b) => Math.Abs(b.Centre - a.Centre) + a.Swing + b.Swing <= SwiftGame.MaxShiftAt(b.Index) + 1e-9).All(x => x);
        bool spaced = walls.Zip(walls.Skip(1), (a, b) => (b.X - a.Right) / run.SpeedAt(b.Index) >= SwiftGame.BayTime - SwiftGame.WallThickness / run.SpeedAt(b.Index) - 1e-9).All(x => x);
        bool inside = walls.All(w => w.Centre - w.Gap / 2 - w.Swing >= SwiftGame.Margin - 1e-9 && w.Centre + w.Gap / 2 + w.Swing <= SwiftGame.Height - SwiftGame.Margin + 1e-9);
        int shutters = walls.Count(w => w.Moving);
        bool lateShutters = walls.Where(w => w.Moving).All(w => w.Index >= run.ShuttersFrom);
        check(walls.Count > 100 && reachable && spaced && inside && shutters > 10 && lateShutters,
          $"{e}: {walls.Count} walls, each opening within {SwiftGame.MaxShift} of the last (shutter travel included), at least {SwiftGame.BayTime} s apart, clear of floor and ceiling, {shutters} sliding shutters from wall {run.ShuttersFrom + 1}");
      }
      check(seen.Count > 0 && seen.All(w => w.Gap >= SwiftGame.MinGap), $"no opening is ever under {SwiftGame.MinGap} units");

      // An easy start: the first ten walls of every course.
      var start = new List<Wall>();
      var first = New(21);
      first.Flap();
      Fly(first, 60, Pilot, start);
      var ten = start.Take(10).ToList();
      check(ten.All(w => w.Gap >= 4.7 && !w.Moving) && ten.Zip(ten.Skip(1), (a, b) => Math.Abs(b.Centre - a.Centre) <= 2.8 + 1e-9).All(x => x) && first.SpeedAt(0) < 4.5,
        $"the first ten walls are easy: openings {ten.Min(w => w.Gap):F1}+ tall, steps of at most 2.8, course speed {first.SpeedAt(0):F1}");
    }

    static void Autopilot(Action<bool, string> check)
    {
      Console.WriteLine("Swift: every course can be flown");
      foreach (var e in new[] { Exposure.Sheltered, Exposure.Normal, Exposure.Exposed })
      {
        var scores = new List<int>();
        int feathers = 0;
        for (int seed = 1; seed <= 5; seed++)
        {
          var g = New(seed, e);
          g.Flap();
          Fly(g, 150, Pilot);
          scores.Add(g.Score);
          feathers += g.Feathers;
        }
        check(scores.All(s => s >= 60), $"{e}: a simple autopilot clears {string.Join(", ", scores)} walls on five seeds ({feathers} feathers)");
      }
      var lv = New(2);
      lv.Flap();
      Fly(lv, 150, Pilot);
      check(lv.Level == 1 + lv.Score / 10 && lv.Level >= 7, $"a level every ten walls (score {lv.Score}, level {lv.Level})");
      check(lv.Trail.Count > 100 && lv.Trail.Zip(lv.Trail.Skip(1), (a, b) => b.X > a.X).All(x => x), $"the flight path is recorded ({lv.Trail.Count} points, always forward)");
    }

    static void Zen(Action<bool, string> check)
    {
      Console.WriteLine("Swift: Zen");
      var g = New(5, Exposure.Normal, SwiftMode.Zen);
      g.Flap();
      Fly(g, 30, null);
      check(!g.IsOver && g.State == SwiftState.Flying && g.Bumps > 5 && g.Score > 0, $"no flapping: bounces off the floor and bumps through walls ({g.Bumps} bumps, {g.Score} walls)");
    }

    static void PauseAndReplay(Action<bool, string> check)
    {
      Console.WriteLine("Swift: pause and replay");
      var g = New(4);
      g.Flap();
      Fly(g, 3, Pilot);
      double y = g.Y, d = g.Distance;
      g.Pause();
      g.Tick(2);
      g.Flap();
      check(g.State == SwiftState.Paused && g.Y == y && g.Distance == d && g.Flaps > 0, "paused: nothing moves and flaps are ignored");
      g.Resume();
      check(g.State == SwiftState.Flying, "resume carries on flying");
      var ready = New(4);
      ready.Pause();
      ready.Resume();
      check(ready.State == SwiftState.Ready, "pausing before take-off comes back to the wait");
      var fall = New(3);
      fall.Flap();
      var wall = fall.Walls[0];
      double over = Math.Min(SwiftGame.Height - 0.6, wall.Centre + wall.Gap / 2 + 1.2);
      for (double t = 0; t < 8 && fall.State != SwiftState.Crashed; t += Frame)
      {
        if (fall.Y + fall.Vy * 0.15 - 0.5 * SwiftGame.Gravity * 0.0225 < over && fall.Vy < 1) fall.Flap();
        fall.Tick(Frame);
      }
      fall.Pause();
      Fly(fall, 2, null);
      check(fall.IsOver && fall.HitWall, "a pause during the fall is ignored: the swift lands and the flight ends");

      var a = New(9);
      var b = New(9);
      a.Flap();
      b.Flap();
      Fly(a, 40, Pilot);
      Fly(b, 40, Pilot);
      check(a.Score == b.Score && a.Y == b.Y && a.Distance == b.Distance && a.Feathers == b.Feathers, $"same seed, same flaps, same flight (score {a.Score})");
      var even = New(9, Exposure.Normal, SwiftMode.Zen);
      var uneven = New(9, Exposure.Normal, SwiftMode.Zen);
      even.Flap();
      uneven.Flap();
      for (int i = 0; i < 20 * 60; i++)
      {
        even.Tick(Frame);
        // The rules step at a fixed rate, so uneven frames cover the same flight.
        uneven.Tick(i % 2 == 0 ? Frame * 0.5 : Frame * 1.5);
      }
      check(Math.Abs(even.Distance - uneven.Distance) < 0.05 && even.Score == uneven.Score && even.Bumps == uneven.Bumps, $"uneven frame times fly the same course ({even.Score} walls, {even.Bumps} bumps)");
    }

    static void Section(Action<bool, string> check)
    {
      Console.WriteLine("Swift: the boss-key section is the game's own section");
      var g = New(6);
      g.Flap();
      Fly(g, 25, Pilot);
      var input = SectionModel.FromGame(g, 0.75);
      var s = new SectionModel(input);
      var onScreen = g.Walls.Where(w => w.Right - g.Distance > input.Left && w.X - g.Distance < input.Right).ToList();
      check(s.Walls.Count == onScreen.Count && s.Walls.Zip(onScreen, (a, b) => Math.Abs(a.X0 - (b.X - g.Distance)) < 1e-9 && Math.Abs(a.Centre - b.CentreAt(g.Elapsed)) < 1e-9).All(x => x),
        $"the {s.Walls.Count} cross walls on screen are cut exactly where the game has them, openings at the same height");
      bool cutRound = s.Walls.All(w => s.Cut.Any(c => Math.Abs(c[2].Y - w.Lo) < 1e-9 && Math.Abs(c[0].X - Math.Max(input.Left, w.X0)) < 1e-9)
        && s.Cut.Any(c => Math.Abs(c[0].Y - w.Hi) < 1e-9 && Math.Abs(c[0].X - Math.Max(input.Left, w.X0)) < 1e-9));
      check(cutRound && s.Cut.Any(c => c[0].Y == -SectionModel.FloorSlab) && s.Cut.Any(c => c[0].Y == SectionModel.Height), "floor slab, roof slab and each wall cut round its opening");
      int straddling = s.Walls.Count(w => w.X0 < input.Left || w.X1 > input.Right);
      check(s.Bays.Count == s.Walls.Count + 1 - straddling && s.Bays.All(b => b.Width > 0) && s.Bays.Where(b => b.Width >= SectionModel.MinBayForLight).All(b => b.HasRooflight) && s.Metrics.Rooflights >= 2,
        $"{s.Bays.Count} bays, none empty ({straddling} walls cut by the edge of the sheet), a rooflight over each wide one ({s.Metrics.Rooflights})");

      var main = s.Streamlines[0];
      bool through = s.Walls.All(w => main.Points.Where(p => p.X >= w.X0 && p.X <= w.X1).All(p => p.Y > w.Lo && p.Y < w.Hi));
      check(through, "the main airflow line passes through every opening");
      check(main.Points.Min(p => p.DistanceTo(input.Probe)) < 0.3 && input.Path.Count > 100, $"it is the swift's own flight up to the probe P1 ({input.Path.Count} path points)");
      check(s.Streamlines.Count(l => !l.Stack) == 3 && s.Streamlines.Count(l => l.Stack) == s.Metrics.Rooflights, "three cross-flow lines, one stack line per rooflight");

      check(Math.Abs(SectionModel.SummerAltitude - 73.44) < 0.01 && Math.Abs(SectionModel.WinterAltitude - 26.56) < 0.01, "noon sun at 40° N: 73.4° in June, 26.6° in December");
      var summer = s.Rays.Where(r => r.Summer).ToList();
      var winter = s.Rays.Where(r => !r.Summer).ToList();
      check(summer.Count == 2 && winter.Count == 2 && s.Rays.All(r => r.Edge >= s.SunBay.LightX0 && r.Edge <= s.SunBay.LightX1), "the sun study goes through one rooflight, the middle one");
      check(summer.All(r => r.To.Y == 0 || r.OnWall) && summer.All(r => r.To.X - r.Edge <= SectionModel.Height / Math.Tan(SectionModel.SummerAltitude * Math.PI / 180) + 1e-9),
        "June sun drops almost straight down through it");
      double Slope(SunRay r) => (r.From.Y - r.To.Y) / (r.To.X - r.From.X);
      check(winter.All(r => Slope(r) < 0.5 && (r.OnWall || r.To.X - r.Edge > 20)) && summer.All(r => Slope(r) > 2.5),
        $"December sun slants far across ({string.Join(", ", winter.Select(r => r.OnWall ? "onto a wall" : "to the floor"))})");
      var lit = s.Bays.First(b => b.HasRooflight);
      var row = s.Daylight.Where(q => q.x >= lit.X0 && q.x <= lit.X1).ToList();
      double under = row.OrderBy(q => Math.Abs(q.x - lit.Mid)).First().df;
      check(under > 4 && under > 1.5 * row.Min(q => q.df), $"daylight peaks under the rooflight ({under:F1} %, {row.Min(q => q.df):F1} % at the walls)");
      var m = s.Metrics;
      check(m.MeanDf > 1.5 && m.MeanDf < 6 && m.Sda > 0.2 && m.Sda < 1, $"mean daylight factor {m.MeanDf:F1} %, {m.Sda:P0} of the workplane at 2 % or more");
      check(m.CrossAch > 1 && m.CrossAch < 40 && m.StackAch > 0.2 && m.StackAch < 15 && m.ProbeSpeed > 0, $"cross ventilation {m.CrossAch:F1} ach, stack {m.StackAch:F1} ach, {m.ProbeSpeed:F2} m/s at P1");

      var windy = SectionModel.FromGame(g, 0.75);
      windy.Wind *= 2;
      var w2 = new SectionModel(windy).Metrics;
      check(Math.Abs(w2.CrossAch - 2 * m.CrossAch) < 1e-9 && Math.Abs(w2.StackAch - m.StackAch) < 1e-9, "double the wind, double the cross flow; the stack flow does not care");
      var wide = SectionModel.FromGame(g, 0.75);
      foreach (var w in wide.Walls)
      {
        w.Lo -= 0.5;
        w.Hi += 0.5;
      }
      check(new SectionModel(wide).Metrics.CrossAch > m.CrossAch, "bigger openings, more air across");
      check(Math.Abs(SectionModel.Series(1, 1) - Math.Sqrt(0.5)) < 1e-12, "openings in series: 1/A² = Σ 1/Aᵢ²");
      check(Math.Abs(input.Wind - Math.Round(1.5 + g.Speed * 0.5, 1)) < 1e-9 && Math.Abs(input.WindowPhase - g.Distance % SwiftGame.WindowPitch) < 1e-9,
        $"the course speed sets the wind ({input.Wind} m/s); the back windows keep their rhythm");

      var before = new SectionModel(SectionModel.FromGame(New(7), 0.75));
      check(before.Walls.Count > 0 && before.Streamlines[0].Points.Count > 10 && double.IsFinite(before.Metrics.CrossAch), "it works before the first flap too");

      // Size far from 0.75 m (a millimetre document, say) still gives a house-sized section.
      var tiny = new SectionModel(SectionModel.FromGame(g, 0.001));
      var huge = new SectionModel(SectionModel.FromGame(g, 1e6));
      check(tiny.Metres == SectionModel.MinMetres && huge.Metres == SectionModel.MaxMetres && tiny.Workplane < SectionModel.Height && huge.Workplane > 0
        && tiny.Daylight.All(q => q.df > 0) && huge.Daylight.All(q => q.df > 0) && double.IsFinite(tiny.Metrics.MeanDf) && double.IsFinite(huge.Metrics.MeanDf),
        $"the metre scale is kept within {SectionModel.MinMetres}–{SectionModel.MaxMetres} m per unit, so the workplane stays under the ceiling and daylight factors stay positive");

      // A winter ray that clears a wall's face but drops below the sill inside its thickness ends on the sill.
      double tan = Math.Tan(SectionModel.WinterAltitude * Math.PI / 180);
      var sill = new SectionModel(new SectionInput
      {
        Walls = new List<CrossWall> { new CrossWall { X0 = 6, X1 = 7.4, Lo = 4, Hi = 9 }, new CrossWall { X0 = 20, X1 = 21.4, Lo = 8.3, Hi = 11 } },
      });
      var bay = sill.SunBay;
      var ray = sill.Rays.First(r => !r.Summer && Math.Abs(r.Edge - (bay.LightX0 + 0.1)) < 1e-9);
      double atFace = SectionModel.Height - (20 - ray.Edge) * tan, atBack = SectionModel.Height - (21.4 - ray.Edge) * tan;
      check(atFace > 8.3 && atBack < 8.3 && ray.OnWall && Math.Abs(ray.To.Y - 8.3) < 1e-9 && ray.To.X > 20 && ray.To.X < 21.4 && Math.Abs(ray.To.X - (ray.Edge + (SectionModel.Height - 8.3) / tan)) < 1e-9,
        $"a ray through the face at {atFace:F2} that would leave at {atBack:F2} stops on the sill at x {ray.To.X:F2}");

      // The swift inside a wall: the main line carries on from the probe, it never doubles back to that wall's opening.
      var inside = new SectionModel(new SectionInput
      {
        Walls = new List<CrossWall> { new CrossWall { X0 = 4.2, X1 = 5.6, Lo = 4, Hi = 8 }, new CrossWall { X0 = 14, X1 = 15.4, Lo = 5, Hi = 9 } },
        Probe = new V2(SwiftGame.BirdX, 6),
      });
      var line = inside.Streamlines[0];
      check(line.Points.Zip(line.Points.Skip(1), (a, b) => b.X >= a.X - 1e-9).All(x => x) && line.Points.Any(p => Math.Abs(p.X - 14.7) < 1e-9 && Math.Abs(p.Y - 7) < 1e-9),
        "from inside a wall the main airflow line only runs forward, to the next opening");
    }
  }
}
