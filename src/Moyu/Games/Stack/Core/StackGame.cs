using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Stack.Core
{
  public enum StackMode { Marathon, Blitz, Zen }

  public enum StackState { Playing, Clearing, Paused, ToppedOut, TimeUp }

  public sealed class StackSettings
  {
    public int Nx = 5, Ny = 5, Nz = 12;
    public StackMode Mode = StackMode.Marathon;
    public int StartLevel = 1;
    public int Seed = Environment.TickCount;
    public IReadOnlyList<PieceDef> Pieces = PieceSets.Get(PieceSets.Standard);
    public double BlitzSeconds = 180;
  }

  /// <summary>What the last layer clear was worth, for the HUD pop-up.</summary>
  public sealed class ClearEvent
  {
    public int Layers;
    public long Points;
    public int Combo;
    public bool Perfect;
    public int Level;
    /// <summary><see cref="StackGame.Elapsed"/> when it happened.</summary>
    public double Time;
  }

  /// <summary>
  /// Rules of the 3D falling-block game, free of Rhino and of real time: feed it commands and <see cref="Tick"/>
  /// steps, read back the well, the falling piece and the score. The same seed and inputs always give the same game.
  /// </summary>
  public sealed class StackGame
  {
    public const double LockDelay = 0.6, ClearDuration = 0.3, SoftDropInterval = 0.035;
    /// <summary>The first levels hold back the preset units that are 3D or over four voxels; they join at half weight on this level and fully on the next. Units the player made are always dealt.</summary>
    public const int MixedFrom = 3, FullSetFrom = 4;
    public const int MaxLockResets = 15, LayersPerLevel = 5;
    static readonly int[] ClearPoints = { 0, 100, 300, 700, 1500 };

    /// <summary>
    /// Offsets tried in order when a turn collides: in place, sideways, then up. The two-cell lift is for a 4-long
    /// unit tipped on the floor or the stack: its pivot is off-centre, so one way puts two cells under the support.
    /// </summary>
    static readonly Int3[] Kicks =
    {
      new Int3(0, 0, 0), new Int3(-1, 0, 0), new Int3(1, 0, 0), new Int3(0, -1, 0), new Int3(0, 1, 0), new Int3(0, 0, 1),
      new Int3(-2, 0, 0), new Int3(2, 0, 0), new Int3(0, -2, 0), new Int3(0, 2, 0), new Int3(0, 0, 2),
    };

    public readonly StackSettings Settings;
    public readonly Well Well;
    readonly Bag _bag;
    StackState _beforePause;
    int[] _clearing = Array.Empty<int>();
    double _fall, _lockTimer;
    int _lockResets, _lowestZ;

    public StackState State { get; private set; }
    /// <summary>The falling piece, or null between pieces and after the game ends.</summary>
    public PieceDef Current { get; private set; }
    /// <summary>Cells of the falling piece relative to <see cref="Position"/>, its pivot cell.</summary>
    public Int3[] Offsets { get; private set; } = Array.Empty<Int3>();
    public Int3 Position { get; private set; }
    public PieceDef Held { get; private set; }
    public bool HoldUsed { get; private set; }
    public long Score { get; private set; }
    public int Layers { get; private set; }
    public int Level { get; private set; }
    public int Combo { get; private set; }
    public int PiecesPlaced { get; private set; }
    /// <summary>Seconds played (paused time excluded).</summary>
    public double Elapsed { get; private set; }
    /// <summary>Held soft-drop key: falls fast and scores 1 per cell.</summary>
    public bool SoftDrop { get; set; }
    public ClearEvent LastClear { get; private set; }
    public double LevelUpTime { get; private set; } = double.NegativeInfinity;
    /// <summary>Zen mode clears the whole well instead of ending; this is when it last did.</summary>
    public double SiteResetTime { get; private set; } = double.NegativeInfinity;
    /// <summary>Full layers flashing before they are removed.</summary>
    public IReadOnlyList<int> ClearingLayers => _clearing;
    /// <summary>0 → 1 over the clear animation.</summary>
    public double ClearProgress { get; private set; }

    public StackGame(StackSettings settings)
    {
      Settings = settings ?? new StackSettings();
      if (Settings.Pieces == null || Settings.Pieces.Count == 0) throw new ArgumentException("The game needs at least one piece kind.");
      Well = new Well(Settings.Nx, Settings.Ny, Settings.Nz);
      foreach (var p in Settings.Pieces)
      {
        var s = p.Size;
        if (Math.Max(s.X, Math.Max(s.Y, s.Z)) > Math.Min(Well.Nx, Well.Ny) || s.Z >= Well.Nz)
          throw new ArgumentException($"{p.Name} ({s.X}×{s.Y}×{s.Z}) does not fit a {Well.Nx}×{Well.Ny}×{Well.Nz} well.");
      }
      Level = Math.Max(1, Settings.StartLevel);
      _bag = new Bag(PoolFor(Level), new Random(Settings.Seed));
      State = StackState.Playing;
      Spawn(_bag.Next());
    }

    public IEnumerable<Int3> PieceCells => Offsets.Select(o => Position + o);
    public PieceDef Upcoming(int index) => _bag.Peek(index);
    public bool IsOver => State == StackState.ToppedOut || State == StackState.TimeUp;
    public double GravityInterval => GravityFor(Level);
    /// <summary>Seconds per cell: a slow 2.4 s at level 1 (time to look and turn), 8 % less each level (1.13 s at level 10, 0.75 s at 15), never under 0.15 s.</summary>
    public static double GravityFor(int level) => Math.Max(0.15, 2.4 * Math.Pow(0.92, level - 1));

    /// <summary>Time a landed piece waits before it locks: 1.2 s at level 1, down to 0.6 s from level 11.</summary>
    public static double LockDelayFor(int level) => Math.Max(LockDelay, 1.2 - 0.06 * (level - 1));

    /// <summary>
    /// The units dealt at a level: on the first levels the flat preset units of up to four voxels (when the set has at
    /// least three) plus every unit the player made, then the other presets join at half weight, then the whole set as
    /// configured. Custom units are never held back: the player built them to see them fall.
    /// </summary>
    public IReadOnlyList<PieceDef> PoolFor(int level)
    {
      var all = Settings.Pieces;
      if (level >= FullSetFrom) return all;
      var easy = all.Where(p => p.IsCustom || (p.IsPlanar && p.Size.Z == 1 && p.Cells.Length <= 4)).ToList();
      if (easy.Count(p => !p.IsCustom) < 3 || easy.Count == all.Count) return all;
      if (level < MixedFrom) return easy;
      return easy.Concat(all.Where(p => !easy.Contains(p)).Select(p => p.WithWeight(p.Weight * 0.5))).ToList();
    }
    public double TimeLeft => Settings.Mode == StackMode.Blitz ? Math.Max(0, Settings.BlitzSeconds - Elapsed) : double.PositiveInfinity;

    public static string LevelName(int level) =>
      level >= 13 ? "超高层 SUPERTALL" : level >= 10 ? "高层 HIGH-RISE" : level >= 7 ? "小高层 MID-RISE" : level >= 4 ? "多层 LOW-RISE" : "亭子 PAVILION";

    bool Fits(Int3[] offsets, Int3 position)
    {
      foreach (var o in offsets)
        if (!Well.IsFree(position + o)) return false;
      return true;
    }

    /// <summary>How many cells the falling piece would drop before landing.</summary>
    public int DropDistance()
    {
      if (Current == null) return 0;
      int d = 0;
      while (Fits(Offsets, Position + (d + 1) * Int3.Down)) d++;
      return d;
    }

    public void Tick(double dt)
    {
      if (State == StackState.Paused || IsOver || dt <= 0) return;
      Elapsed += dt;
      if (Settings.Mode == StackMode.Blitz && Elapsed >= Settings.BlitzSeconds)
      {
        // A clear already credited in Lock() finishes now, so the well on the result screen matches the score.
        if (State == StackState.Clearing) FinishClear();
        State = StackState.TimeUp;
        Current = null;
        return;
      }
      if (State == StackState.Clearing)
      {
        ClearProgress += dt / ClearDuration;
        if (ClearProgress < 1) return;
        FinishClear();
        State = StackState.Playing;
        Spawn(_bag.Next());
        return;
      }
      if (Current == null) return;

      bool zen = Settings.Mode == StackMode.Zen;
      if (!zen || SoftDrop)
      {
        double interval = SoftDrop ? Math.Min(GravityInterval, SoftDropInterval) : GravityInterval;
        // Time saved up towards the next slow step is not spent at the soft-drop rate, or pressing it after a wait sent
        // the piece down the whole well in one frame: at most one cell comes at once, then one per interval.
        if (SoftDrop) _fall = Math.Min(_fall, interval);
        _fall += dt;
        while (_fall >= interval)
        {
          _fall -= interval;
          if (!StepDown()) { _fall = 0; break; }
          if (SoftDrop) Score++;
        }
      }
      // Zen pieces only lock on a hard drop, so you can take your time.
      if (!zen && !Fits(Offsets, Position + Int3.Down))
      {
        _lockTimer += dt;
        if (_lockTimer >= LockDelayFor(Level)) Lock();
      }
      // Only falling to a new low starts the delay afresh: a turn that kicks the piece up into the air must not, or
      // turning it over and over would keep it from ever locking (the lock resets per height are capped instead).
      else if (Position.Z <= _lowestZ) _lockTimer = 0;
    }

    void FinishClear()
    {
      Well.RemoveLayers(_clearing);
      _clearing = Array.Empty<int>();
      ClearProgress = 0;
    }

    bool StepDown()
    {
      if (!Fits(Offsets, Position + Int3.Down)) return false;
      Position += Int3.Down;
      if (Position.Z < _lowestZ)
      {
        _lowestZ = Position.Z;
        _lockResets = 0;
      }
      return true;
    }

    /// <summary>A move or turn while resting restarts the lock delay, a limited number of times per height.</summary>
    void AfterManoeuvre()
    {
      if (_lockTimer > 0 && _lockResets < MaxLockResets)
      {
        _lockTimer = 0;
        _lockResets++;
      }
    }

    /// <summary>Sideways move (z of <paramref name="delta"/> ignored).</summary>
    public bool Move(Int3 delta)
    {
      if (State != StackState.Playing || Current == null) return false;
      var d = new Int3(delta.X, delta.Y, 0);
      if (!Fits(Offsets, Position + d)) return false;
      Position += d;
      AfterManoeuvre();
      return true;
    }

    /// <summary>Quarter turn about an axis-aligned unit axis, kicking sideways or up when the turn collides.</summary>
    public bool Rotate(Int3 axis, bool positive)
    {
      if (State != StackState.Playing || Current == null) return false;
      var turned = Offsets.Select(o => Int3.Rotate(o, axis, positive)).ToArray();
      foreach (var k in Kicks)
        if (Fits(turned, Position + k))
        {
          Offsets = turned;
          Position += k;
          AfterManoeuvre();
          return true;
        }
      return false;
    }

    public void HardDrop()
    {
      if (State != StackState.Playing || Current == null) return;
      int d = DropDistance();
      Position += d * Int3.Down;
      Score += 2 * d;
      Lock();
    }

    /// <summary>
    /// Puts the falling piece aside (once per piece) and brings in the held one, or the next. Refused when that one has
    /// no room at the top, so a hold never ends the game or resets a zen site while the falling piece is fine.
    /// </summary>
    public bool Hold()
    {
      if (State != StackState.Playing || Current == null || HoldUsed) return false;
      // Peek(0) is the piece Next() deals below.
      if (!Fits(SpawnOffsets(Held ?? _bag.Peek(0), out var at), at)) return false;
      var swap = Held;
      Held = Current;
      Spawn(swap ?? _bag.Next());
      HoldUsed = true;
      return true;
    }

    public void Pause()
    {
      if (State != StackState.Playing && State != StackState.Clearing) return;
      _beforePause = State;
      State = StackState.Paused;
      SoftDrop = false;
    }

    public void Resume()
    {
      if (State == StackState.Paused) State = _beforePause;
    }

    void Lock()
    {
      var def = Current;
      var cells = PieceCells.ToList();
      Current = null;
      HoldUsed = false;
      PiecesPlaced++;
      _lockTimer = 0;
      _fall = 0;
      if (cells.Any(c => c.Z >= Well.Nz))
      {
        TopOut();
        return;
      }
      foreach (var c in cells) Well.Set(c, def.Colour);

      var full = Well.FullLayers();
      if (full.Count == 0)
      {
        Combo = 0;
        Spawn(_bag.Next());
        return;
      }
      Combo++;
      long points = (long)Math.Round(ClearPoints[Math.Min(full.Count, 4)] * Level * def.ScoreMultiplier);
      if (Combo > 1) points += 50L * (Combo - 1) * Level;
      bool perfect = Well.Count == full.Count * Well.LayerSize;
      if (perfect) points += 3000L * Level;
      Score += points;
      LastClear = new ClearEvent { Layers = full.Count, Points = points, Combo = Combo, Perfect = perfect, Level = Level, Time = Elapsed };
      int before = Level;
      Layers += full.Count;
      Level = Math.Max(1, Settings.StartLevel) + Layers / LayersPerLevel;
      if (Level > before)
      {
        LevelUpTime = Elapsed;
        _bag.SetPool(PoolFor(Level));
      }
      _clearing = full.ToArray();
      ClearProgress = 0;
      State = StackState.Clearing;
    }

    /// <param name="blocked">The piece that had no room at the top, if that is why: zen deals it again on the cleared site.</param>
    void TopOut(PieceDef blocked = null)
    {
      Current = null;
      if (Settings.Mode == StackMode.Zen)
      {
        Well.Clear();
        Combo = 0;
        SiteResetTime = Elapsed;
        // Every piece fits an empty well (checked in the constructor), so this does not recurse again.
        Spawn(blocked ?? _bag.Next());
        return;
      }
      State = StackState.ToppedOut;
    }

    /// <summary>Where a new piece of this kind appears: its cells round the pivot, and the pivot's cell at the top centre.</summary>
    Int3[] SpawnOffsets(PieceDef def, out Int3 position)
    {
      var s = def.Size;
      double cx = (s.X - 1) / 2.0, cy = (s.Y - 1) / 2.0, cz = (s.Z - 1) / 2.0;
      // Turning about the cell nearest the middle keeps the piece where you expect it.
      var pivot = def.Cells.OrderBy(c => (c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy) + (c.Z - cz) * (c.Z - cz)).First();
      position = new Int3((Well.Nx - s.X) / 2, (Well.Ny - s.Y) / 2, Well.Nz - s.Z) + pivot;
      return def.Cells.Select(c => c - pivot).ToArray();
    }

    void Spawn(PieceDef def)
    {
      Offsets = SpawnOffsets(def, out var position);
      Position = position;
      Current = def;
      _lowestZ = Position.Z;
      _lockResets = 0;
      _lockTimer = 0;
      _fall = 0;
      if (!Fits(Offsets, Position)) TopOut(def);
    }
  }
}
