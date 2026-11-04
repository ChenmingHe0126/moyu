using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Games.Breakout.Core;

namespace Moyu.Core.Tests
{
  /// <summary>Rules of the Rebound brick breaker (no Rhino needed).</summary>
  static class TestsBreakout
  {
    public static void Run(Action<bool, string> check)
    {
      BondsLayOut(check);
      BricksTakeHits(check);
      PaddleSteers(check);
      WallsAndAngles(check);
      LivesAndServe(check);
      ClearAndNextLevel(check);
      ComboScoring(check);
      PowerUps(check);
      NoTunnelling(check);
      BlitzAndReplay(check);
      CustomWalls(check);
      EveryBrickReachable(check);
      HeldBalls(check);
      Facade(check);
    }

    static BreakoutGame Empty(BreakoutMode mode = BreakoutMode.Classic)
    {
      var g = new BreakoutGame(new BreakoutSettings { Seed = 3, Mode = mode, PowerChance = 0 });
      return g;
    }

    /// <summary>Runs until the predicate holds or the time runs out.</summary>
    static bool RunUntil(BreakoutGame g, Func<bool> done, double seconds)
    {
      for (double t = 0; t < seconds; t += 1 / 120.0)
      {
        if (done()) return true;
        g.Tick(1 / 120.0);
      }
      return done();
    }

    static bool Overlap(Brick a, Brick b) => a.X0 < b.X1 - 1e-9 && b.X0 < a.X1 - 1e-9 && a.Y0 < b.Y1 - 1e-9 && b.Y0 < a.Y1 - 1e-9;

    static void BondsLayOut(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: brick bonds");
      foreach (BondType t in Enum.GetValues(typeof(BondType)))
      {
        var wall = Bonds.Lay(t, 1, 13, 15.8, 10, new Random(1));
        bool inside = wall.All(b => b.X0 >= 0 && b.X1 <= 13 && b.Y1 <= 15.8 + 1e-9 && b.Width > 0.3);
        bool apart = !wall.Where((a, i) => wall.Skip(i + 1).Any(b => Overlap(a, b))).Any();
        check(wall.Count > 20 && inside && apart, $"{t}: {wall.Count} bricks inside the field, none overlapping");
      }
      var stretcher = Bonds.Lay(BondType.Stretcher, 1, 13, 15.8, 4, new Random(1));
      double FirstJoint(IEnumerable<Brick> course) => course.OrderBy(b => b.X0).First().X1;
      var courses = stretcher.GroupBy(b => Math.Round(b.Y0, 3)).OrderByDescending(c => c.Key).ToList();
      check(Math.Abs(FirstJoint(courses[0]) - FirstJoint(courses[1])) > 0.5, "stretcher bond: joints shift half a brick every course");
      var english = Bonds.Lay(BondType.English, 1, 13, 15.8, 4, new Random(1)).GroupBy(b => Math.Round(b.Y0, 3)).OrderByDescending(c => c.Key).ToList();
      check(english[0].Average(b => b.Width) > 1.3 && english[1].Average(b => b.Width) < 0.8, "English bond: a stretcher course, then a header course");
      var flemish = Bonds.Lay(BondType.Flemish, 1, 13, 15.8, 2, new Random(1)).GroupBy(b => Math.Round(b.Y0, 3)).First().OrderBy(b => b.X0).ToList();
      // Interior bricks only: the ends are cut to the field.
      var inner = flemish.Skip(1).Take(flemish.Count - 2).ToList();
      bool alternates = inner.Zip(inner.Skip(1), (a, b) => (a.Width > 1.2) != (b.Width > 1.2)).All(x => x);
      check(alternates, "Flemish bond: stretchers and headers alternate in a course");
      int full = Bonds.Lay(BondType.Stretcher, 1, 13, 15.8, 10, new Random(1)).Count, screen = Bonds.Lay(BondType.Screen, 1, 13, 15.8, 10, new Random(1)).Count;
      check(screen < full * 0.7, $"screen wall leaves holes ({screen} of {full} bricks)");
      var arch = Bonds.Lay(BondType.Arch, 1, 13, 15.8, 10, new Random(1));
      check(!arch.Any(b => b.X0 < 6.5 && b.X1 > 6.5 && b.Y0 < 10.5) && arch.Any(b => b.Material == BrickMaterial.Steel), "arch: an opening at the foot of the wall, its ring in steel");
      var version = Empty();
      int v0 = version.WallVersion;
      version.SetWall(new[] { new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Glass), new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      version.AddBall(6.5, 4, 0, 10);
      RunUntil(version, () => version.BricksBroken > 0, 3);
      check(version.WallVersion > v0 + 1, "the wall version counts up when the wall is laid and when a brick is hit");
      var late = Bonds.Lay(BondType.Stretcher, 8, 13, 15.8, 10, new Random(2));
      var early = Bonds.Lay(BondType.Stretcher, 1, 13, 15.8, 10, new Random(2));
      check(late.Count(b => b.Material == BrickMaterial.Concrete) > early.Count(b => b.Material == BrickMaterial.Concrete) && late.Any(b => b.Material == BrickMaterial.Steel) && !early.Any(b => b.Material == BrickMaterial.Steel),
        "later levels: more concrete, some steel");
    }

    static void BricksTakeHits(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: hits per material");
      foreach (var (m, hits) in new[] { (BrickMaterial.Glass, 1), (BrickMaterial.Brick, 2), (BrickMaterial.Concrete, 3) })
      {
        var g = Empty();
        var target = new Brick(5.5, 8, 7.5, 8.55, m);
        g.SetWall(new[] { target, new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
        int bounces = 0;
        for (int i = 0; i < hits; i++)
        {
          g.AddBall(6.5, 4, 0, 10);
          RunUntil(g, () => g.Balls.Count > 0 && g.Balls[0].Vy < 0, 3);
          bounces++;
        }
        check(!target.Alive && bounces == hits, $"{m}: breaks on hit {hits}");
      }
      var steel = Empty();
      var wallBrick = new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Steel);
      steel.SetWall(new[] { wallBrick, new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      for (int i = 0; i < 4; i++)
      {
        steel.AddBall(6.5, 4, 0, 10);
        RunUntil(steel, () => steel.Balls.Count > 0 && steel.Balls[0].Vy < 0, 3);
      }
      check(wallBrick.Alive && steel.Score == 0, "steel never breaks and scores nothing");
      var down = Empty();
      down.SetWall(new[] { new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Glass), new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      down.AddBall(6.5, 4, 0, 10);
      RunUntil(down, () => down.Balls[0].Vy < 0, 3);
      check(down.Balls[0].Vy < 0 && down.Balls[0].Y < 8, "the ball bounces back down off the brick's underside");
    }

    static void PaddleSteers(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: paddle");
      double Angle(double offset)
      {
        var g = Empty();
        g.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
        double px = g.PaddleX;
        var b = g.AddBall(px + offset, 4, 0, -8);
        RunUntil(g, () => b.Vy > 0, 2);
        return Math.Atan2(b.Vx, b.Vy) * 180 / Math.PI;
      }
      double mid = Angle(0), right = Angle(1.3), left = Angle(-0.6);
      check(Math.Abs(mid) < 1, $"centre of the paddle sends it straight up ({mid:F1}°)");
      check(right > 50 && right <= 60.01, $"the right edge sends it away at about 60° ({right:F1}°)");
      check(left < -15 && left > -40, $"left of centre: up and to the left ({left:F1}°)");
      var keys = Empty();
      keys.Steer = 1;
      double x0 = keys.PaddleX;
      for (int i = 0; i < 30; i++) keys.Tick(1 / 60.0);
      check(keys.PaddleX > x0 + 3 && keys.Balls[0].Held && Math.Abs(keys.Balls[0].X - keys.PaddleX - 0.3) < 1e-9, "arrow keys move the paddle; the served ball rides along");
      for (int i = 0; i < 200; i++) keys.Tick(1 / 60.0);
      check(Math.Abs(keys.PaddleX - (13 - BreakoutGame.PaddleWidth / 2)) < 1e-9, "the paddle stops at the wall");
      var mouse = Empty();
      mouse.TargetX = 2;
      for (int i = 0; i < 60; i++) mouse.Tick(1 / 60.0);
      check(Math.Abs(mouse.PaddleX - 2) < 1e-6, "the paddle follows the mouse");
    }

    static void WallsAndAngles(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: walls");
      var g = Empty();
      g.SetWall(new[] { new Brick(0.2, 16.2, 1.2, 16.6, BrickMaterial.Glass) });
      var b = g.AddBall(6, 8, 10, 0.5);
      RunUntil(g, () => b.Vx < 0, 2);
      bool steep = Math.Abs(b.Vy) >= 0.27 * g.BallSpeed;
      for (int i = 0; i < 600 && g.Balls.Count > 0; i++)
      {
        g.Tick(1 / 120.0);
        if (g.Balls.Count > 0 && Math.Abs(b.Vy) < 0.27 * g.BallSpeed) steep = false;
      }
      check(steep, "a nearly flat ball is tipped steeper at the first wall and never shuttles sideways");
      var top = Empty();
      top.SetWall(new[] { new Brick(0.2, 2, 0.6, 2.3, BrickMaterial.Glass) });
      var t = top.AddBall(6.5, 15, 0, 9);
      RunUntil(top, () => t.Vy < 0, 2);
      check(t.Vy < 0 && t.Y <= 17 - BreakoutGame.BallRadius + 1e-9, "the top wall sends it back down");
    }

    static void LivesAndServe(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: lives");
      var g = Empty();
      g.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      g.AddBall(0.5, 3, 0, -9);
      RunUntil(g, () => g.State == BreakoutState.Serving, 3);
      check(g.Lives == 2 && g.State == BreakoutState.Serving && g.Balls.Count == 1 && g.Balls[0].Held, "a missed ball costs a life and serves a new one");
      var rally = Empty(BreakoutMode.Zen);
      rally.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Steel), new Brick(11.8, 15, 12.8, 15.5, BrickMaterial.Glass) });
      double baseSpeed = rally.BallSpeed;
      Returns(rally, rally.AddBall(6.5, 6, 0.8, -8), 12);
      bool faster = rally.BallSpeed > baseSpeed * 1.05;
      rally.AddBall(0.5, 3, 0, -9);
      rally.TargetX = 12;
      RunUntil(rally, () => rally.State == BreakoutState.Serving, 3);
      check(faster && Math.Abs(rally.BallSpeed - baseSpeed) < 1e-9, $"a rally speeds the ball up ({(faster ? "yes" : "no")}); the serve after a lost ball starts at the level's speed again");
      for (int i = 0; i < 2; i++)
      {
        g.AddBall(0.5, 3, 0, -9);
        RunUntil(g, () => g.State != BreakoutState.Playing, 3);
      }
      check(g.State == BreakoutState.Lost && g.IsOver, "out of lives: game over");
      var zen = Empty(BreakoutMode.Zen);
      zen.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      for (int i = 0; i < 5; i++)
      {
        zen.AddBall(0.5, 3, 0, -9);
        RunUntil(zen, () => zen.State == BreakoutState.Serving, 3);
      }
      check(zen.Lives == 3 && !zen.IsOver, "zen: no lives lost");
      var serve = Empty();
      serve.Launch();
      check(serve.State == BreakoutState.Playing && serve.Balls[0].Vy > 0, "space launches the served ball upwards");
    }

    static void ClearAndNextLevel(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: clearing a wall");
      var g = Empty();
      g.SetWall(new[] { new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Glass), new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Steel) });
      g.AddBall(6.5, 4, 0, 10);
      RunUntil(g, () => g.State == BreakoutState.Cleared, 3);
      check(g.State == BreakoutState.Cleared && g.Score >= 500, "the last breakable brick clears the wall (steel does not count), +500 × level");
      RunUntil(g, () => g.State == BreakoutState.Serving, 3);
      check(g.Level == 2 && g.Remaining > 20 && g.Bond == BondType.Stack, "after a pause: level 2, a fresh wall in the next bond");
    }

    static void ComboScoring(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: combo");
      var g = Empty();
      var bricks = Enumerable.Range(0, 4).Select(i => new Brick(5.5, 6 + i * 0.7, 7.5, 6.55 + i * 0.7, BrickMaterial.Glass)).ToList();
      bricks.Add(new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass));
      g.SetWall(bricks);
      g.Apply(PowerKind.Pierce);
      g.AddBall(6.5, 4, 0, 10);
      RunUntil(g, () => bricks.Take(4).All(b => !b.Alive), 3);
      check(g.Score == 10 + 20 + 30 + 40 && g.Combo == 4, $"each hit before touching the paddle raises the multiplier (score {g.Score})");
      var b2 = g.AddBall(g.PaddleX, 3, 0, -9);
      RunUntil(g, () => b2.Vy > 0, 2);
      check(g.Combo == 0, "the paddle resets the combo");
    }

    /// <summary>Keeps the paddle under one ball and counts how often it comes back off the paddle.</summary>
    static int Returns(BreakoutGame g, Ball ball, int wanted, double seconds = 90)
    {
      int n = 0;
      double before = ball.Vy;
      for (double t = 0; t < seconds && n < wanted && g.Balls.Contains(ball); t += 1 / 120.0)
      {
        if (ball.Held) g.Launch();
        g.TargetX = ball.X;
        g.Tick(1 / 120.0);
        if (before < 0 && (ball.Vy > 0 || ball.Held) && ball.Y < 3) n++;
        before = ball.Vy;
      }
      return n;
    }

    static void PowerUps(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: power-ups wear off by use");
      var g = Empty(BreakoutMode.Zen);
      g.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Steel), new Brick(11.8, 15, 12.8, 15.5, BrickMaterial.Glass) });
      g.Apply(PowerKind.Wide);
      check(Math.Abs(g.PaddleWidthNow - BreakoutGame.PaddleWidth * 1.5) < 1e-9 && g.WideLeft == BreakoutGame.WideHits, $"wide: paddle ×1.5 for {BreakoutGame.WideHits} returns");
      var ball = g.AddBall(6.5, 6, 0.8, -8);
      int seven = Returns(g, ball, BreakoutGame.WideHits - 1);
      bool still = g.PaddleWidthNow > BreakoutGame.PaddleWidth;
      Returns(g, ball, 1);
      check(seven == BreakoutGame.WideHits - 1 && still && Math.Abs(g.PaddleWidthNow - BreakoutGame.PaddleWidth) < 1e-9 && g.WideLeft == 0, "still wide after 7 returns, back to normal on the 8th");
      g.Apply(PowerKind.Narrow);
      check(g.NarrowLeft == BreakoutGame.NarrowHits && g.PaddleWidthNow < BreakoutGame.PaddleWidth, $"narrow (the bad one): {BreakoutGame.NarrowHits} returns");
      double fast = g.BallSpeed;
      g.Apply(PowerKind.Slow);
      check(Math.Abs(g.BallSpeed - fast * 0.7) < 1e-9 && g.SlowLeft == BreakoutGame.SlowHits, $"slow: speed ×0.7 for {BreakoutGame.SlowHits} returns");
      Returns(g, ball, BreakoutGame.SlowHits);
      // Rallies still nudge the speed up a little, so compare against the slowed speed.
      check(g.SlowLeft == 0 && g.NarrowLeft == 0 && g.BallSpeed >= fast && Math.Abs(g.PaddleWidthNow - BreakoutGame.PaddleWidth) < 1e-9, "both wear off with the returns");
      var multi = Empty();
      multi.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      multi.AddBall(6.5, 8, 2, 8);
      multi.Apply(PowerKind.Multi);
      check(multi.Balls.Count == 3, "multi: three balls (instant)");

      var sticky = Empty(BreakoutMode.Zen);
      sticky.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Steel), new Brick(11.8, 15, 12.8, 15.5, BrickMaterial.Glass) });
      sticky.Apply(PowerKind.Sticky);
      var sb = sticky.AddBall(sticky.PaddleX + 0.5, 3, 0, -9);
      int catches = 0, bounces = 0;
      for (int k = 0; k < 4; k++)
      {
        double before = sb.Vy;
        for (double t = 0; t < 8; t += 1 / 120.0)
        {
          sticky.TargetX = sb.X;
          sticky.Tick(1 / 120.0);
          if (sb.Held)
          {
            catches++;
            sticky.Launch();
            break;
          }
          if (before < 0 && sb.Vy > 0 && sb.Y < 3)
          {
            bounces++;
            break;
          }
          before = sb.Vy;
        }
      }
      check(catches == BreakoutGame.StickyCatches && bounces == 1 && sticky.StickyLeft == 0, $"sticky: catches the ball {catches} times, then it bounces as usual");

      var pierce = Empty();
      var column = Enumerable.Range(0, 8).Select(i => new Brick(5.5, 6 + i * 0.7, 7.5, 6.55 + i * 0.7, BrickMaterial.Concrete)).ToList();
      pierce.SetWall(column.Concat(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) }));
      pierce.Apply(PowerKind.Pierce);
      var pb = pierce.AddBall(6.5, 4, 0, 10);
      RunUntil(pierce, () => pb.Vy < 0, 3);
      int gone = column.Count(b => !b.Alive);
      check(gone == BreakoutGame.PierceBricks && pierce.PierceLeft == 0 && column[BreakoutGame.PierceBricks].Alive && pb.Vy < 0,
        $"pierce: straight through {gone} concrete bricks, then the ball bounces off the next one");

      var life = Empty();
      for (int i = 0; i < 4; i++) life.Apply(PowerKind.Life);
      check(life.Lives == BreakoutGame.MaxLives, "extra lives stop at 5");
      var lost = Empty();
      lost.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      lost.Apply(PowerKind.Wide);
      lost.Apply(PowerKind.Sticky);
      lost.AddBall(12.5, 3, 0, -9);
      lost.TargetX = 1;
      RunUntil(lost, () => lost.Lives < 3, 3);
      check(lost.Lives < 3 && lost.WideLeft == 0 && lost.StickyLeft == 0, "losing a ball clears them all");

      var counts = new Dictionary<PowerKind, int>();
      for (int i = 0; i < 400; i++)
      {
        var odds = new BreakoutGame(new BreakoutSettings { Seed = 100 + i, PowerChance = 1, PowerTour = false });
        odds.SetWall(new[] { new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Glass), new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
        odds.AddBall(6.5, 4, 0, 10);
        RunUntil(odds, () => odds.Powers.Count > 0, 2);
        foreach (var pw in odds.Powers) counts[pw.Kind] = counts.TryGetValue(pw.Kind, out int c) ? c + 1 : 1;
      }
      int all = counts.Values.Sum(), stickyDrops = counts.TryGetValue(PowerKind.Sticky, out int sd) ? sd : 0;
      check(all > 300 && stickyDrops > 0 && stickyDrops < all * 0.11, $"sticky is rare: {stickyDrops} of {all} drops ({stickyDrops * 100.0 / all:F0} %)");

      Console.WriteLine("Rebound: the power-ups show up early");
      var fresh = new BreakoutGame(new BreakoutSettings { Seed = 8 });
      check(Math.Abs(fresh.PowerChanceNow - 0.36) < 1e-9, $"first wall: {fresh.PowerChanceNow:P0} of broken bricks drop one (later walls 15 %)");
      var later = new BreakoutGame(new BreakoutSettings { Seed = 8, StartLevel = 3 });
      var second = new BreakoutGame(new BreakoutSettings { Seed = 8, StartLevel = 2 });
      check(Math.Abs(second.PowerChanceNow - 0.24) < 1e-9 && Math.Abs(later.PowerChanceNow - 0.15) < 1e-9, "second wall 24 %, then the usual 15 %");
      var tour = new BreakoutGame(new BreakoutSettings { Seed = 9, PowerChance = 1 });
      var shown = new List<PowerKind>();
      var seen = new HashSet<Power>();
      for (int i = 0; i < 6; i++)
      {
        tour.SetWall(new[] { new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Glass), new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
        tour.AddBall(6.5, 4, 0, 10);
        RunUntil(tour, () => tour.Powers.Any(pw => !seen.Contains(pw)), 2);
        foreach (var pw in tour.Powers.Where(pw => seen.Add(pw))) shown.Add(pw.Kind);
      }
      check(shown.Count == 6 && shown.Distinct().Count() == 6 && !shown.Contains(PowerKind.Narrow),
        $"the first six drops are the six helpful ones, once each ({string.Join(", ", shown)})");

      var drop = new BreakoutGame(new BreakoutSettings { Seed = 1, PowerChance = 1 });
      drop.SetWall(new[] { new Brick(5.5, 8, 7.5, 8.55, BrickMaterial.Glass), new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      drop.AddBall(6.5, 4, 0, 10);
      RunUntil(drop, () => drop.Powers.Count > 0, 2);
      check(drop.Powers.Count == 1, "a broken brick can drop a power-up");
      drop.TargetX = drop.Powers[0].X;
      RunUntil(drop, () => drop.Powers.Count == 0, 5);
      check(drop.LastPower != null, "catching it with the paddle applies it");
    }

    static void NoTunnelling(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: fast balls");
      var g = Empty();
      var thin = new Brick(0.5, 9, 12.5, 9.05, BrickMaterial.Steel);
      g.SetWall(new[] { thin, new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      for (int i = 0; i < 20; i++) g.Apply(PowerKind.Life);
      var b = g.AddBall(6.5, 4, 0, 50);
      bool through = false;
      for (int i = 0; i < 60; i++)
      {
        g.Tick(1 / 30.0);
        if (g.Balls.Count > 0 && g.Balls[0].Y > 9.5) through = true;
      }
      check(!through, "even at full speed with a coarse frame the ball does not pass through a thin wall");
    }

    static void BlitzAndReplay(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: blitz and replay");
      var blitz = new BreakoutGame(new BreakoutSettings { Mode = BreakoutMode.Blitz, BlitzSeconds = 2, Seed = 1 });
      for (int i = 0; i < 200 && !blitz.IsOver; i++) blitz.Tick(1 / 60.0);
      check(blitz.State == BreakoutState.TimeUp, "blitz ends when the clock runs out");
      string Run(int seed)
      {
        var g = new BreakoutGame(new BreakoutSettings { Seed = seed, Mode = BreakoutMode.Zen });
        g.Launch();
        for (int i = 0; i < 3000; i++)
        {
          // Meet the ball a little off centre, as a player would, so it does not loop straight up and down.
          g.TargetX = g.Balls.Count > 0 ? g.Balls[0].X + 0.6 * Math.Sin(i * 0.013) : 6.5;
          if (g.State == BreakoutState.Serving) g.Launch();
          g.Tick(1 / 60.0);
        }
        return $"{g.Score}/{g.BricksBroken}/{g.Level}/{g.Remaining}";
      }
      string a = Run(5);
      check(a == Run(5), $"the same seed and inputs replay exactly ({a})");
      check(int.Parse(a.Split('/')[1]) > 15, "a paddle that follows the ball keeps a rally going and breaks bricks");
    }

    static void CustomWalls(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: custom walls");
      var wall = new CustomWall();
      for (int i = 0; i < 6; i++)
        for (int j = 0; j < 3; j++)
          wall.Rects.Add((i * 400.0, j * 200.0, i * 400 + 380 - j * 60, j * 200 + 180));
      var bricks = Bonds.FitCustom(wall, 13, 15.8, 7);
      check(bricks.Count == 18 && bricks.All(b => b.X0 >= 0 && b.X1 <= 13 && b.Y1 <= 15.8), "closed curves become bricks scaled into the field");
      check(bricks.Select(b => b.Material).Distinct().Count() == 3, "smaller panels are glass, larger ones concrete");
      var g = new BreakoutGame(new BreakoutSettings { Custom = wall, Seed = 1 });
      check(g.Remaining == 18 && g.WallName.Contains("Custom"), "a game can be played on it");
      // A vent slot 100 mm wide on a 2.4 km-wide drawing is nothing in the field: it is left out, not laid inverted.
      wall.Rects.Add((1000, 100, 1001, 300));
      var slotted = Bonds.FitCustom(wall, 13, 15.8, 7, out int tooSmall);
      check(slotted.Count == 18 && tooSmall == 1 && slotted.All(b => b.Width > 0.3 && b.Height > 0.3), "a panel too thin for a ball is skipped and counted, never laid inside out");
      var thin = new CustomWall();
      thin.Rects.Add((0, 0, 2000, 180));
      var strip = Bonds.FitCustom(thin, 13, 15.8, 7);
      check(strip.Count == 1 && strip[0].Height > 1 && strip[0].Width > 12.4, "a single long panel keeps most of its width");
      var nothing = new CustomWall();
      nothing.Rects.Add((0, 0, 1, 1));
      nothing.Rects.Add((1000, 1000, 1001, 1001));
      var fallback = new BreakoutGame(new BreakoutSettings { Custom = nothing, Seed = 1 });
      check(Bonds.FitCustom(nothing, 13, 15.8, 7).Count == 0 && fallback.Remaining > 20, "a custom wall with no playable panel falls back to a bond");
    }

    /// <summary>Bricks connected through breakable neighbours to the top or bottom course, where the ball can get at them.</summary>
    static HashSet<Brick> Reachable(List<Brick> wall)
    {
      double top = wall.Max(b => b.Y1), bottom = wall.Min(b => b.Y0), gap = Bonds.Joint + 0.02;
      bool Touch(Brick a, Brick b)
      {
        double ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), oy = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
        return (ox > 0.2 && oy > -gap && oy <= 1e-9) || (oy > 0.2 && ox > -gap && ox <= 1e-9);
      }
      var reached = new HashSet<Brick>(wall.Where(b => b.Breakable && (b.Y1 > top - 1e-6 || b.Y0 < bottom + 1e-6)));
      var queue = new Queue<Brick>(reached);
      while (queue.Count > 0)
      {
        var a = queue.Dequeue();
        foreach (var b in wall)
          if (b.Breakable && !reached.Contains(b) && Touch(a, b))
          {
            reached.Add(b);
            queue.Enqueue(b);
          }
      }
      return reached;
    }

    static void EveryBrickReachable(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: no brick walled in by steel");
      // A glass brick boxed in by steel on every side: the steel around it gives way to concrete.
      var boxed = new List<Brick>
      {
        new Brick(3, 10, 4.5, 10.55, BrickMaterial.Steel), new Brick(4.58, 10, 6.08, 10.55, BrickMaterial.Steel), new Brick(6.16, 10, 7.66, 10.55, BrickMaterial.Steel),
        new Brick(3, 9.37, 4.5, 9.92, BrickMaterial.Steel), new Brick(4.58, 9.37, 6.08, 9.92, BrickMaterial.Glass), new Brick(6.16, 9.37, 7.66, 9.92, BrickMaterial.Steel),
        new Brick(3, 8.74, 4.5, 9.29, BrickMaterial.Steel), new Brick(4.58, 8.74, 6.08, 9.29, BrickMaterial.Steel), new Brick(6.16, 8.74, 7.66, 9.29, BrickMaterial.Steel),
      };
      int demoted = Bonds.OpenUp(boxed, 13, 17);
      var centre = boxed[4];
      bool sides = boxed[1].Material == BrickMaterial.Concrete && boxed[3].Material == BrickMaterial.Concrete && boxed[5].Material == BrickMaterial.Concrete && boxed[7].Material == BrickMaterial.Concrete;
      bool corners = boxed[0].Material == BrickMaterial.Steel && boxed[8].Material == BrickMaterial.Steel;
      check(demoted == 4 && sides && corners && centre.Material == BrickMaterial.Glass && boxed[1].Hits == 3, "a glass brick boxed in by steel: the four steel sides become concrete, the corners stay steel");
      var open = new List<Brick> { new Brick(3, 10, 4.5, 10.55, BrickMaterial.Steel), new Brick(4.58, 10, 6.08, 10.55, BrickMaterial.Glass), new Brick(6.16, 10, 7.66, 10.55, BrickMaterial.Steel) };
      check(Bonds.OpenUp(open, 13, 17) == 0 && open[0].Material == BrickMaterial.Steel, "a brick the ball can reach from above or below leaves its steel neighbours alone");

      // The bonds that could wall a brick in (headers with three steel neighbours, edge bricks): a few hundred walls each.
      foreach (var type in new[] { BondType.English, BondType.Flemish, BondType.Stack, BondType.Stretcher, BondType.Arch })
      {
        int sealedBricks = 0, steel = 0, walls = 0;
        for (int seed = 0; seed < 300; seed++)
        {
          var wall = Bonds.Lay(type, 6 + seed % 10, 13, 15.8, 10, new Random(seed));
          walls++;
          steel += wall.Count(b => !b.Breakable);
          if (type == BondType.Arch) continue;
          var reached = Reachable(wall);
          sealedBricks += wall.Count(b => b.Breakable && !reached.Contains(b));
        }
        check(sealedBricks == 0 && steel > walls, $"{type}: {walls} walls at levels 6–15, every breakable brick reachable, {steel / (double)walls:F1} steel bricks a wall");
      }
    }

    static void HeldBalls(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: balls held on the paddle");
      var g = Empty(BreakoutMode.Zen);
      g.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Steel), new Brick(11.8, 15, 12.8, 15.5, BrickMaterial.Glass) });
      g.Apply(PowerKind.Wide);
      Returns(g, g.AddBall(6.5, 6, 0.8, -8), BreakoutGame.WideHits - 1);
      g.Apply(PowerKind.Sticky);
      // The last wide return is a sticky catch near the wide paddle's end.
      g.AddBall(6.5, 16, 0, 1);
      g.TargetX = 6.5;
      for (int i = 0; i < 60; i++) g.Tick(1 / 120.0);
      g.TargetX = g.PaddleX;
      var ball = g.AddBall(g.PaddleX + 1.7, 3, 0, -9);
      RunUntil(g, () => ball.Held, 2);
      g.Tick(1 / 120.0);
      double half = g.PaddleWidthNow / 2;
      check(ball.Held && g.WideLeft == 0 && Math.Abs(ball.X - g.PaddleX) <= half + 1e-9 && Math.Abs(ball.HeldOffset) <= half + 1e-9,
        $"a sticky catch that uses up wide keeps the ball on the narrower paddle (offset {ball.HeldOffset:F2} of {half:F2})");
      g.Launch();
      check(!ball.Held && ball.Vy > 0 && Math.Abs(Math.Atan2(ball.Vx, ball.Vy)) <= BreakoutGame.MaxAngle + 1e-9, "and it launches from the paddle's edge");

      var multi = Empty();
      multi.SetWall(new[] { new Brick(0.2, 15, 1.2, 15.5, BrickMaterial.Glass) });
      multi.Apply(PowerKind.Sticky);
      multi.TargetX = multi.PaddleX;
      var mb = multi.AddBall(multi.PaddleX, 3, 0, -9);
      RunUntil(multi, () => mb.Held, 2);
      multi.Apply(PowerKind.Multi);
      check(mb.Held == false && multi.Balls.Count == 3 && multi.Balls.All(b => !b.Held && b.Vy > 0), "multi caught while the ball is stuck lets it go and splits it");
    }

    static void Facade(Action<bool, string> check)
    {
      Console.WriteLine("Rebound: the tower facade");
      var halves = new List<Brick> { new Brick(0, 0, 6.5, 1, BrickMaterial.Glass), new Brick(6.5, 0, 13, 1, BrickMaterial.Concrete) };
      var split = new TowerFacade(halves, 13);
      bool sides = Enumerable.Range(0, TowerFacade.Floors).All(f => Enumerable.Range(0, TowerFacade.Columns).All(c =>
        split.Panels[f, c] == (c < TowerFacade.Columns / 2 ? PanelKind.Vision : PanelKind.Spandrel)));
      check(sides, "each panel takes the brick behind it: glass → vision glass, concrete → spandrel");

      var wall = Bonds.Lay(BondType.Stretcher, 4, 13, 15.8, 10, new Random(1));
      var t = new TowerFacade(wall, 13);
      check(t.PanelCount == TowerFacade.Columns * TowerFacade.Floors && t.Storeys == 24 && t.Width == 36,
        $"{TowerFacade.Columns} × {TowerFacade.Floors} panels, 36 m wide, {t.Storeys} storeys, {t.Top:F1} m to the crown");
      check(t.VisionPanels + t.FrittedPanels + t.OpaquePanels + t.FinPanels == t.PanelCount && t.Panels.Cast<PanelKind>().Distinct().Count() >= 3,
        $"the bond becomes the facade pattern ({t.VisionPanels} vision, {t.FrittedPanels} fritted, {t.OpaquePanels} spandrel, {t.FinPanels} fin)");
      check(t.Wwr > 0.25 && t.Wwr < 0.8 && t.UValue > 0.4 && t.UValue < 1.6 && t.GValue > 0.15 && t.GValue < 0.45,
        $"WWR {t.Wwr:P0}, U {t.UValue:F2} W/m²K, g {t.GValue:F2}, {t.CarbonTonnes:F0} t CO₂e");
      foreach (var b in wall.Where((b, i) => i % 3 == 0 && b.Breakable)) b.Hits = 0;
      var after = new TowerFacade(wall, 13);
      int clear = after.Panels.Cast<PanelKind>().Count(k => k == PanelKind.Clear);
      check(clear > 50 && after.Wwr >= t.Wwr && after.GValue > t.GValue && after.Vlt > t.Vlt, $"broken bricks open up as clear glass ({clear} panels, WWR {after.Wwr:P0}, g {after.GValue:F2})");
    }
  }
}
