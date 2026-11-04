using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Games.Stack.Core;

namespace Moyu.Core.Tests
{
  /// <summary>Rules of the VoxStack falling-block game (no Rhino needed).</summary>
  static class TestsStack
  {
    public static void Run(Action<bool, string> check)
    {
      Orientations(check);
      Shapes(check);
      WellLayers(check);
      BagFairness(check);
      Gravity(check);
      LockDelay(check);
      ClearsAndScore(check);
      TopOutAndZen(check);
      KicksAndHold(check);
      Replay(check);
      Blitz(check);
      Metrics(check);
    }

    static StackGame Game(int nx, int ny, int nz, params PieceDef[] pieces) =>
      new StackGame(new StackSettings { Nx = nx, Ny = ny, Nz = nz, Seed = 1, Pieces = pieces });

    static void Orientations(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: orientations of the built-in pieces");
      var expected = new (PieceDef p, int n)[]
      {
        (PieceSets.I4, 3), (PieceSets.O4, 3), (PieceSets.L4, 24), (PieceSets.T4, 12), (PieceSets.S4, 12),
        (PieceSets.SpiralL, 12), (PieceSets.SpiralR, 12), (PieceSets.Tripod, 8), (PieceSets.I3, 3), (PieceSets.Cross, 3),
      };
      foreach (var (p, n) in expected) check(p.OrientationCount == n, $"{p.Name}: {p.OrientationCount} orientations (expected {n})");
      check(!Polycube.SameShape(PieceSets.SpiralL.Cells, PieceSets.SpiralR.Cells), "the two spirals are mirror images, not rotations of each other");
      check(Polycube.SameShape(PieceSets.L4.Cells, PieceSets.L4.Cells.Select(c => new Int3(-c.X, c.Y, c.Z))), "a flat L flipped over is still an L in 3D");
      var turned = PieceSets.Tripod.Cells;
      for (int i = 0; i < 4; i++) turned = turned.Select(c => Int3.Rotate(c, Int3.UnitX, true)).ToArray();
      check(turned.SequenceEqual(PieceSets.Tripod.Cells), "four quarter turns give the identity");
      foreach (var preset in new[] { PieceSets.Easy, PieceSets.Standard, PieceSets.Hard })
      {
        var set = PieceSets.Get(preset);
        bool distinct = set.All(a => set.Count(b => Polycube.SameShape(a.Cells, b.Cells)) == 1);
        check(distinct && set.All(p => PieceDef.Validate(p.Cells, 5, 5, 12) == null), $"{PieceSets.Names[preset]}: {set.Count} distinct, valid pieces");
      }
    }

    static void Shapes(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: custom unit parsing and validation");
      var pavilion = Polycube.ParsePattern("###/#.#|#..");
      check(pavilion.Length == 6 && Polycube.Size(pavilion).Equals(new Int3(3, 2, 2)), $"pattern → 6 voxels, 3×2×2 (got {pavilion.Length}, {Polycube.Size(pavilion)})");
      check(PieceDef.Validate(pavilion, 5, 5, 12) == null, "pavilion is a valid unit");
      check(Math.Abs(new PieceDef("p", pavilion, 0, isCustom: true).ScoreMultiplier - 1.6) < 1e-9, "6 non-planar voxels score ×1.6");
      var diagonal = new[] { new Int3(0, 0, 0), new Int3(1, 1, 0) };
      check(PieceDef.Validate(diagonal, 5, 5, 12)?.Contains("2 pieces") == true, "edge-touching cubes are rejected as 2 pieces");
      var longOne = Polycube.ParsePattern("#####");
      check(PieceDef.Validate(longOne, 5, 5, 12)?.Contains("exceed") == true, "a 5-long unit is rejected");
      check(PieceDef.Validate(PieceSets.I4.Cells, 3, 3, 12)?.Contains("cannot turn") == true, "a 4-long unit is rejected in a 3×3 well");
      check(PieceDef.Validate(Polycube.ParsePattern("##########/##"), 5, 5, 12)?.Contains("most") == true, "more than 10 voxels is rejected");
      var black = new PieceDef("black", PieceSets.O4.Cells, 0, isCustom: true);
      check(black.Colour == unchecked((int)0xFF000000) && black.WithWeight(0.5).Colour == black.Colour, "a transparent-black colour becomes opaque black, since the well reads 0 as empty");
      var b = Game(2, 2, 6, black);
      b.HardDrop();
      check(b.Well.Count == 4 && b.State == StackState.Clearing && b.Layers == 1, "so a unit of that colour still fills and clears a layer");
    }

    static void WellLayers(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: well layers");
      var w = new Well(3, 3, 6);
      for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
          w.Set(new Int3(x, y, 1), 7);
      w.Set(new Int3(0, 0, 0), 5);
      w.Set(new Int3(2, 2, 2), 9);
      check(w.FullLayers().SequenceEqual(new[] { 1 }), "layer 1 is full");
      w.RemoveLayers(new[] { 1 });
      check(w.Get(0, 0, 0) == 5 && w.Get(2, 2, 1) == 9 && w.Count == 2 && w.Height == 2, "the layer above drops into the gap, the one below stays");
      check(w.IsFree(new Int3(1, 1, 7)) && !w.IsFree(new Int3(3, 0, 0)) && !w.IsFree(new Int3(0, 0, -1)), "above the rim is free, walls and floor are not");
    }

    static void BagFairness(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: bag randomiser");
      var set = PieceSets.Get(PieceSets.Standard);
      var bag = new Bag(set, new Random(3));
      bool fair = true;
      for (int round = 0; round < 20; round++)
      {
        var drawn = Enumerable.Range(0, set.Count).Select(_ => bag.Next()).ToList();
        fair &= set.All(p => drawn.Count(d => d == p) == 1);
      }
      check(fair, "every bag of 8 deals each standard piece exactly once");
      var half = new Bag(new[] { PieceSets.I4, PieceSets.O4.WithWeight(0.5) }, new Random(4));
      var many = Enumerable.Range(0, 3000).Select(_ => half.Next()).ToList();
      double share = many.Count(p => p.Name == PieceSets.O4.Name) / (double)many.Count;
      check(share > 0.28 && share < 0.39, $"weight 0.5 → about a third of the deals ({share:P0})");
    }

    static void Gravity(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: gravity");
      check(Math.Abs(StackGame.GravityFor(1) - 2.4) < 1e-12 && Math.Abs(StackGame.GravityFor(10) - 1.133) < 1e-3 && Math.Abs(StackGame.GravityFor(15) - 0.746) < 1e-3 && StackGame.GravityFor(40) == 0.15,
        "a slow 2.4 s per cell at L1, 1.13 s at L10, 0.75 s at L15, 0.15 s floor");
      check(Enumerable.Range(1, 20).All(l => StackGame.GravityFor(l + 1) <= StackGame.GravityFor(l)), "and only ever faster");
      var g = Game(5, 5, 12, PieceSets.O4);
      int z0 = g.Position.Z;
      g.Tick(2.39);
      check(g.Position.Z == z0, "nothing moves for the first 2.4 s");
      g.Tick(0.02);
      check(g.Position.Z == z0 - 1, "then one cell down at level 1");
      g.SoftDrop = true;
      long before = g.Score;
      g.Tick(0.1);
      check(g.Position.Z <= z0 - 3, $"soft drop falls about a cell per 35 ms (z {z0 - 1} → {g.Position.Z} in 0.1 s)");
      check(g.Score > before, "soft drop scores");
    }

    static void LockDelay(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: lock delay");
      var g = Game(5, 5, 12, PieceSets.O4);
      g.SoftDrop = true;
      for (int i = 0; i < 100 && g.Position.Z > 0; i++) g.Tick(0.04);
      g.SoftDrop = false;
      check(g.Position.Z == 0 && g.Current != null, "the piece rests on the floor");
      double delay = StackGame.LockDelayFor(1);
      check(Math.Abs(delay - 1.2) < 1e-12 && Math.Abs(StackGame.LockDelayFor(11) - 0.6) < 1e-12 && StackGame.LockDelayFor(20) == StackGame.LockDelay,
        "lock delay 1.2 s at level 1, 0.6 s from level 11");
      g.Tick(delay - 0.1);
      check(g.Current != null && g.Well.Count == 0, $"{delay - 0.1:F1} s after landing it can still move");
      g.Move(Int3.UnitX);
      g.Tick(delay - 0.1);
      check(g.Well.Count == 0, "a move restarts the lock delay");
      g.Tick(0.15);
      check(g.Well.Count == 4 && g.PiecesPlaced == 1, "it locks once the delay runs out");

      Console.WriteLine("VoxStack: an easy start");
      var std = new StackGame(new StackSettings { Seed = 3, Pieces = PieceSets.Get(PieceSets.Standard) });
      bool flatFirst = Enumerable.Range(0, 40).Select(i => std.Upcoming(i)).All(p => p.IsPlanar && p.Size.Z == 1);
      check(flatFirst && std.PoolFor(1).Count == 5, "levels 1–2 deal only the five flat units of the standard set");
      check(std.PoolFor(StackGame.MixedFrom).Count == 8 && std.PoolFor(StackGame.MixedFrom).Where(p => !p.IsPlanar).All(p => p.Weight == 0.5), "level 3 mixes the 3D units in at half weight");
      check(ReferenceEquals(std.PoolFor(StackGame.FullSetFrom), std.Settings.Pieces), "level 4 on: the whole set");
      var mine = new StackGame(new StackSettings { Seed = 3, Pieces = new[] { PieceSets.O4, PieceSets.Tripod } });
      check(ReferenceEquals(mine.PoolFor(1), mine.Settings.Pieces), "a small set is dealt as it is");
      // A preset set plus a unit of the player's own (7 voxels, 3D): it is dealt from the first piece, at full weight.
      var gate = new PieceDef("Gate", new[] { new Int3(0, 0, 0), new Int3(0, 0, 1), new Int3(0, 0, 2), new Int3(1, 0, 2), new Int3(2, 0, 2), new Int3(2, 0, 1), new Int3(2, 0, 0) }, 0, 1.0, true);
      var withGate = new StackGame(new StackSettings { Seed = 3, Nx = 6, Ny = 6, Nz = 14, Pieces = PieceSets.Get(PieceSets.Standard).Concat(new[] { gate }).ToList() });
      var early = withGate.PoolFor(1);
      check(early.Contains(gate) && early.Count == 6 && early.Count(p => !p.IsPlanar || p.Size.Z > 1) == 1, "a custom unit is dealt from level 1 with the flat presets, the 3D presets still wait");
      check(Enumerable.Range(0, 30).Any(i => withGate.Upcoming(i).Name == "Gate"), "and it actually turns up in the first bags");
      var late = new StackGame(new StackSettings { Seed = 3, StartLevel = 8, Pieces = PieceSets.Get(PieceSets.Standard) });
      check(Enumerable.Range(0, 40).Any(i => !late.Upcoming(i).IsPlanar), "starting at a high level deals the whole set from the first piece");
    }

    static void ClearsAndScore(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: layer clears and scoring");
      // 2×2 well: every O piece fills a whole layer.
      var g = Game(2, 2, 6, PieceSets.O4);
      g.HardDrop();
      check(g.State == StackState.Clearing && g.ClearingLayers.SequenceEqual(new[] { 0 }), "a full layer starts the clear animation");
      long first = g.Score;
      check(first == 100 + 3000 + 2 * 5, $"single + site cleared + 5 cells hard drop = {first}");
      g.Tick(StackGame.ClearDuration + 0.01);
      check(g.State == StackState.Playing && g.Well.Count == 0 && g.Current != null, "after the animation the layer is gone and a new piece falls");
      g.HardDrop();
      check(g.Combo == 2 && g.Score - first == 100 + 50 + 3000 + 10, $"second clear in a row adds a combo bonus ({g.Score - first})");

      // 3×3 well with I3 pieces: three flat drops side by side clear one layer; a non-clearing lock resets the combo.
      var h = Game(3, 3, 8, PieceSets.I3);
      h.Move(new Int3(0, -1, 0));
      h.HardDrop();
      h.Move(new Int3(0, 1, 0));
      h.HardDrop();
      check(h.Combo == 0 && h.Well.Count == 6, "two of three rows placed, no clear yet");
      h.HardDrop();
      check(h.LastClear != null && h.LastClear.Layers == 1 && h.Layers == 1, "the third row completes the layer");

      var lv = Game(2, 2, 6, PieceSets.O4);
      for (int i = 0; i < 5; i++)
      {
        lv.HardDrop();
        lv.Tick(StackGame.ClearDuration + 0.01);
      }
      check(lv.Layers == 5 && lv.Level == 2 && lv.LevelUpTime > double.NegativeInfinity, "level 2 after 5 layers");
    }

    static void TopOutAndZen(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: topping out and zen mode");
      var g = Game(3, 3, 4, PieceSets.O4);
      for (int i = 0; i < 10 && !g.IsOver; i++) g.HardDrop();
      check(g.State == StackState.ToppedOut && g.Current == null, $"stacking O pieces in a corner tops out ({g.PiecesPlaced} placed)");
      g.Move(Int3.UnitX);
      g.HardDrop();
      check(g.State == StackState.ToppedOut, "no input after the end");

      var z = new StackGame(new StackSettings { Nx = 3, Ny = 3, Nz = 4, Mode = StackMode.Zen, Seed = 2, Pieces = new[] { PieceSets.O4 } });
      int start = z.Position.Z;
      z.Tick(10);
      check(z.Position.Z == start, "zen: no gravity");
      for (int i = 0; i < 10; i++) z.HardDrop();
      check(z.State == StackState.Playing && z.SiteResetTime > double.NegativeInfinity, "zen: topping out clears the site instead of ending");
    }

    static void KicksAndHold(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: wall kicks and hold");
      var g = Game(4, 4, 12, PieceSets.I4);
      check(g.Rotate(Int3.UnitZ, true), "I4 turns upright about z");
      var xs = g.PieceCells.Select(c => c.X).Distinct().ToList();
      check(xs.Count == 1, "now it lies along y");
      while (g.Move(Int3.UnitX)) { }
      check(g.PieceCells.All(c => c.X == 3), "slid against the +x wall");
      check(g.Rotate(Int3.UnitZ, true) && g.PieceCells.All(c => c.X >= 0 && c.X < 4), "turning against the wall kicks it back inside");

      var h = Game(5, 5, 12, PieceSets.I4, PieceSets.O4, PieceSets.T4);
      var first = h.Current;
      check(h.Hold() && h.Held == first && h.Current != first, "hold stores the falling piece and deals the next");
      check(!h.Hold(), "only one hold per piece");
      h.HardDrop();
      var next = h.Current;
      check(h.Hold() && h.Current == first && h.Held == next, "after locking, hold swaps the stored piece back in");
    }

    static void Replay(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: deterministic replay");
      string Play(int seed)
      {
        var g = new StackGame(new StackSettings { Seed = seed });
        var moves = new[] { Int3.UnitX, -Int3.UnitX, Int3.UnitY, -Int3.UnitY };
        var rng = new Random(99);
        for (int i = 0; i < 400 && !g.IsOver; i++)
        {
          int r = rng.Next(10);
          if (r < 4) g.Move(moves[r]);
          else if (r < 7) g.Rotate(new[] { Int3.UnitX, Int3.UnitY, Int3.UnitZ }[r - 4], rng.Next(2) == 0);
          else if (r == 7) g.HardDrop();
          g.Tick(0.05);
        }
        return $"{g.Score}/{g.Layers}/{g.PiecesPlaced}/{g.Well.Count}/{g.State}";
      }
      string a = Play(42), b = Play(42), c = Play(43);
      check(a == b, $"same seed and inputs → same game ({a})");
      check(a != c, $"another seed deals differently ({c})");
    }

    static void Blitz(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: blitz");
      var g = new StackGame(new StackSettings { Mode = StackMode.Blitz, BlitzSeconds = 3, Seed = 5 });
      g.Pause();
      g.Tick(10);
      check(g.State == StackState.Paused && g.Elapsed == 0, "the clock stops while paused");
      g.Resume();
      for (int i = 0; i < 40; i++) g.Tick(0.1);
      check(g.State == StackState.TimeUp && g.TimeLeft == 0, "time up after the limit");

      // The clock runs out while a layer flashes: the clear is finished first, so the well matches the score.
      var c = new StackGame(new StackSettings { Nx = 2, Ny = 2, Nz = 6, Mode = StackMode.Blitz, BlitzSeconds = 1, Seed = 5, Pieces = new[] { PieceSets.O4 } });
      c.Tick(0.9);
      c.HardDrop();
      check(c.State == StackState.Clearing && c.Well.Count == 4, "a layer is clearing when the clock runs out");
      c.Tick(0.2);
      check(c.State == StackState.TimeUp && c.Well.Count == 0 && c.ClearingLayers.Count == 0 && c.Layers == 1 && c.Current == null,
        "time up removes the cleared layer and leaves no clear animation behind");
    }

    static void Metrics(Action<bool, string> check)
    {
      Console.WriteLine("VoxStack: massing metrics");
      var cells = new List<Int3>();
      for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
          cells.Add(new Int3(x, y, 0));
      cells.Add(new Int3(0, 0, 1));
      var m = MassingMetrics.From(cells, 5, 5);
      double site = Math.Pow(6.6 * 7.2, 2);
      check(Math.Abs(m.SiteArea - site) < 1e-6 && Math.Abs(m.Gfa - 26 * 7.2 * 7.2 * 3) < 1e-6, $"site {m.SiteArea:F0} m², GFA {m.Gfa:F0} m²");
      check(m.Floors == 6 && Math.Abs(m.Height - 21.6) < 1e-9 && Math.Abs(m.Coverage - 25 * 51.84 / site) < 1e-9, $"{m.Floors} floors, {m.Height} m, coverage {m.Coverage:P0}");
      var decoy = MassingMetrics.Decoy(5, 5, 12, 7);
      check(decoy.Count > 30 && decoy.All(c => c.X >= 0 && c.X < 5 && c.Y >= 0 && c.Y < 5 && c.Z < 12) && Polycube.IsConnected(decoy),
        $"decoy massing: {decoy.Count} voxels, connected, inside the well");
      check(MassingMetrics.Decoy(5, 5, 12, 7).SequenceEqual(decoy), "decoy is repeatable per seed");
    }
  }
}
