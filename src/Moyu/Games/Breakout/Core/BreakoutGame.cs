using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Breakout.Core
{
  public enum BreakoutMode { Classic, Blitz, Zen }

  public enum BreakoutState { Serving, Playing, Paused, Cleared, Lost, TimeUp }

  public enum PowerKind { Wide, Multi, Slow, Sticky, Pierce, Life, Narrow }

  public sealed class Ball
  {
    public double X, Y, Vx, Vy;
    /// <summary>Held on the paddle (serve or sticky catch), at this offset from its centre.</summary>
    public bool Held;
    public double HeldOffset;
    /// <summary>Bounces off walls and steel since the ball last met the paddle or a breakable brick.</summary>
    public int Idle;
  }

  public sealed class Power
  {
    public double X, Y;
    public PowerKind Kind;
  }

  /// <summary>What the last brick hit did, for pop-ups and debris.</summary>
  public sealed class HitEvent
  {
    public Brick Brick;
    public bool Broken;
    public long Points;
    public int Combo;
    public double Time;
  }

  public sealed class BreakoutSettings
  {
    public BreakoutMode Mode = BreakoutMode.Classic;
    public int StartLevel = 1;
    public int Seed = Environment.TickCount;
    public double Width = 13, Height = 17;
    public int Lives = 3;
    public double BlitzSeconds = 180;
    /// <summary>Bond for every level; null = the six bonds in turn.</summary>
    public BondType? Bond;
    /// <summary>A custom wall instead of a bond.</summary>
    public CustomWall Custom;
    public int Courses = 10;
    public double PowerChance = 0.15;
    /// <summary>The first drops of a game show each helpful power-up once, in a random order, before the odds take over.</summary>
    public bool PowerTour = true;
  }

  /// <summary>
  /// Rules of Rebound, free of Rhino and real time: a paddle at the bottom of a walled field, balls that bounce off
  /// walls, bricks and the paddle (where it hits the paddle sets the angle), bricks that take one to three hits,
  /// power-ups, combos, levels laid in real brick bonds. Same seed and inputs, same game.
  /// </summary>
  public sealed class BreakoutGame
  {
    public const double BallRadius = 0.24, PaddleY = 1.0, PaddleHeight = 0.35, PaddleWidth = 2.4, PaddleSpeed = 16,
      MaxAngle = 60 * Math.PI / 180, MinVertical = 0.28, PowerFall = 4.2, PowerSize = 0.5, MaxSpeed = 16, ClearPause = 1.6;
    public const int MaxCombo = 8, MaxLives = 5;
    /// <summary>Multi stops splitting here: more balls only crowd the field and slow the viewport down.</summary>
    public const int MaxBalls = 9;
    /// <summary>
    /// From the <see cref="IdleBounces"/>-th bounce in a row off walls and steel alone, each one turns the ball by
    /// <see cref="IdleTurn"/> with a chance of one in <see cref="IdleBounces"/> (see Wander).
    /// </summary>
    public const int IdleBounces = 8;
    public const double IdleTurn = 4 * Math.PI / 180;
    /// <summary>
    /// Power-ups wear off by use, not by time: wide, narrow and slow count paddle returns, sticky counts catches,
    /// pierce counts the bricks it goes through.
    /// </summary>
    public const int WideHits = 8, NarrowHits = 5, SlowHits = 6, StickyCatches = 3, PierceBricks = 6;

    public readonly BreakoutSettings Settings;
    readonly Random _rng;
    readonly List<Brick> _bricks = new List<Brick>();
    readonly List<Ball> _balls = new List<Ball>();
    readonly List<Power> _powers = new List<Power>();
    double _speedUp = 1, _clearLeft;
    BreakoutState _beforePause;

    public BreakoutState State { get; private set; }
    public int Level { get; private set; }
    public int Lives { get; private set; }
    public long Score { get; private set; }
    public int Combo { get; private set; }
    public int BricksBroken { get; private set; }
    public double Elapsed { get; private set; }
    public double PaddleX { get; private set; }
    /// <summary>Keyboard: -1, 0 or 1. Ignored while a mouse target is set.</summary>
    public int Steer { get; set; }
    /// <summary>Mouse: the x the paddle chases; NaN = keyboard.</summary>
    public double TargetX { get; set; } = double.NaN;
    /// <summary>Uses left of each power-up (0 = off).</summary>
    public int WideLeft { get; private set; }
    public int NarrowLeft { get; private set; }
    public int SlowLeft { get; private set; }
    public int PierceLeft { get; private set; }
    public int StickyLeft { get; private set; }
    public double LifeLostTime { get; private set; } = double.NegativeInfinity;
    public double ClearedTime { get; private set; } = double.NegativeInfinity;
    public double LevelStartTime { get; private set; }
    public HitEvent LastHit { get; private set; }
    /// <summary>Counts up whenever a brick's state changes or the wall is replaced, so a drawn wall knows when it is stale.</summary>
    public int WallVersion { get; private set; }
    public (PowerKind kind, double time)? LastPower { get; private set; }
    /// <summary>Bricks broken since the wall was laid, for debris and for the facade study's windows.</summary>
    public readonly List<Brick> Broken = new List<Brick>();

    /// <summary>The helpful power-ups, each shown once at the start of a game.</summary>
    public static readonly PowerKind[] Tour = { PowerKind.Wide, PowerKind.Multi, PowerKind.Slow, PowerKind.Sticky, PowerKind.Pierce, PowerKind.Life };
    readonly Queue<PowerKind> _tour = new Queue<PowerKind>();

    /// <summary>Chance that a broken brick drops a power-up: generous on the first two walls so the power-ups show up early.</summary>
    public double PowerChanceNow => Math.Min(1, Settings.PowerChance * (Level <= 1 ? 2.4 : Level == 2 ? 1.6 : 1));

    public BreakoutGame(BreakoutSettings settings)
    {
      Settings = settings ?? new BreakoutSettings();
      _rng = new Random(Settings.Seed);
      foreach (var k in Tour.OrderBy(_ => _rng.Next())) _tour.Enqueue(k);
      Level = Math.Max(1, Settings.StartLevel);
      Lives = Settings.Lives;
      PaddleX = Settings.Width / 2;
      LayWall();
      Serve();
    }

    public IReadOnlyList<Brick> Bricks => _bricks;
    public IReadOnlyList<Ball> Balls => _balls;
    public IReadOnlyList<Power> Powers => _powers;
    public bool IsOver => State == BreakoutState.Lost || State == BreakoutState.TimeUp;
    public double TimeLeft => Settings.Mode == BreakoutMode.Blitz ? Math.Max(0, Settings.BlitzSeconds - Elapsed) : double.PositiveInfinity;
    public BondType Bond => Settings.Bond ?? Bonds.ForLevel(Level);
    public string WallName => Settings.Custom != null ? "自定义 Custom wall" : Bonds.Names[(int)Bond];
    public int Remaining => _bricks.Count(b => b.Alive && b.Breakable);
    public int Total { get; private set; }
    public double Progress => Total == 0 ? 1 : 1 - Remaining / (double)Total;

    public double PaddleWidthNow
    {
      get
      {
        double w = PaddleWidth;
        if (WideLeft > 0) w *= 1.5;
        if (NarrowLeft > 0) w *= 0.65;
        return w;
      }
    }

    /// <summary>Ball speed for the level, nudged up by rallies and down by the slow power-up.</summary>
    public double BallSpeed => Math.Min(MaxSpeed, (7.5 + 0.45 * (Level - 1)) * _speedUp) * (SlowLeft > 0 ? 0.7 : 1);

    // ---------------------------------------------------------------- setup

    void LayWall()
    {
      _bricks.Clear();
      Broken.Clear();
      _powers.Clear();
      double top = Settings.Height - Bonds.Headroom;
      if (Settings.Custom != null) _bricks.AddRange(Bonds.FitCustom(Settings.Custom, Settings.Width, top, Settings.Height * 0.45));
      // A custom wall with nothing left to play on falls back to the bonds.
      if (_bricks.Count == 0) _bricks.AddRange(Bonds.Lay(Bond, Level, Settings.Width, top, Settings.Courses, _rng));
      Total = Remaining;
      WallVersion++;
      LevelStartTime = Elapsed;
      _speedUp = 1;
    }

    void Serve()
    {
      _balls.Clear();
      _balls.Add(new Ball { Held = true, HeldOffset = 0.3, X = PaddleX + 0.3, Y = PaddleY + PaddleHeight / 2 + BallRadius });
      Combo = 0;
      State = BreakoutState.Serving;
    }

    // ---------------------------------------------------------------- input

    /// <summary>Space: launch a held ball (a serve or a sticky catch).</summary>
    public void Launch()
    {
      if (State != BreakoutState.Serving && State != BreakoutState.Playing) return;
      foreach (var b in _balls.Where(b => b.Held))
      {
        b.Held = false;
        SetAngle(b, b.HeldOffset / (PaddleWidthNow / 2) * 0.5);
      }
      State = BreakoutState.Playing;
    }

    public void Pause()
    {
      if (State != BreakoutState.Serving && State != BreakoutState.Playing && State != BreakoutState.Cleared) return;
      _beforePause = State;
      State = BreakoutState.Paused;
    }

    public void Resume()
    {
      if (State == BreakoutState.Paused) State = _beforePause;
    }

    // ---------------------------------------------------------------- time

    public void Tick(double dt)
    {
      if (dt <= 0 || State == BreakoutState.Paused || IsOver) return;
      Elapsed += dt;
      if (Settings.Mode == BreakoutMode.Blitz && Elapsed >= Settings.BlitzSeconds)
      {
        State = BreakoutState.TimeUp;
        return;
      }
      if (State == BreakoutState.Cleared)
      {
        _clearLeft -= dt;
        if (_clearLeft <= 0)
        {
          Level++;
          LayWall();
          Serve();
        }
        return;
      }
      MovePaddle(dt);
      double half = PaddleWidthNow / 2;
      foreach (var b in _balls)
      {
        if (!b.Held) continue;
        // The paddle may have shrunk since the catch (a wide that ran out, a narrow caught): keep the ball on it.
        b.HeldOffset = Math.Max(-half, Math.Min(half, b.HeldOffset));
        b.X = PaddleX + b.HeldOffset;
        b.Y = PaddleY + PaddleHeight / 2 + BallRadius;
      }
      if (State == BreakoutState.Playing) MoveBalls(dt);
      MovePowers(dt);
    }

    void MovePaddle(double dt)
    {
      double half = PaddleWidthNow / 2, target;
      if (!double.IsNaN(TargetX)) target = TargetX;
      else target = PaddleX + Steer * PaddleSpeed * dt;
      double step = PaddleSpeed * 1.6 * dt;
      PaddleX += Math.Max(-step, Math.Min(step, target - PaddleX));
      PaddleX = Math.Max(half, Math.Min(Settings.Width - half, PaddleX));
    }

    void MoveBalls(double dt)
    {
      // Breaking the last brick clears the wall and empties the list mid-loop: stop there.
      for (int i = _balls.Count - 1; i >= 0 && State == BreakoutState.Playing; i--)
      {
        var ball = _balls[i];
        if (ball.Held) continue;
        double speed = BallSpeed;
        Normalise(ball, speed);
        double dist = speed * dt;
        int steps = Math.Max(1, (int)Math.Ceiling(dist / (BallRadius * 0.45)));
        for (int s = 0; s < steps && _balls.Contains(ball) && !ball.Held; s++)
        {
          ball.X += ball.Vx * dt / steps;
          ball.Y += ball.Vy * dt / steps;
          Collide(ball);
        }
      }
      if (State == BreakoutState.Playing && _balls.Count == 0) LoseLife();
    }

    void Collide(Ball ball)
    {
      double r = BallRadius, w = Settings.Width, h = Settings.Height;
      if (ball.X < r) { ball.X = r; ball.Vx = Math.Abs(ball.Vx); KeepSteep(ball); Wander(ball); }
      if (ball.X > w - r) { ball.X = w - r; ball.Vx = -Math.Abs(ball.Vx); KeepSteep(ball); Wander(ball); }
      if (ball.Y > h - r) { ball.Y = h - r; ball.Vy = -Math.Abs(ball.Vy); Wander(ball); }
      if (ball.Y < -2 * r)
      {
        _balls.Remove(ball);
        return;
      }
      // Paddle: only when coming down onto its top.
      double half = PaddleWidthNow / 2, top = PaddleY + PaddleHeight / 2;
      if (ball.Vy < 0 && ball.Y - r <= top && ball.Y >= PaddleY - PaddleHeight / 2 && Math.Abs(ball.X - PaddleX) <= half + r * 0.6)
      {
        double f = Math.Max(-1, Math.Min(1, (ball.X - PaddleX) / (half + r)));
        ball.Y = top + r;
        ball.Idle = 0;
        Combo = 0;
        _speedUp = Math.Min(1.25, _speedUp * 1.015);
        // Each return uses up one go of the paddle power-ups.
        if (WideLeft > 0) WideLeft--;
        if (NarrowLeft > 0) NarrowLeft--;
        if (SlowLeft > 0) SlowLeft--;
        if (StickyLeft > 0)
        {
          StickyLeft--;
          ball.Held = true;
          half = PaddleWidthNow / 2;
          ball.HeldOffset = Math.Max(-half, Math.Min(half, ball.X - PaddleX));
          ball.Vx = ball.Vy = 0;
          return;
        }
        SetAngle(ball, f);
        return;
      }
      // Bricks: take the deepest overlap this sub-step.
      Brick hit = null;
      double best = 0, nx = 0, ny = 0;
      foreach (var b in _bricks)
      {
        if (!b.Alive) continue;
        double cx = Math.Max(b.X0, Math.Min(ball.X, b.X1)), cy = Math.Max(b.Y0, Math.Min(ball.Y, b.Y1));
        double dx = ball.X - cx, dy = ball.Y - cy, d2 = dx * dx + dy * dy;
        if (d2 >= r * r) continue;
        double d = Math.Sqrt(d2), pen;
        double ux, uy;
        if (d > 1e-9)
        {
          ux = dx / d;
          uy = dy / d;
          pen = r - d;
        }
        else
        {
          // Centre inside the brick: push out along the shallowest side.
          double l = ball.X - b.X0, rr = b.X1 - ball.X, bo = ball.Y - b.Y0, tp = b.Y1 - ball.Y, m = Math.Min(Math.Min(l, rr), Math.Min(bo, tp));
          ux = m == l ? -1 : m == rr ? 1 : 0;
          uy = m == bo ? -1 : m == tp ? 1 : 0;
          pen = m + r;
        }
        if (pen > best)
        {
          best = pen;
          hit = b;
          nx = ux;
          ny = uy;
        }
      }
      if (hit == null) return;
      bool pierce = PierceLeft > 0 && hit.Breakable;
      if (pierce) PierceLeft--;
      else
      {
        ball.X += nx * best;
        ball.Y += ny * best;
        double vn = ball.Vx * nx + ball.Vy * ny;
        if (vn < 0)
        {
          ball.Vx -= 2 * vn * nx;
          ball.Vy -= 2 * vn * ny;
        }
        KeepSteep(ball);
      }
      // A breakable brick is progress; steel is one more bounce of a ball that may be going round in circles.
      if (hit.Breakable) ball.Idle = 0;
      else Wander(ball);
      Damage(hit, pierce);
    }

    void Damage(Brick b, bool pierce)
    {
      if (!b.Breakable) return;
      b.Hits = pierce ? 0 : b.Hits - 1;
      WallVersion++;
      Combo = Math.Min(MaxCombo, Combo + 1);
      long points = (b.Material == BrickMaterial.Concrete ? 15 : 10) * (long)Combo;
      Score += points;
      LastHit = new HitEvent { Brick = b, Broken = !b.Alive, Points = points, Combo = Combo, Time = Elapsed };
      if (b.Alive) return;
      BricksBroken++;
      Broken.Add(b);
      // The last brick drops nothing: Clear() sweeps the field, and a power-up from the tour would be gone for good.
      if (Remaining == 0)
      {
        Clear();
        return;
      }
      if (_rng.NextDouble() < PowerChanceNow)
      {
        var kind = Settings.PowerTour && _tour.Count > 0 ? _tour.Dequeue() : RandomPower();
        _powers.Add(new Power { X = (b.X0 + b.X1) / 2, Y = (b.Y0 + b.Y1) / 2, Kind = kind });
      }
    }

    PowerKind RandomPower()
    {
      double r = _rng.NextDouble();
      // Sticky is the rarest of the helpful ones (7 %): it changes the rhythm most.
      return r < 0.22 ? PowerKind.Wide : r < 0.40 ? PowerKind.Multi : r < 0.56 ? PowerKind.Slow : r < 0.63 ? PowerKind.Sticky : r < 0.76 ? PowerKind.Pierce : r < 0.85 ? PowerKind.Life : PowerKind.Narrow;
    }

    void Clear()
    {
      Score += 500L * Level;
      ClearedTime = Elapsed;
      _clearLeft = ClearPause;
      _balls.Clear();
      _powers.Clear();
      State = BreakoutState.Cleared;
    }

    void LoseLife()
    {
      LifeLostTime = Elapsed;
      _powers.Clear();
      WideLeft = NarrowLeft = SlowLeft = PierceLeft = StickyLeft = 0;
      // The next serve starts at the level's base speed, not where the lost rally left it.
      _speedUp = 1;
      if (Settings.Mode != BreakoutMode.Zen) Lives--;
      if (Lives <= 0)
      {
        State = BreakoutState.Lost;
        return;
      }
      Serve();
    }

    void MovePowers(double dt)
    {
      double half = PaddleWidthNow / 2, top = PaddleY + PaddleHeight / 2;
      for (int i = _powers.Count - 1; i >= 0; i--)
      {
        var p = _powers[i];
        p.Y -= PowerFall * dt;
        if (p.Y - PowerSize / 2 <= top && p.Y + PowerSize / 2 >= PaddleY - PaddleHeight / 2 && Math.Abs(p.X - PaddleX) <= half + PowerSize / 2)
        {
          _powers.Remove(p);
          Apply(p.Kind);
        }
        else if (p.Y < -1) _powers.Remove(p);
      }
    }

    /// <summary>Gives a power-up as if caught (tests and scripted demos use it too).</summary>
    public void Apply(PowerKind kind)
    {
      LastPower = (kind, Elapsed);
      switch (kind)
      {
        case PowerKind.Wide: WideLeft = WideHits; NarrowLeft = 0; break;
        case PowerKind.Narrow: NarrowLeft = NarrowHits; WideLeft = 0; break;
        case PowerKind.Slow: SlowLeft = SlowHits; break;
        case PowerKind.Sticky: StickyLeft = StickyCatches; break;
        case PowerKind.Pierce: PierceLeft = PierceBricks; break;
        case PowerKind.Life: Lives = Math.Min(MaxLives, Lives + 1); break;
        case PowerKind.Multi:
          // Caught while every ball is stuck to the paddle: let them go, so there is something to split.
          if (_balls.Count > 0 && _balls.All(b => b.Held)) Launch();
          foreach (var b in _balls.Where(b => !b.Held).ToList())
            foreach (double turn in new[] { -0.3, 0.3 })
            {
              if (_balls.Count >= MaxBalls) break;
              double c = Math.Cos(turn), s = Math.Sin(turn);
              _balls.Add(new Ball { X = b.X, Y = b.Y, Vx = b.Vx * c - b.Vy * s, Vy = b.Vx * s + b.Vy * c });
            }
          break;
      }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Points the ball up the screen at f × <see cref="MaxAngle"/> from vertical (f in −1 … 1).</summary>
    void SetAngle(Ball b, double f)
    {
      double a = Math.Max(-1, Math.Min(1, f)) * MaxAngle, speed = BallSpeed;
      b.Vx = Math.Sin(a) * speed;
      b.Vy = Math.Cos(a) * speed;
    }

    /// <summary>Never let a ball settle into a near-horizontal shuttle between the side walls.</summary>
    void KeepSteep(Ball b)
    {
      double speed = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
      if (speed < 1e-9) return;
      if (Math.Abs(b.Vy) < MinVertical * speed)
      {
        b.Vy = (b.Vy >= 0 ? 1 : -1) * MinVertical * speed;
        b.Vx = Math.Sign(b.Vx) * Math.Sqrt(Math.Max(0, speed * speed - b.Vy * b.Vy));
      }
    }

    /// <summary>
    /// Called on each bounce off a wall or steel. Steel never breaks and bounces off it are exact, so a ball can settle
    /// into a loop that never comes back to the paddle or a brick: after a few such bounces in a row it now and then
    /// turns a few degrees, too little to notice, until it finds its way out.
    /// </summary>
    void Wander(Ball b)
    {
      // At random bounces, not every n-th: a loop n bounces long would otherwise take the turn at the same point
      // every time round and stay a loop. The game's own generator keeps a replay exact.
      if (++b.Idle < IdleBounces || _rng.Next(IdleBounces) != 0) return;
      double speed = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
      // Turn steeper, so a ball skimming between steel drops through a gap and comes back down to the paddle; one
      // already within about 15° of vertical turns away from it instead, so it drifts out of a shaft. A fixed turn
      // would cancel itself out over a loop's mirror bounces.
      double turn = (b.Vx >= 0) == (b.Vy >= 0) ? IdleTurn : -IdleTurn;
      if (Math.Abs(b.Vx) < 0.26 * speed) turn = -turn;
      double c = Math.Cos(turn), s = Math.Sin(turn), vx = b.Vx * c - b.Vy * s;
      b.Vy = b.Vx * s + b.Vy * c;
      b.Vx = vx;
    }

    static void Normalise(Ball b, double speed)
    {
      double s = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
      if (s < 1e-9)
      {
        b.Vy = speed;
        return;
      }
      b.Vx *= speed / s;
      b.Vy *= speed / s;
    }

    /// <summary>Replaces the balls in play with one at a position and velocity (tests and scripted demos).</summary>
    public Ball AddBall(double x, double y, double vx, double vy)
    {
      var b = new Ball { X = x, Y = y, Vx = vx, Vy = vy };
      _balls.Clear();
      _balls.Add(b);
      State = BreakoutState.Playing;
      return b;
    }

    /// <summary>Replaces the wall (tests).</summary>
    public void SetWall(IEnumerable<Brick> bricks)
    {
      _bricks.Clear();
      _bricks.AddRange(bricks);
      Broken.Clear();
      Total = Remaining;
      WallVersion++;
    }
  }
}
