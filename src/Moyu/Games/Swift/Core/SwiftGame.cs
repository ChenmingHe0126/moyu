using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Swift.Core
{
  public enum SwiftMode { Classic, Zen }

  /// <summary>Wind exposure classes stand in for difficulty: sheltered is gentle, exposed is fast and tight.</summary>
  public enum Exposure { Sheltered, Normal, Exposed }

  public enum SwiftState { Ready, Flying, Crashed, Over, Paused }

  public sealed class SwiftSettings
  {
    public SwiftMode Mode = SwiftMode.Classic;
    public Exposure Exposure = Exposure.Normal;
    /// <summary>Multiplies the height of every opening (1 = standard).</summary>
    public double Opening = 1;
    /// <summary>Multiplies the distance between cross walls (1 = standard).</summary>
    public double Bay = 1;
    public int Seed = 1;

    public SwiftSettings Copy() => (SwiftSettings)MemberwiseClone();
  }

  /// <summary>A point in course units: x along the course, y up from the floor.</summary>
  public readonly struct V2
  {
    public readonly double X, Y;
    public V2(double x, double y)
    {
      X = x;
      Y = y;
    }
    public double DistanceTo(V2 o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
    public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Y + b.Y);
    public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Y - b.Y);
    public static V2 operator *(V2 a, double k) => new V2(a.X * k, a.Y * k);
    public override string ToString() => $"({X:F2}, {Y:F2})";
  }

  /// <summary>A cross wall with one opening to fly through. Shutter walls slide their opening up and down.</summary>
  public sealed class Wall
  {
    public readonly int Index;
    /// <summary>Left face, in course units.</summary>
    public readonly double X;
    /// <summary>Opening centre at rest and opening height.</summary>
    public readonly double Centre, Gap;
    /// <summary>How far a sliding shutter moves the opening either way (0 for a fixed opening).</summary>
    public readonly double Swing, Phase;
    public bool Passed, Hit;

    public Wall(int index, double x, double centre, double gap, double swing = 0, double phase = 0)
    {
      Index = index;
      X = x;
      Centre = centre;
      Gap = gap;
      Swing = swing;
      Phase = phase;
    }

    public double Right => X + SwiftGame.WallThickness;
    public double Middle => X + SwiftGame.WallThickness / 2;
    public bool Moving => Swing > 0;
    public double CentreAt(double t) => Centre + Swing * Math.Sin(t * SwiftGame.ShutterOmega + Phase);
  }

  public sealed class Feather
  {
    public readonly double X, Y;
    public bool Taken;
    public double TakenAt;

    public Feather(double x, double y)
    {
      X = x;
      Y = y;
    }
  }

  /// <summary>
  /// Swift: a one-button flyer through an endless building section. Each flap sets the swift climbing, gravity
  /// pulls it back down, and cross walls with one opening each scroll past. Passing a wall scores; touching a wall
  /// or the floor ends a classic flight (in Zen the swift bumps through and carries on). Feathers between walls are
  /// a bonus. The first walls are easy (tall openings near the middle, a slow course); openings then shrink, the
  /// course speeds up and the openings swing further up and down, and later some openings are sliding shutters.
  /// Course units: the floor is y = 0, the ceiling y = <see cref="Height"/>.
  /// </summary>
  public sealed class SwiftGame
  {
    public const double Height = 12, BirdX = 5, Radius = 0.38, Gravity = 27, FlapSpeed = 9.0, MaxFall = 13,
      WallThickness = 1.4, FirstWall = 16, Ahead = 42, Margin = 1.0, MaxShift = 4.5, ShutterOmega = 2 * Math.PI / 4.0,
      ShutterBonus = 0.5, BayTime = 0.95, MinGap = 2.8, FeatherRadius = 0.6, CrashSeconds = 1.2, Step = 1.0 / 240, TrailStep = 0.04;
    /// <summary>The hall's back wall: a tall window every <see cref="WindowPitch"/> units (the game draws it, the boss section shows it beyond).</summary>
    public const double WindowPitch = 6.0, WindowWidth = 2.6, WindowSill = 2.4, WindowHead = 9.6;
    const int TrailMax = 600;

    public readonly SwiftSettings Settings;
    readonly Random _rng;
    readonly List<Wall> _walls = new List<Wall>();
    readonly List<Feather> _feathers = new List<Feather>();
    readonly List<V2> _trail = new List<V2>();
    double _pending, _trailClock;
    int _nextIndex;
    SwiftState _beforePause;

    public SwiftState State { get; private set; } = SwiftState.Ready;
    public double Y { get; private set; } = Height / 2;
    public double Vy { get; private set; }
    /// <summary>How far the course has scrolled; the swift flies at course x = Distance + BirdX.</summary>
    public double Distance { get; private set; }
    /// <summary>Seconds of flight (drives the shutters).</summary>
    public double Elapsed { get; private set; }
    /// <summary>Seconds not paused, including the wait before the first flap (drives animation).</summary>
    public double Clock { get; private set; }
    public int Score { get; private set; }
    public int Feathers { get; private set; }
    public int Flaps { get; private set; }
    /// <summary>Zen bumps: walls touched and floor bounces.</summary>
    public int Bumps { get; private set; }
    public double LastFlap { get; private set; } = -10;
    public double LastBump { get; private set; } = -10;
    public double CrashTime { get; private set; }
    public bool HitWall { get; private set; }

    public IReadOnlyList<Wall> Walls => _walls;
    public IReadOnlyList<Feather> FeatherList => _feathers;
    /// <summary>The swift's recent path in course units, oldest first.</summary>
    public IReadOnlyList<V2> Trail => _trail;

    public SwiftGame(SwiftSettings settings)
    {
      Settings = settings.Copy();
      // Math.Min / Max pass NaN through, so a bad factor would give the course NaN walls.
      if (!(Settings.Opening > 0)) Settings.Opening = 1;
      if (!(Settings.Bay > 0)) Settings.Bay = 1;
      _rng = new Random(settings.Seed);
      Fill();
    }

    public double BirdCourseX => Distance + BirdX;
    public V2 Bird => new V2(BirdCourseX, Y);
    public bool IsOver => State == SwiftState.Over;
    public bool Zen => Settings.Mode == SwiftMode.Zen;
    public int Level => 1 + Score / 10;

    /// <summary>Course speed in units per second once <paramref name="passed"/> walls are behind.</summary>
    public double SpeedAt(int passed)
    {
      double b = Settings.Exposure == Exposure.Sheltered ? 3.9 : Settings.Exposure == Exposure.Exposed ? 5.2 : 4.4;
      return b + Math.Min(2.0, passed * 0.04);
    }

    public double Speed => SpeedAt(Score);

    public double GapAt(int index)
    {
      double b = Settings.Exposure == Exposure.Sheltered ? 5.6 : Settings.Exposure == Exposure.Exposed ? 4.2 : 5.0;
      return Math.Max(MinGap, (b - Math.Min(1.4, index * 0.03)) * Settings.Opening);
    }

    /// <summary>
    /// Distance between wall faces. Walls close up as the score rises, but never to less than <see cref="BayTime"/>
    /// seconds of flight apart at the speed they are met, so a dive from a high opening to a low one stays possible.
    /// </summary>
    public double PitchAt(int index)
    {
      double b = Settings.Exposure == Exposure.Sheltered ? 9.6 : Settings.Exposure == Exposure.Exposed ? 8.4 : 9.0;
      return Math.Max(SpeedAt(index) * BayTime + WallThickness, (b - Math.Min(1.2, index * 0.02)) * Settings.Bay);
    }

    /// <summary>Walls from this index on may be sliding shutters.</summary>
    public int ShuttersFrom => Settings.Exposure == Exposure.Sheltered ? 40 : Settings.Exposure == Exposure.Exposed ? 15 : 25;

    /// <summary>How far an opening may sit from the last one: small steps for the first walls, the full swing by wall 24.</summary>
    public static double MaxShiftAt(int index) => Math.Min(MaxShift, 1.6 + index * 0.12);

    /// <summary>The first wall the swift has not yet cleared.</summary>
    public Wall NextWall => _walls.FirstOrDefault(w => w.Right + Radius > BirdCourseX);

    // ---------------------------------------------------------------- course

    void Fill()
    {
      while (_walls.Count == 0 || _walls[_walls.Count - 1].X < Distance + Ahead)
      {
        int i = _nextIndex++;
        var prev = _walls.Count > 0 ? _walls[_walls.Count - 1] : null;
        double x = prev == null ? FirstWall + BirdX : prev.X + PitchAt(i);
        double gap = GapAt(i);
        double swing = 0, phase = 0;
        if (i >= ShuttersFrom && _rng.NextDouble() < Math.Min(0.5, 0.3 + (i - ShuttersFrom) * 0.01))
        {
          swing = 0.8 + _rng.NextDouble() * 0.4;
          phase = _rng.NextDouble() * Math.PI * 2;
          // A moving opening is a little taller, so it stays fair at full speed.
          gap += ShutterBonus;
        }
        double lo = Margin + gap / 2 + swing, hi = Height - Margin - gap / 2 - swing;
        // Shutters on either side eat into the climb or dive between the two openings.
        double from = prev?.Centre ?? Height / 2, shift = MaxShiftAt(i) - swing - (prev?.Swing ?? 0);
        double c = Math.Max(lo, Math.Min(hi, from + (_rng.NextDouble() * 2 - 1) * shift));
        var wall = new Wall(i, x, c, gap, swing, phase);
        if (prev != null && _rng.NextDouble() < 0.3)
        {
          // A feather halfway along the bay, on the line between the two openings, a little off it.
          double mx = (prev.Right + x) / 2;
          double my = (prev.Centre + c) / 2 + (_rng.NextDouble() - 0.5) * 1.6;
          _feathers.Add(new Feather(mx, Math.Max(1.2, Math.Min(Height - 1.2, my))));
        }
        _walls.Add(wall);
      }
      _walls.RemoveAll(w => w.Right < Distance - 8);
      _feathers.RemoveAll(f => f.X < Distance - 8);
    }

    // ---------------------------------------------------------------- input

    public void Flap()
    {
      if (State == SwiftState.Ready)
      {
        State = SwiftState.Flying;
        _trail.Clear();
      }
      if (State != SwiftState.Flying) return;
      Vy = FlapSpeed;
      Flaps++;
      LastFlap = Clock;
    }

    /// <summary>Pauses the wait or the flight; the fall after a wall hit just plays out.</summary>
    public void Pause()
    {
      if (State == SwiftState.Ready || State == SwiftState.Flying)
      {
        _beforePause = State;
        State = SwiftState.Paused;
      }
    }

    public void Resume()
    {
      if (State == SwiftState.Paused) State = _beforePause;
    }

    // ---------------------------------------------------------------- simulation

    public void Tick(double dt)
    {
      if (State == SwiftState.Paused || State == SwiftState.Over || dt <= 0) return;
      _pending += Math.Min(dt, 0.25);
      while (_pending >= Step)
      {
        _pending -= Step;
        StepOnce(Step);
        if (State == SwiftState.Over) break;
      }
    }

    void StepOnce(double dt)
    {
      Clock += dt;
      switch (State)
      {
        case SwiftState.Ready:
          Y = Height / 2 + 0.25 * Math.Sin(Clock * 4);
          return;
        case SwiftState.Crashed:
          Vy = Math.Max(Vy - Gravity * dt, -MaxFall);
          Y += Vy * dt;
          if (Y <= Radius || Clock - CrashTime > CrashSeconds)
          {
            Y = Math.Max(Radius, Y);
            Vy = 0;
            State = SwiftState.Over;
          }
          return;
        case SwiftState.Flying:
          break;
        default:
          return;
      }

      Elapsed += dt;
      Vy = Math.Max(Vy - Gravity * dt, -MaxFall);
      Y += Vy * dt;
      if (Y > Height - Radius)
      {
        Y = Height - Radius;
        Vy = Math.Min(Vy, 0);
      }
      Distance += Speed * dt;
      Fill();

      _trailClock += dt;
      if (_trailClock >= TrailStep)
      {
        _trailClock = 0;
        _trail.Add(Bird);
        if (_trail.Count > TrailMax) _trail.RemoveAt(0);
      }

      double bx = BirdCourseX;
      foreach (var w in _walls)
      {
        if (w.Right + Radius < bx || w.X - Radius > bx) continue;
        if (!Touches(w, bx, Y)) continue;
        if (Zen)
        {
          if (!w.Hit)
          {
            w.Hit = true;
            Bumps++;
            LastBump = Clock;
          }
          continue;
        }
        w.Hit = true;
        Crash(true);
        return;
      }
      foreach (var w in _walls)
        if (!w.Passed && bx > w.Middle)
        {
          w.Passed = true;
          Score++;
        }
      foreach (var f in _feathers)
        if (!f.Taken && Math.Abs(f.X - bx) < FeatherRadius + Radius && new V2(f.X, f.Y).DistanceTo(Bird) < FeatherRadius + Radius)
        {
          f.Taken = true;
          f.TakenAt = Clock;
          Feathers++;
        }

      if (Y <= Radius)
      {
        if (Zen)
        {
          Y = Radius;
          Vy = FlapSpeed * 0.8;
          Bumps++;
          LastBump = Clock;
        }
        else
        {
          Y = Radius;
          Crash(false);
        }
      }
    }

    void Crash(bool wall)
    {
      HitWall = wall;
      CrashTime = Clock;
      if (wall)
      {
        // Knocked back and down; the course stops while the swift falls to the floor.
        Vy = Math.Min(Vy, 2);
        State = SwiftState.Crashed;
      }
      else
      {
        Vy = 0;
        State = SwiftState.Over;
      }
    }

    /// <summary>Does a swift at (x, y) touch the solid parts of the wall (everything but its opening)?</summary>
    public bool Touches(Wall w, double x, double y)
    {
      double c = w.CentreAt(Elapsed), lo = c - w.Gap / 2, hi = c + w.Gap / 2;
      double cx = Math.Max(w.X, Math.Min(w.Right, x));
      // Lower part: from the floor up to the sill; upper part: from the head up to the ceiling.
      double below = Math.Min(y, lo), above = Math.Max(y, hi);
      double d1 = Dist(x, y, cx, below), d2 = Dist(x, y, cx, above);
      return d1 < Radius || d2 < Radius;
    }

    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
  }
}
