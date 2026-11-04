using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Games.Snake.Core;

namespace Moyu.Core.Tests
{
  /// <summary>Rules of the Serpentine surface snake (no Rhino needed).</summary>
  static class TestsSnake
  {
    public static void Run(Action<bool, string> check)
    {
      Topology(check);
      Movement(check);
      Turns(check);
      Walls(check);
      SelfAndTail(check);
      Fruit(check);
      Bombs(check);
      PowerUps(check);
      Slope(check);
      LevelsAndBlitz(check);
      Spawning(check);
      Replay(check);
      Metrics(check);
      Landscape(check);
    }

    static SnakeGame Game(SurfaceGrid grid, int length = 4, SnakeMode mode = SnakeMode.Classic, bool items = false) =>
      new SnakeGame(grid, new SnakeSettings { Mode = mode, Seed = 7, StartLength = length, SpawnItems = items });

    /// <summary>Advances exactly one step (the grid is flat, so the step time does not change).</summary>
    static void StepOnce(SnakeGame g) => g.Tick(1 / g.Speed + 1e-9);

    static Cell Ahead(SnakeGame g, int n = 1) => new Cell(g.Head.U + n * g.Heading.du, g.Head.V + n * g.Heading.dv);

    static void Topology(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: grid topology");
      var open = SurfaceGrid.Flat(6, 4);
      check(!open.TryStep(new Cell(5, 1), 1, 0, out _) && !open.TryStep(new Cell(2, 0), 0, -1, out _), "open edges are walls");
      check(open.TryStep(new Cell(2, 1), 1, 0, out var n) && n == new Cell(3, 1), "a step inside moves one cell");
      var tube = SurfaceGrid.Flat(6, 4, wrapU: true);
      check(tube.TryStep(new Cell(5, 1), 1, 0, out var w) && w == new Cell(0, 1), "a closed direction wraps round (cylinder)");
      check(!tube.TryStep(new Cell(2, 3), 0, 1, out _), "the open direction of a cylinder is still a wall");
      var torus = SurfaceGrid.Flat(6, 4, true, true);
      check(torus.TryStep(new Cell(0, 0), 0, -1, out var t) && t == new Cell(0, 3), "a torus wraps both ways");
      check(torus.GridDistance(new Cell(0, 0), new Cell(5, 3)) == 2 && open.GridDistance(new Cell(0, 0), new Cell(5, 3)) == 8,
        "grid distance takes the short way round only where the surface is closed");
      var blocked = new bool[6 * 4];
      blocked[1 * 6 + 3] = true;
      var holed = SurfaceGrid.Flat(6, 4, blocked: blocked);
      check(holed.IsBlocked(new Cell(3, 1)) && !holed.TryStep(new Cell(2, 1), 1, 0, out _), "a trimmed-away cell is an obstacle");
      check(holed.FreeCells().Count() == 23, "free cells skip the obstacle");
      var ramp = SurfaceGrid.Flat(6, 4, height: (u, v) => 0.3 * u);
      check(Math.Abs(ramp.Slope(new Cell(1, 1), new Cell(2, 1)) - 0.3) < 1e-9 && Math.Abs(ramp.Slope(new Cell(2, 1), new Cell(1, 1)) + 0.3) < 1e-9,
        "slope is rise over run, positive uphill");
    }

    static void Movement(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: start and movement");
      var g = Game(SurfaceGrid.Flat(10, 7));
      check(g.Length == 4 && g.Head == new Cell(5, 3) && g.Heading == (1, 0), $"starts in the middle, 4 long, heading +u (head {g.Head})");
      check(g.Body.SequenceEqual(new[] { new Cell(5, 3), new Cell(4, 3), new Cell(3, 3), new Cell(2, 3) }), "body trails straight behind the head");
      double v1 = SnakeGame.SpeedFor(1);
      check(Math.Abs(g.Speed - v1) < 1e-9, $"level 1 runs {v1} cells per second (got {g.Speed:F3})");
      g.Tick(0.5 / v1);
      check(g.Head == new Cell(5, 3) && Math.Abs(g.StepProgress - 0.5) < 1e-9, "half a step in, the head has not moved yet");
      g.Tick(0.5 / v1 + 1e-9);
      check(g.Head == new Cell(6, 3) && g.PreviousHead == new Cell(5, 3) && g.RemovedTail == new Cell(2, 3), "one step: head forward, tail cell left behind");
      check(g.Length == 4 && g.Occupies(new Cell(3, 3)) && !g.Occupies(new Cell(2, 3)), "length and occupancy follow the body");
      g.Pause();
      g.Tick(1);
      check(g.Head == new Cell(6, 3) && g.State == SnakeState.Paused, "paused: time does not move the snake");
      g.Resume();
      StepOnce(g);
      check(g.Head == new Cell(7, 3) && g.State == SnakeState.Playing, "resumed");
      var tube = Game(SurfaceGrid.Flat(6, 5, wrapU: true));
      for (int i = 0; i < 3; i++) StepOnce(tube);
      check(tube.Head == new Cell(0, 2) && tube.State == SnakeState.Playing, $"on a cylinder the snake runs off one side and on at the other (head {tube.Head})");
      var small = Game(SurfaceGrid.Flat(3, 3), length: 2);
      check(small.Length == 2, "fits a 3 × 3 field with a short snake");
      bool threw = false;
      try { Game(SurfaceGrid.Flat(3, 3), length: 6); }
      catch (ArgumentException) { threw = true; }
      check(threw, "refuses a field with no room for the snake");
    }

    static void Turns(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: turn queue");
      var g = Game(SurfaceGrid.Flat(12, 9));
      check(!g.Turn(-1, 0), "cannot reverse onto the body");
      check(!g.Turn(1, 0), "turning to the current heading does nothing");
      check(!g.Turn(1, 1), "diagonals are not turns");
      check(g.Turn(0, 1) && g.Turn(-1, 0), "two quick turns are both queued");
      check(!g.Turn(0, -1), "a third waits until the queue has room");
      StepOnce(g);
      check(g.Heading == (0, 1) && g.Head == new Cell(6, 5), "first queued turn on the next step");
      StepOnce(g);
      check(g.Heading == (-1, 0) && g.Head == new Cell(5, 5), "second queued turn on the step after");
      var r = Game(SurfaceGrid.Flat(12, 9));
      check(r.TurnLeft() && r.PlannedHeading == (0, 1), "left of +u is +v (anticlockwise seen from the normal)");
      check(r.TurnRight() && r.PlannedHeading == (1, 0), "right turns back");
    }

    static void Walls(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: walls and obstacles");
      var g = Game(SurfaceGrid.Flat(8, 5));
      for (int i = 0; i < 3; i++) StepOnce(g);
      check(g.State == SnakeState.Playing && g.Head == new Cell(7, 2), "runs up to the edge");
      StepOnce(g);
      check(g.State == SnakeState.Dead && g.Cause == DeathCause.Wall && g.IsOver, "hits the open edge");
      var zen = Game(SurfaceGrid.Flat(8, 5), mode: SnakeMode.Zen);
      for (int i = 0; i < 6; i++) StepOnce(zen);
      check(zen.State == SnakeState.Playing && zen.Head == new Cell(7, 2), "zen: waits at the wall instead of dying");
      zen.Turn(0, 1);
      StepOnce(zen);
      check(zen.Head == new Cell(7, 3), "zen: carries on after a turn");
      var blocked = new bool[8 * 5];
      blocked[2 * 8 + 6] = true;
      var h = Game(SurfaceGrid.Flat(8, 5, blocked: blocked));
      StepOnce(h);
      StepOnce(h);
      check(h.State == SnakeState.Dead && h.Cause == DeathCause.Wall, "a hole in a trimmed surface counts as a wall");
    }

    static void SelfAndTail(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: self collision");
      var g = Game(SurfaceGrid.Flat(12, 9), length: 5);
      g.Turn(0, 1);
      g.Turn(-1, 0);
      StepOnce(g);
      StepOnce(g);
      g.Turn(0, -1);
      StepOnce(g);
      check(g.State == SnakeState.Dead && g.Cause == DeathCause.Self, "a 5-long snake turning a tight square bites itself");
      var tail = Game(SurfaceGrid.Flat(12, 9), length: 4);
      tail.Turn(0, 1);
      tail.Turn(-1, 0);
      StepOnce(tail);
      StepOnce(tail);
      tail.Turn(0, -1);
      StepOnce(tail);
      check(tail.State == SnakeState.Playing && tail.Head == new Cell(5, 4), "a 4-long snake may follow its own tail, which moves out of the way");
      var zen = Game(SurfaceGrid.Flat(12, 9), length: 5, mode: SnakeMode.Zen);
      zen.Turn(0, 1);
      zen.Turn(-1, 0);
      StepOnce(zen);
      StepOnce(zen);
      zen.Turn(0, -1);
      StepOnce(zen);
      check(zen.State == SnakeState.Playing && zen.Length == 5, "zen: passes over itself");
    }

    static void Fruit(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: fruit, growth and combo");
      var g = Game(SurfaceGrid.Flat(30, 9, true, true));
      g.PlaceItem(ItemKind.Apple, Ahead(g, 1));
      g.PlaceItem(ItemKind.Grape, Ahead(g, 2));
      g.PlaceItem(ItemKind.Melon, Ahead(g, 6));
      StepOnce(g);
      check(g.Score == 10 && g.Combo == 1 && g.FruitsEaten == 1 && g.Length == 4, "apple: 10 points, growth starts next step");
      check(g.LastEvent?.Kind == ItemKind.Apple && g.LastEvent.Points == 10, "the event names the apple");
      StepOnce(g);
      check(g.Length == 5 && g.RemovedTail == null, "grew by one (tail stayed put)");
      check(g.Score == 10 + 30 * 2 && g.Combo == 2, $"grape within 4 s: 30 × combo 2 (score {g.Score})");
      for (int i = 0; i < 3; i++) StepOnce(g);
      check(g.Length == 8, $"grape adds 3 (length {g.Length})");
      StepOnce(g);
      check(g.Score == 70 + 80 * 3 && g.Combo == 3, $"melon: 80 × combo 3 (score {g.Score})");
      for (int i = 0; i < 6; i++) StepOnce(g);
      check(g.Length == 13, $"melon adds 5 (length {g.Length})");
      check(g.Items.Count == 0, "eaten items leave the field");
      for (int i = 0; i < 25; i++) StepOnce(g); // 5 s without fruit
      g.PlaceItem(ItemKind.Apple, Ahead(g, 1));
      StepOnce(g);
      check(g.Combo == 1 && g.Score == 320, "a pause longer than 4 s resets the combo");
    }

    static void Bombs(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: bombs");
      var g = Game(SurfaceGrid.Flat(30, 9, true, true), length: 8);
      g.PlaceItem(ItemKind.Apple, Ahead(g, 1));
      g.PlaceItem(ItemKind.Bomb, Ahead(g, 3));
      StepOnce(g);
      StepOnce(g);
      var tailBefore = g.Body.Skip(5).Take(3).ToArray(); // the step itself drops the last cell first
      StepOnce(g);
      check(g.Length == 6, $"bomb cuts 3 segments off the tail (length {g.Length})");
      check(g.Score == 0 && g.Combo == 0, "bomb costs up to 50 points and breaks the combo");
      check(g.LastEvent.Kind == ItemKind.Bomb && g.LastEvent.Lost.Length == 3, "the event lists the lost segments for the debris effect");
      check(tailBefore.All(c => g.LastEvent.Lost.Contains(c)), "the lost segments are the old tail");
      check(g.State == SnakeState.Playing, "still alive");
      var rich = Game(SurfaceGrid.Flat(30, 9, true, true), length: 8);
      for (int i = 1; i <= 6; i++) rich.PlaceItem(ItemKind.Apple, Ahead(rich, i));
      for (int i = 0; i < 6; i++) StepOnce(rich);
      long before = rich.Score;
      rich.PlaceItem(ItemKind.Bomb, Ahead(rich, 1));
      StepOnce(rich);
      check(rich.Score == before - 50, $"bomb: −50 ({before} → {rich.Score})");
      var shorty = Game(SurfaceGrid.Flat(30, 9, true, true), length: 4);
      shorty.PlaceItem(ItemKind.Bomb, Ahead(shorty, 1));
      StepOnce(shorty);
      check(shorty.State == SnakeState.Dead && shorty.Cause == DeathCause.Bomb, "a snake too short to lose 3 segments dies");
      var owed = Game(SurfaceGrid.Flat(30, 9, true, true), length: 8);
      owed.PlaceItem(ItemKind.Melon, Ahead(owed, 1));
      owed.PlaceItem(ItemKind.Bomb, Ahead(owed, 2));
      StepOnce(owed);
      StepOnce(owed);
      check(owed.Length == 6, $"a bomb right after a melon cuts to 6 (length {owed.Length})");
      for (int i = 0; i < 5; i++) StepOnce(owed);
      check(owed.Length == 6, $"and the growth the melon still owed is lost with the tail (length {owed.Length})");
    }

    static void PowerUps(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: ice and ghost");
      var g = Game(SurfaceGrid.Flat(30, 9, true, true));
      g.PlaceItem(ItemKind.Ice, Ahead(g, 1));
      StepOnce(g);
      double v1 = SnakeGame.SpeedFor(1);
      check(g.IceActive && Math.Abs(g.Speed - v1 * 0.6) < 1e-9, $"ice: speed × 0.6 (got {g.Speed:F3})");
      g.Boost = true;
      g.Tick(0.01);
      check(Math.Abs(g.Speed - v1 * 0.6 * 1.8) < 1e-9, "sprint stacks on top of ice");
      g.Boost = false;
      while (g.Elapsed < 7) g.Tick(0.05);
      check(!g.IceActive && Math.Abs(g.Speed - v1) < 1e-9, "ice wears off after 6 s");

      var ghost = Game(SurfaceGrid.Flat(14, 9), length: 6);
      ghost.PlaceItem(ItemKind.Ghost, Ahead(ghost, 1));
      StepOnce(ghost);
      ghost.Turn(0, 1);
      ghost.Turn(-1, 0);
      StepOnce(ghost);
      StepOnce(ghost);
      ghost.Turn(0, -1);
      StepOnce(ghost);
      check(ghost.GhostActive && ghost.State == SnakeState.Playing, "ghost: crosses its own body");
      StepOnce(ghost);
      ghost.Turn(1, 0);
      for (int i = 0; i < 2; i++) StepOnce(ghost);
      check(ghost.State == SnakeState.Playing, "and keeps going");
    }

    static void Slope(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: slope and speed");
      var up = new SnakeGame(SurfaceGrid.Flat(12, 5, height: (u, v) => 0.3 * u), new SnakeSettings { Seed = 1, SpawnItems = false });
      var down = new SnakeGame(SurfaceGrid.Flat(12, 5, height: (u, v) => -0.3 * u), new SnakeSettings { Seed = 1, SpawnItems = false });
      var off = new SnakeGame(SurfaceGrid.Flat(12, 5, height: (u, v) => 0.3 * u), new SnakeSettings { Seed = 1, SpawnItems = false, SlopeAffectsSpeed = false });
      double v1 = SnakeGame.SpeedFor(1);
      check(up.Speed < v1 && Math.Abs(up.SlopeFactor - 0.625) < 1e-9, $"uphill is slower (factor {up.SlopeFactor:F3})");
      check(down.Speed > v1 && Math.Abs(down.SlopeFactor - 1.375) < 1e-9, $"downhill is faster (factor {down.SlopeFactor:F3})");
      check(Math.Abs(off.Speed - v1) < 1e-9, "slope effect can be switched off");
      var cliff = new SnakeGame(SurfaceGrid.Flat(12, 5, height: (u, v) => 3.0 * u), new SnakeSettings { Seed = 1, SpawnItems = false });
      var drop = new SnakeGame(SurfaceGrid.Flat(12, 5, height: (u, v) => -3.0 * u), new SnakeSettings { Seed = 1, SpawnItems = false });
      check(Math.Abs(cliff.SlopeFactor - 0.6) < 1e-9 && Math.Abs(drop.SlopeFactor - 1.5) < 1e-9, "at most 40 % slower and 50 % faster");
      check(up.Turn(0, 1) && Math.Abs(up.Speed - v1 * 0.625) < 1e-9, "a queued turn takes effect on the next step's slope");
      up.Tick(0.001);
      check(Math.Abs(up.SlopeFactor - 1) < 1e-9, "turning across the slope is back to normal speed");
      var torn = new SnakeGame(SurfaceGrid.Flat(12, 5, height: (u, v) => u == 7 ? double.NaN : 0.3 * u), new SnakeSettings { Seed = 1, SpawnItems = false });
      check(Math.Abs(torn.SlopeFactor - 1) < 1e-9 && Math.Abs(torn.Speed - v1) < 1e-9, "an invalid surface sample (NaN height) ahead counts as flat");
      StepOnce(torn);
      check(torn.Head == new Cell(7, 2) && torn.State == SnakeState.Playing, "and the snake keeps moving over it");
    }

    static void LevelsAndBlitz(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: levels and blitz");
      var g = Game(SurfaceGrid.Flat(30, 9, true, true));
      for (int i = 1; i <= 5; i++) g.PlaceItem(ItemKind.Apple, Ahead(g, i));
      for (int i = 0; i < 4; i++) StepOnce(g);
      check(g.Level == 1, "four fruit: still level 1");
      StepOnce(g);
      check(g.Level == 2 && g.LevelUpTime == g.Elapsed, "five fruit: level 2");
      check(Math.Abs(g.Speed - SnakeGame.SpeedFor(2)) < 1e-9 && SnakeGame.SpeedFor(2) > SnakeGame.SpeedFor(1), $"and faster ({g.Speed:F2} cells/s)");
      check(Math.Abs(SnakeGame.SpeedFor(1) - 3.4) < 1e-9 && Math.Abs(SnakeGame.SpeedFor(40) - 15) < 1e-9, "speed runs from an easy 3.4 up to a cap of 15");
      check(SnakeGame.LevelName(1) != SnakeGame.LevelName(13), "levels have names");
      var b = new SnakeGame(SurfaceGrid.Flat(20, 9, true, true), new SnakeSettings { Mode = SnakeMode.Blitz, BlitzSeconds = 2, Seed = 1, SpawnItems = false });
      while (!b.IsOver && b.Elapsed < 5) b.Tick(0.05);
      check(b.State == SnakeState.TimeUp && Math.Abs(b.Elapsed - 2) < 0.06 && b.TimeLeft == 0, $"blitz ends when the clock runs out ({b.Elapsed:F2} s)");
      check(double.IsPositiveInfinity(g.TimeLeft), "classic has no clock");
    }

    static void Spawning(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: item spawning");
      var blocked = new bool[16 * 16];
      for (int i = 0; i < 16; i++) blocked[i * 16 + 3] = i % 4 != 0;
      var grid = SurfaceGrid.Flat(16, 16, true, true, blocked: blocked);
      var zen = new SnakeGame(grid, new SnakeSettings { Mode = SnakeMode.Zen, Seed = 3 });
      bool apples = true, clean = true;
      var seen = new HashSet<ItemKind>();
      while (zen.Elapsed < 60)
      {
        zen.Tick(0.02);
        apples &= zen.Items.Count(i => i.Kind == ItemKind.Apple) == SnakeGame.Apples;
        clean &= zen.Items.All(i => !grid.IsBlocked(i.Cell) && !zen.Occupies(i.Cell)) && zen.Items.Select(i => i.Cell).Distinct().Count() == zen.Items.Count;
        foreach (var i in zen.Items) seen.Add(i.Kind);
      }
      check(apples, "always 2 apples on the field");
      check(clean, "items never sit on an obstacle, on the snake or on each other");
      check(seen.Contains(ItemKind.Grape) && seen.Contains(ItemKind.Melon) && (seen.Contains(ItemKind.Ice) || seen.Contains(ItemKind.Ghost)),
        $"grapes, melons and power-ups turn up within a minute ({string.Join(", ", seen)})");
      check(!seen.Contains(ItemKind.Bomb), "zen has no bombs");

      var early = new SnakeGame(grid, new SnakeSettings { StartLevel = SnakeGame.BombsFrom - 1, Seed = 3, StartLength = 3 });
      bool none = true;
      while (!early.IsOver && early.Elapsed < 30)
      {
        early.Tick(0.02);
        none &= early.Level >= SnakeGame.BombsFrom || !early.Items.Any(i => i.Kind == ItemKind.Bomb);
      }
      check(none && early.Elapsed > 10, $"no bombs before level {SnakeGame.BombsFrom} ({early.Elapsed:F0} s watched)");

      var classic = new SnakeGame(grid, new SnakeSettings { StartLevel = SnakeGame.BombsFrom, Seed = 3, StartLength = 3 });
      var known = new HashSet<Item>();
      bool far = true;
      int bombs = 0;
      while (!classic.IsOver && classic.Elapsed < 30)
      {
        classic.Tick(0.02);
        foreach (var item in classic.Items.Where(i => i.Kind == ItemKind.Bomb && known.Add(i)))
        {
          bombs++;
          far &= grid.GridDistance(item.Cell, classic.Head) >= 3;
        }
      }
      check(bombs > 0, $"bombs appear from level {SnakeGame.BombsFrom} ({bombs} seen)");
      check(far, "never right in front of the head");
      check(classic.Items.Where(i => i.Kind == ItemKind.Bomb).All(i => i.Expires - i.Born <= 12 + 1e-9), "bombs go away after 12 s");

      // A single open row of twelve cells with an eight-long snake: crowded, so items are drawn from the free list.
      var row = new bool[12 * 4];
      for (int i = 12; i < row.Length; i++) row[i] = true;
      var crowded = new SnakeGame(SurfaceGrid.Flat(12, 4, true, true, blocked: row), new SnakeSettings { Mode = SnakeMode.Zen, Seed = 5, StartLength = 8 });
      check(crowded.Items.Count == 2 && crowded.Items.All(i => !crowded.Occupies(i.Cell) && i.Cell.V == 0), "a crowded field still gets its two apples, on free cells");
      bool tidy = true;
      while (crowded.Elapsed < 20)
      {
        crowded.Tick(0.02);
        tidy &= crowded.Items.All(i => !crowded.Occupies(i.Cell) && i.Cell.V == 0) && crowded.Items.Count <= Math.Max(0, 12 - crowded.Length);
      }
      check(tidy && crowded.Length >= 12 && crowded.Items.Count == 0, $"it eats its way to a full field, where nothing more can spawn (length {crowded.Length}, {crowded.Items.Count} items)");
    }

    static void Replay(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: same seed, same game");
      string Run(int seed)
      {
        var g = new SnakeGame(SurfaceGrid.Flat(20, 14, true, true), new SnakeSettings { Mode = SnakeMode.Zen, Seed = seed });
        for (int i = 0; i < 1500; i++)
        {
          if (i % 37 == 0) g.TurnLeft();
          if (i % 91 == 0) g.TurnRight();
          g.Tick(1 / 60.0);
        }
        return string.Join(";", g.Body) + "|" + string.Join(";", g.Items.Select(t => t.Kind + "@" + t.Cell)) + "|" + g.Score;
      }
      check(Run(11) == Run(11), "replaying the same seed and inputs gives the same body, items and score");
      check(Run(11) != Run(12), "a different seed gives a different game");
    }

    static void Metrics(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: circulation metrics");
      var flat = SurfaceGrid.Flat(10, 6);
      var straight = Enumerable.Range(1, 5).Select(u => new Cell(u, 2)).ToList();
      var m = RouteMetrics.From(straight, flat, 1);
      check(Math.Abs(m.Length - 4) < 1e-9 && m.Gain == 0 && m.MaxSlope == 0 && Math.Abs(m.Accessible - 1) < 1e-9, "flat straight route: 4 m, no climb, fully accessible");
      check(Math.Abs(RouteMetrics.From(straight, flat, 0.001).Length - 0.004) < 1e-12, "lengths convert to metres");
      var ramp = SurfaceGrid.Flat(10, 6, height: (u, v) => 0.1 * u);
      var r = RouteMetrics.From(straight, ramp, 1);
      check(Math.Abs(r.Gain - 0.4) < 1e-9 && Math.Abs(r.AverageSlope - 0.1) < 1e-9 && Math.Abs(r.MaxSlope - 0.1) < 1e-9 && r.Accessible == 0,
        "10 % ramp: 0.4 m climb, 10 % slopes, none accessible");
      var back = RouteMetrics.From(straight.AsEnumerable().Reverse().ToList(), ramp, 1);
      check(back.Gain == 0 && Math.Abs(back.AverageSlope - 0.1) < 1e-9, "walking down: no climb, same slope");
      // A closed site can put two cell centres one above the other (the torus girth): that step is vertical.
      var xs = new double[9];
      var ys = new double[9];
      var zs = new double[9];
      for (int i = 0; i < 9; i++) { xs[i] = i % 3; ys[i] = i / 3; }
      xs[5] = xs[4];
      zs[5] = 1;
      var cliff = new SurfaceGrid(3, 3, false, false, xs, ys, zs);
      var up = RouteMetrics.From(new List<Cell> { new Cell(0, 1), new Cell(1, 1), new Cell(2, 1) }, cliff, 1);
      check(up.MaxSlope == RouteMetrics.MaxReported && up.AverageSlope <= RouteMetrics.MaxReported && Math.Abs(up.Gain - 1) < 1e-9,
        $"a vertical step reports at most 100 % slope (max {up.MaxSlope * 100:F0} %, average {up.AverageSlope * 100:F0} %)");
      var grid = SurfaceGrid.Flat(28, 18);
      var decoy = RouteMetrics.Decoy(grid, 5);
      bool continuous = decoy.Zip(decoy.Skip(1), (a, b) => grid.GridDistance(a, b)).All(d => d == 1);
      check(decoy.Count > 20 && continuous && decoy.All(c => !grid.IsBlocked(c)), $"decoy route is long, continuous and on free cells ({decoy.Count} cells)");
      check(decoy.Distinct().Count() == decoy.Count, "decoy route never doubles back on itself");
    }

    static void Landscape(Action<bool, string> check)
    {
      Console.WriteLine("Serpentine: the boss-key landscape plan");
      // A meadow with a steep bank along its far side, a path straight across and two plazas.
      var grid = SurfaceGrid.Flat(28, 20, height: (u, v) => v >= 16 ? (v - 15) * 0.9 : 0);
      var route = Enumerable.Range(3, 22).Select(u => new Cell(u, 8)).ToList();
      var nodes = new[] { new Cell(6, 4), new Cell(20, 12) };
      var plan = new LandscapePlan(grid, route, nodes, 5);
      var again = new LandscapePlan(grid, route, nodes, 5);
      check(plan.Trees.Count > 12 && plan.Trees.Select(t => t.Cell).SequenceEqual(again.Trees.Select(t => t.Cell)), $"{plan.Trees.Count} trees, the same every time for one seed");
      check(plan.Trees.All(t => !route.Contains(t.Cell) && !nodes.Contains(t.Cell) && route.All(r => grid.GridDistance(r, t.Cell) >= 2)), "no tree on the path, its verge or a plaza");
      check(plan.Trees.All(t => LandscapePlan.SteepestFrom(grid, t.Cell) <= LandscapePlan.MaxPlantingSlope), "none on the steep bank");
      int avenue = route.Count(r => plan.Trees.Any(t => grid.GridDistance(r, t.Cell) == 2 && (t.Cell.U == r.U)));
      check(avenue >= 3, $"an avenue along the path ({avenue} trees two cells off it)");
      check(plan.Trees.Count(t => t.Cell.U <= 1 || t.Cell.U >= 26 || t.Cell.V <= 1) > plan.Trees.Count / 3, "a woodland belt along the open edges");
      check(plan.Benches.Count == 3 && plan.Benches.All(b => b.index % 7 == 3) && plan.People.Count >= route.Count / 8 + nodes.Length, $"{plan.Benches.Count} benches every seven cells, {plan.People.Count} people");
      check(plan.ShadeShare > 0.2 && plan.ShadeShare <= 1, $"{plan.ShadeShare:P0} of the path in or beside shade");
    }
  }
}
