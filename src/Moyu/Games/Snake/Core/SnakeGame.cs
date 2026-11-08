using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Snake.Core
{
  public enum SnakeMode { Classic, Blitz, Zen }

  /// <summary><see cref="SnakeState.Won"/>: the body covers every free cell it can reach (not in zen, which passes over itself and runs on).</summary>
  public enum SnakeState { Playing, Paused, Dead, TimeUp, Won }

  public enum ItemKind { Apple, Grape, Melon, Bomb, Ice, Ghost }

  public enum DeathCause { None, Wall, Self, Bomb }

  public sealed class Item
  {
    public Cell Cell;
    public ItemKind Kind;
    public double Born;
    public double Expires = double.PositiveInfinity;
  }

  /// <summary>What the last item did, for the HUD pop-up and the debris of a bomb.</summary>
  public sealed class SnakeEvent
  {
    public ItemKind Kind;
    public Cell Cell;
    public long Points;
    public int Combo;
    public double Time;
    public Cell[] Lost = Array.Empty<Cell>();
  }

  public sealed class SnakeSettings
  {
    public SnakeMode Mode = SnakeMode.Classic;
    public int StartLevel = 1;
    public int Seed = Environment.TickCount;
    public bool SlopeAffectsSpeed = true;
    public double BlitzSeconds = 180;
    public int StartLength = 4;
    /// <summary>Off for scripted tests that place every item by hand.</summary>
    public bool SpawnItems = true;
  }

  /// <summary>
  /// Rules of snake on a surface grid, free of Rhino and of real time: feed it turns and <see cref="Tick"/> steps,
  /// read back the body, the items and the score. The same seed and inputs always give the same game.
  /// </summary>
  public sealed class SnakeGame
  {
    public const double ComboWindow = 4, PowerSeconds = 6, BoostFactor = 1.8, IceFactor = 0.6, MaxSpeed = 15;
    public const int FruitsPerLevel = 5, BombCut = 3, MinLength = 2, MaxCombo = 5, BufferedTurns = 2, Apples = 2;

    public readonly SurfaceGrid Grid;
    public readonly SnakeSettings Settings;
    readonly LinkedList<Cell> _body = new LinkedList<Cell>();
    readonly Dictionary<Cell, int> _occupied = new Dictionary<Cell, int>();
    readonly List<Item> _items = new List<Item>();
    readonly Queue<(int du, int dv)> _turns = new Queue<(int, int)>();
    readonly Random _rng;
    /// <summary>The island of each cell (-1 when blocked), the size of each, and the one the snake is on.</summary>
    readonly int[] _island;
    readonly List<int> _islandSize = new List<int>();
    readonly int _home, _playable;
    double _acc, _interval, _nextGrape, _nextMelon, _nextBomb, _nextPower;
    int _grow;
    SnakeState _beforePause;

    public SnakeState State { get; private set; }
    public DeathCause Cause { get; private set; }
    public (int du, int dv) Heading { get; private set; }
    /// <summary>Where the head was before the last step (for smooth drawing between steps).</summary>
    public Cell PreviousHead { get; private set; }
    /// <summary>The cell the last segment came from in the last step, or null when the snake grew.</summary>
    public Cell? RemovedTail { get; private set; }
    /// <summary>The last step moved the snake (false before the first step and while zen waits at a wall).</summary>
    public bool Moved { get; private set; }
    public long Score { get; private set; }
    public int Level { get; private set; }
    public int FruitsEaten { get; private set; }
    public int Combo { get; private set; }
    public double Elapsed { get; private set; }
    public double GhostUntil { get; private set; } = double.NegativeInfinity;
    public double IceUntil { get; private set; } = double.NegativeInfinity;
    public double LastEatTime { get; private set; } = double.NegativeInfinity;
    public double LevelUpTime { get; private set; } = double.NegativeInfinity;
    public SnakeEvent LastEvent { get; private set; }
    /// <summary>Speed multiplier from the slope of the step about to be taken.</summary>
    public double SlopeFactor { get; private set; } = 1;
    /// <summary>Held sprint key.</summary>
    public bool Boost { get; set; }

    public SnakeGame(SurfaceGrid grid, SnakeSettings settings)
    {
      Grid = grid ?? throw new ArgumentNullException(nameof(grid));
      Settings = settings ?? new SnakeSettings();
      _rng = new Random(Settings.Seed);
      _island = Islands(Grid, _islandSize);
      Level = Math.Max(1, Settings.StartLevel);
      if (!PlaceSnake(Math.Max(MinLength, Settings.StartLength))) throw new ArgumentException("The surface has no free run of cells long enough for the snake.");
      // A trim can cut the site into islands the snake can never cross between: only its own island is played,
      // so an apple is never put where it cannot be eaten (and would hold its place for good).
      _home = _island[Grid.Index(Head)];
      _playable = _islandSize[_home];
      PreviousHead = Head;
      _nextGrape = 6 + 6 * _rng.NextDouble();
      _nextMelon = 15 + 10 * _rng.NextDouble();
      _nextBomb = 4 + 4 * _rng.NextDouble();
      _nextPower = 18 + 8 * _rng.NextDouble();
      SpawnItems();
      _interval = StepInterval();
      State = SnakeState.Playing;
    }

    /// <summary>Head first.</summary>
    public IEnumerable<Cell> Body => _body;
    public int Length => _body.Count;
    public Cell Head => _body.First.Value;
    public IReadOnlyList<Item> Items => _items;
    public bool IsOver => State == SnakeState.Dead || State == SnakeState.TimeUp || State == SnakeState.Won;
    public bool GhostActive => Elapsed < GhostUntil;
    public bool IceActive => Elapsed < IceUntil;
    public double TimeLeft => Settings.Mode == SnakeMode.Blitz ? Math.Max(0, Settings.BlitzSeconds - Elapsed) : double.PositiveInfinity;
    /// <summary>0 → 1 between two steps.</summary>
    public double StepProgress => _interval > 0 ? Math.Min(1, _acc / _interval) : 0;
    /// <summary>Cells per second: an easy 3.4 at level 1, +0.55 a level, capped at 15.</summary>
    public static double SpeedFor(int level) => Math.Min(MaxSpeed, 3.4 + 0.55 * (level - 1));

    /// <summary>Bombs start on this level, so the first levels are only fruit.</summary>
    public const int BombsFrom = 3;
    /// <summary>Cells per second right now, with sprint, ice and slope.</summary>
    public double Speed => _interval > 0 ? 1 / _interval : 0;
    public bool Occupies(Cell c) => _occupied.ContainsKey(c);

    public static string LevelName(int level) =>
      level >= 13 ? "山脊 RIDGE" : level >= 10 ? "峡谷 CANYON" : level >= 7 ? "丘陵 HILLS" : level >= 4 ? "草坡 MEADOW" : "庭院 COURTYARD";

    bool PlaceSnake(int length)
    {
      var start = new Cell(Grid.Nu / 2, Grid.Nv / 2);
      // Nearest the middle, on the largest island first.
      var candidates = Grid.FreeCells().OrderByDescending(c => _islandSize[_island[Grid.Index(c)]]).ThenBy(c => Grid.GridDistance(c, start));
      foreach (var head in candidates)
        foreach (var (du, dv) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
        {
          var cells = new List<Cell> { head };
          if (Grid.IsBlocked(head)) break;
          var c = head;
          while (cells.Count < length && Grid.TryStep(c, -du, -dv, out var back) && !cells.Contains(back))
          {
            cells.Add(back);
            c = back;
          }
          // Room to move forward too, so the first step is not into a wall.
          if (cells.Count < length || !Grid.TryStep(head, du, dv, out var ahead) || cells.Contains(ahead)) continue;
          foreach (var cell in cells)
          {
            _body.AddLast(cell);
            Add(cell);
          }
          Heading = (du, dv);
          return true;
        }
      return false;
    }

    void Add(Cell c) => _occupied[c] = _occupied.TryGetValue(c, out int n) ? n + 1 : 1;

    void Remove(Cell c)
    {
      if (!_occupied.TryGetValue(c, out int n)) return;
      if (n <= 1) _occupied.Remove(c);
      else _occupied[c] = n - 1;
    }

    // ---------------------------------------------------------------- input

    /// <summary>The heading the snake will have after the queued turns.</summary>
    public (int du, int dv) PlannedHeading => _turns.Count > 0 ? _turns.Last() : Heading;

    /// <summary>Queues a turn to grid direction (du, dv). Ignored when it would reverse onto the body or the queue is full.</summary>
    public bool Turn(int du, int dv)
    {
      if (State != SnakeState.Playing || Math.Abs(du) + Math.Abs(dv) != 1) return false;
      var last = PlannedHeading;
      if (du == last.du && dv == last.dv) return false;
      if (du == -last.du && dv == -last.dv && _body.Count > 1) return false;
      if (_turns.Count >= BufferedTurns) return false;
      _turns.Enqueue((du, dv));
      return true;
    }

    /// <summary>Quarter turn anticlockwise in UV, which is left when seen from the surface's normal side.</summary>
    public bool TurnLeft()
    {
      var h = PlannedHeading;
      return Turn(-h.dv, h.du);
    }

    public bool TurnRight()
    {
      var h = PlannedHeading;
      return Turn(h.dv, -h.du);
    }

    public void Pause()
    {
      if (State != SnakeState.Playing) return;
      _beforePause = State;
      State = SnakeState.Paused;
      Boost = false;
    }

    public void Resume()
    {
      if (State == SnakeState.Paused) State = _beforePause;
    }

    /// <summary>Puts an item on a cell (tests and scripted demos).</summary>
    public void PlaceItem(ItemKind kind, Cell cell, double life = double.PositiveInfinity)
    {
      _items.RemoveAll(i => i.Cell == cell);
      _items.Add(new Item { Kind = kind, Cell = cell, Born = Elapsed, Expires = Elapsed + life });
    }

    // ---------------------------------------------------------------- time

    public void Tick(double dt)
    {
      if (State != SnakeState.Playing || dt <= 0) return;
      Elapsed += dt;
      if (Settings.Mode == SnakeMode.Blitz && Elapsed >= Settings.BlitzSeconds)
      {
        State = SnakeState.TimeUp;
        return;
      }
      _items.RemoveAll(i => i.Expires <= Elapsed);
      SpawnItems();
      _acc += dt;
      _interval = StepInterval();
      while (_acc >= _interval && State == SnakeState.Playing)
      {
        _acc -= _interval;
        Step();
        _interval = StepInterval();
      }
    }

    double StepInterval()
    {
      double speed = SpeedFor(Level);
      if (Boost) speed *= BoostFactor;
      if (IceActive) speed *= IceFactor;
      SlopeFactor = 1;
      if (Settings.SlopeAffectsSpeed && _body.Count > 0)
      {
        var h = _turns.Count > 0 ? _turns.Peek() : Heading;
        if (Grid.TryStep(Head, h.du, h.dv, out var next))
          SlopeFactor = Math.Max(0.6, Math.Min(1.5, 1 - 1.25 * Grid.Slope(Head, next)));
        // An invalid surface sample (NaN height) must not freeze the snake.
        if (double.IsNaN(SlopeFactor)) SlopeFactor = 1;
      }
      return 1 / (speed * SlopeFactor);
    }

    void Step()
    {
      if (_turns.Count > 0) Heading = _turns.Dequeue();
      PreviousHead = Head;
      RemovedTail = null;
      bool zen = Settings.Mode == SnakeMode.Zen;
      Moved = false;
      if (!Grid.TryStep(Head, Heading.du, Heading.dv, out var next))
      {
        // Zen waits at the wall for a turn; the other modes end.
        if (!zen) Die(DeathCause.Wall);
        _acc = 0;
        return;
      }
      bool growing = _grow > 0;
      var tail = _body.Last.Value;
      int hits = _occupied.TryGetValue(next, out int n) ? n : 0;
      if (!growing && next == tail) hits--; // the tail moves out of the way this step
      if (hits > 0 && !zen && !GhostActive)
      {
        Die(DeathCause.Self);
        return;
      }
      _body.AddFirst(next);
      Add(next);
      Moved = true;
      if (growing) _grow--;
      else
      {
        _body.RemoveLast();
        Remove(tail);
        RemovedTail = tail;
      }
      var item = _items.FirstOrDefault(i => i.Cell == next);
      if (item != null) Eat(item);
      // The whole site is filled: a win, not a bite into the tail on the next step (or an endless chase with nothing left to eat).
      if (State == SnakeState.Playing && !zen && _occupied.Count >= _playable)
      {
        State = SnakeState.Won;
        Boost = false;
      }
    }

    void Die(DeathCause cause)
    {
      Cause = cause;
      State = SnakeState.Dead;
      Boost = false;
    }

    void Eat(Item item)
    {
      _items.Remove(item);
      var ev = new SnakeEvent { Kind = item.Kind, Cell = item.Cell, Time = Elapsed };
      LastEvent = ev;
      switch (item.Kind)
      {
        case ItemKind.Apple: Fruit(ev, 1, 10); break;
        case ItemKind.Grape: Fruit(ev, 3, 30); break;
        case ItemKind.Melon: Fruit(ev, 5, 80); break;
        case ItemKind.Ice: IceUntil = Elapsed + PowerSeconds; break;
        case ItemKind.Ghost: GhostUntil = Elapsed + PowerSeconds; break;
        case ItemKind.Bomb:
          ev.Points = -Math.Min(Score, 50);
          Score += ev.Points;
          Combo = 0;
          if (_body.Count - BombCut < MinLength)
          {
            ev.Lost = _body.ToArray();
            Die(DeathCause.Bomb);
            return;
          }
          var lost = new List<Cell>();
          for (int i = 0; i < BombCut; i++)
          {
            var t = _body.Last.Value;
            _body.RemoveLast();
            Remove(t);
            lost.Add(t);
          }
          ev.Lost = lost.ToArray();
          // The new last segment came from the last cell cut (the one right behind it), not from the far end.
          RemovedTail = lost[lost.Count - 1];
          // Growth still owed from fruit eaten just before is lost with the tail.
          _grow = 0;
          break;
      }
    }

    void Fruit(SnakeEvent ev, int grow, int points)
    {
      Combo = Elapsed - LastEatTime <= ComboWindow ? Math.Min(MaxCombo, Combo + 1) : 1;
      LastEatTime = Elapsed;
      _grow += grow;
      ev.Combo = Combo;
      ev.Points = (long)points * Combo;
      Score += ev.Points;
      FruitsEaten++;
      int before = Level;
      Level = Math.Max(1, Settings.StartLevel) + FruitsEaten / FruitsPerLevel;
      if (Level > before) LevelUpTime = Elapsed;
    }

    // ---------------------------------------------------------------- items

    void SpawnItems()
    {
      if (!Settings.SpawnItems) return;
      while (_items.Count(i => i.Kind == ItemKind.Apple) < Apples && TrySpawn(ItemKind.Apple, double.PositiveInfinity)) { }
      if (Elapsed >= _nextGrape)
      {
        TrySpawn(ItemKind.Grape, 14);
        _nextGrape = Elapsed + 8 + 6 * _rng.NextDouble();
      }
      if (Elapsed >= _nextMelon)
      {
        TrySpawn(ItemKind.Melon, 8);
        _nextMelon = Elapsed + 20 + 12 * _rng.NextDouble();
      }
      if (Settings.Mode != SnakeMode.Zen && Level >= BombsFrom && Elapsed >= _nextBomb)
      {
        if (_items.Count(i => i.Kind == ItemKind.Bomb) < 1 + (Level - BombsFrom) / 3) TrySpawn(ItemKind.Bomb, 12);
        _nextBomb = Elapsed + 5 + 4 * _rng.NextDouble();
      }
      if (Elapsed >= _nextPower)
      {
        TrySpawn(_rng.NextDouble() < 0.5 ? ItemKind.Ice : ItemKind.Ghost, 10);
        _nextPower = Elapsed + 18 + 10 * _rng.NextDouble();
      }
    }

    bool TrySpawn(ItemKind kind, double life)
    {
      var cell = RandomFreeCell(kind == ItemKind.Bomb ? 4 : 1);
      if (cell == null) return false;
      _items.Add(new Item { Kind = kind, Cell = cell.Value, Born = Elapsed, Expires = Elapsed + life });
      return true;
    }

    /// <summary>
    /// A random free cell. On a mostly empty field random picks land on one almost at once; on a crowded one (a long
    /// zen snake) the free cells are listed once and one is drawn, so a full field costs one scan per spawn.
    /// </summary>
    Cell? RandomFreeCell(int awayFromHead)
    {
      var taken = new HashSet<Cell>(_items.Select(i => i.Cell));
      bool Free(Cell c) => !Grid.IsBlocked(c) && _island[Grid.Index(c)] == _home && !_occupied.ContainsKey(c) && !taken.Contains(c) && Grid.GridDistance(c, Head) >= awayFromHead;
      if ((_occupied.Count + taken.Count) * 2 < _playable)
      {
        for (int attempt = 0; attempt < 300; attempt++)
        {
          var c = new Cell(_rng.Next(Grid.Nu), _rng.Next(Grid.Nv));
          if (Free(c)) return c;
        }
      }
      var free = Grid.FreeCells().Where(Free).ToList();
      return free.Count > 0 ? free[_rng.Next(free.Count)] : (Cell?)null;
    }

    /// <summary>Numbers the islands of free cells joined by steps (wrapping where the grid does) and counts each one.</summary>
    static int[] Islands(SurfaceGrid grid, List<int> sizes)
    {
      var island = new int[grid.Count];
      for (int i = 0; i < island.Length; i++) island[i] = -1;
      var queue = new Queue<Cell>();
      foreach (var seed in grid.FreeCells())
      {
        if (island[grid.Index(seed)] >= 0) continue;
        int id = sizes.Count, size = 0;
        island[grid.Index(seed)] = id;
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
          var c = queue.Dequeue();
          size++;
          foreach (var (du, dv) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
            if (grid.TryStep(c, du, dv, out var next) && island[grid.Index(next)] < 0)
            {
              island[grid.Index(next)] = id;
              queue.Enqueue(next);
            }
        }
        sizes.Add(size);
      }
      return island;
    }
  }
}
