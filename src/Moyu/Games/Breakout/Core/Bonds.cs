using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Breakout.Core
{
  public enum BrickMaterial { Glass, Brick, Concrete, Steel }

  /// <summary>Real brickwork patterns, one per level, so the wall reads as a facade.</summary>
  public enum BondType { Stretcher, Stack, English, Flemish, Screen, Arch }

  /// <summary>A brick or panel: an axis-aligned rectangle in field units, with how many hits it has left.</summary>
  public sealed class Brick
  {
    public double X0, Y0, X1, Y1;
    public BrickMaterial Material;
    public int Hits;
    public int MaxHits;
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public double Area => Width * Height;
    public bool Breakable => Material != BrickMaterial.Steel;
    public bool Alive => Hits > 0;

    public Brick(double x0, double y0, double x1, double y1, BrickMaterial material)
    {
      X0 = x0;
      Y0 = y0;
      X1 = x1;
      Y1 = y1;
      Material = material;
      MaxHits = Hits = HitsFor(material);
    }

    public static int HitsFor(BrickMaterial m) => m == BrickMaterial.Glass ? 1 : m == BrickMaterial.Brick ? 2 : m == BrickMaterial.Concrete ? 3 : int.MaxValue;

    public Brick Copy() => new Brick(X0, Y0, X1, Y1, Material);
  }

  /// <summary>A custom wall: rectangles in a unit-free 2D frame (scaled into the brick zone by <see cref="Bonds.FitCustom"/>).</summary>
  public sealed class CustomWall
  {
    public readonly List<(double x0, double y0, double x1, double y1)> Rects = new List<(double, double, double, double)>();
  }

  public static class Bonds
  {
    public const double Course = 0.55, Joint = 0.08, Stretcher = 1.6, Header = 0.8;
    /// <summary>Free field between the top course and the top wall.</summary>
    public const double Headroom = 1.2;

    public static readonly string[] Names = { "顺砌 Stretcher bond", "通缝 Stack bond", "英式 English bond", "荷兰式 Flemish bond", "花墙 Screen wall", "拱 Arch" };

    /// <summary>The bond a level uses when none is given: the six in turn.</summary>
    public static BondType ForLevel(int level) => (BondType)((level - 1) % 6);

    /// <summary>
    /// Lays a wall of <paramref name="courses"/> courses across <paramref name="width"/>, its top course just under
    /// <paramref name="top"/>. Harder materials creep downwards as the level rises; a few steel bricks appear from level 4.
    /// </summary>
    public static List<Brick> Lay(BondType type, int level, double width, double top, int courses, Random rng)
    {
      var bricks = new List<Brick>();
      double pitch = Course + Joint;
      for (int c = 0; c < courses; c++)
      {
        double y1 = top - c * pitch, y0 = y1 - Course;
        double t = courses <= 1 ? 1 : c / (double)(courses - 1); // 0 top … 1 bottom
        foreach (var (x0, x1) in CourseUnits(type, c, width))
        {
          double a = Math.Max(0, x0) + Joint / 2, b = Math.Min(width, x1) - Joint / 2;
          if (b - a < 0.35) continue;
          if (type == BondType.Screen && (c + (int)Math.Floor((x0 + (c % 2 == 1 ? Stretcher / 2 : 0)) / Stretcher)) % 2 == 1) continue;
          var m = MaterialFor(t, level, rng);
          bricks.Add(new Brick(a, y0, b, y1, m));
        }
      }
      if (type == BondType.Arch) CutArch(bricks, width, top - courses * pitch, courses * pitch);
      OpenUp(bricks, width, top + Headroom);
      return bricks;
    }

    /// <summary>The smallest custom panel worth playing: anything thinner than this is skipped by <see cref="FitCustom"/>.</summary>
    public const double MinPanel = 0.3;
    /// <summary>Room a ball needs around a point to sit there.</summary>
    const double Clearance = BreakoutGame.BallRadius + 0.01;

    /// <summary>
    /// Keeps every breakable brick reachable: the ball cannot pass a joint, so a brick walled in by steel (and the
    /// field's sides) could never be hit and the wall never cleared. Breakable bricks spread from the open field through
    /// touching breakable neighbours; any left unreached has its steel neighbours demoted to concrete, until none is left.
    /// </summary>
    public static int OpenUp(List<Brick> bricks, double width, double height)
    {
      int demoted = 0;
      for (bool again = true; again;)
      {
        again = false;
        var reached = new HashSet<Brick>();
        var queue = new Queue<Brick>();
        foreach (var b in bricks)
          if (b.Breakable && Exposed(b, bricks, width, height))
          {
            reached.Add(b);
            queue.Enqueue(b);
          }
        while (queue.Count > 0)
        {
          var a = queue.Dequeue();
          foreach (var b in bricks)
            if (b.Breakable && !reached.Contains(b) && Touch(a, b))
            {
              reached.Add(b);
              queue.Enqueue(b);
            }
        }
        foreach (var a in bricks.Where(b => b.Breakable && !reached.Contains(b)).ToList())
          foreach (var b in bricks)
            if (!b.Breakable && Touch(a, b))
            {
              b.Material = BrickMaterial.Concrete;
              b.Hits = b.MaxHits = Brick.HitsFor(b.Material);
              demoted++;
              again = true;
            }
      }
      return demoted;
    }

    /// <summary>Whether a ball could sit against one of the brick's sides: probes just outside each side, clear of every brick and the field's walls.</summary>
    static bool Exposed(Brick b, List<Brick> bricks, double width, double height)
    {
      double off = Joint + Clearance + 0.05;
      foreach (double f in new[] { 0.2, 0.5, 0.8 })
      {
        double x = b.X0 + b.Width * f, y = b.Y0 + b.Height * f;
        if (Free(x, b.Y1 + off, bricks, width, height) || Free(x, b.Y0 - off, bricks, width, height)) return true;
        if (Free(b.X0 - off, y, bricks, width, height) || Free(b.X1 + off, y, bricks, width, height)) return true;
      }
      return false;
    }

    static bool Free(double x, double y, List<Brick> bricks, double width, double height)
    {
      if (x < Clearance || x > width - Clearance || y < Clearance || y > height - Clearance) return false;
      foreach (var b in bricks)
      {
        double dx = Math.Max(0, Math.Max(b.X0 - x, x - b.X1)), dy = Math.Max(0, Math.Max(b.Y0 - y, y - b.Y1));
        if (dx * dx + dy * dy < Clearance * Clearance) return false;
      }
      return true;
    }

    /// <summary>
    /// Shared edge two neighbours need for a ball to pass from one's hole into the other's. Between steel the opening is
    /// that edge plus a joint either side, so half a header (0.32 + 0.16 = 0.48) is exactly the ball's width: it gets
    /// through only by luck, so such a brick counts as walled in.
    /// </summary>
    const double Passage = 2 * Clearance - 2 * Joint + 0.05;

    /// <summary>Neighbours across a joint sharing enough edge for a ball to reach one through the other's hole.</summary>
    static bool Touch(Brick a, Brick b)
    {
      double gap = Joint + 0.02;
      double ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), oy = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
      return (ox > Passage && oy > -gap && oy <= 1e-9) || (oy > Passage && ox > -gap && ox <= 1e-9);
    }

    static IEnumerable<(double, double)> CourseUnits(BondType type, int c, double width)
    {
      switch (type)
      {
        case BondType.Stack: return Run(0, Stretcher, Stretcher, width);
        case BondType.English: return c % 2 == 0 ? Run(0, Stretcher, Stretcher, width) : Run(-Header / 2, Header, Header, width);
        // Each header sits centred over the stretcher below it.
        case BondType.Flemish: return Alternate(c % 2 == 0 ? 0 : -(Stretcher - Header / 2), width);
        default: return Run(c % 2 == 0 ? 0 : -Stretcher / 2, Stretcher, Stretcher, width);
      }
    }

    static IEnumerable<(double, double)> Run(double start, double first, double len, double width)
    {
      for (double x = start; x < width; x += len) yield return (x, x + len);
    }

    /// <summary>Stretcher, header, stretcher, header … in one course.</summary>
    static IEnumerable<(double, double)> Alternate(double start, double width)
    {
      bool stretcher = true;
      for (double x = start; x < width;)
      {
        double len = stretcher ? Stretcher : Header;
        yield return (x, x + len);
        x += len;
        stretcher = !stretcher;
      }
    }

    static BrickMaterial MaterialFor(double t, int level, Random rng)
    {
      double hard = Math.Min(0.55, 0.12 + 0.06 * (level - 1));
      double soft = Math.Max(0.15, 0.78 - 0.04 * (level - 1));
      if (level >= 4 && rng.NextDouble() < Math.Min(0.1, 0.03 * (level - 3))) return BrickMaterial.Steel;
      if (t < hard) return BrickMaterial.Concrete;
      if (t > soft) return BrickMaterial.Glass;
      return BrickMaterial.Brick;
    }

    /// <summary>An arched opening through the middle of the wall, its ring set in steel.</summary>
    static void CutArch(List<Brick> bricks, double width, double bottom, double height)
    {
      double cx = width / 2, rx = width * 0.22, ry = height * 0.75;
      bool Inside(double x, double y, double grow) => Math.Pow((x - cx) / (rx + grow), 2) + Math.Pow((y - bottom) / (ry + grow), 2) < 1;
      bricks.RemoveAll(b => Inside((b.X0 + b.X1) / 2, (b.Y0 + b.Y1) / 2, 0));
      foreach (var b in bricks)
        if (Inside((b.X0 + b.X1) / 2, (b.Y0 + b.Y1) / 2, 0.9) && Math.Abs((b.X0 + b.X1) / 2 - cx) < rx * 0.8)
        {
          b.Material = BrickMaterial.Steel;
          b.Hits = b.MaxHits = Brick.HitsFor(BrickMaterial.Steel);
        }
    }

    /// <summary>
    /// Fits a custom wall into the brick zone (scaled to the field width, keeping proportions, top-aligned under
    /// <paramref name="top"/>). Smaller panels are glass, larger ones concrete.
    /// </summary>
    public static List<Brick> FitCustom(CustomWall wall, double width, double top, double maxHeight) => FitCustom(wall, width, top, maxHeight, out _);

    /// <param name="tooSmall">Panels skipped because, scaled into the field, they were thinner than <see cref="MinPanel"/>.</param>
    public static List<Brick> FitCustom(CustomWall wall, double width, double top, double maxHeight, out int tooSmall)
    {
      tooSmall = 0;
      var rects = wall.Rects.Where(r => r.x1 > r.x0 && r.y1 > r.y0).ToList();
      if (rects.Count == 0) return new List<Brick>();
      double minX = rects.Min(r => r.x0), maxX = rects.Max(r => r.x1), minY = rects.Min(r => r.y0), maxY = rects.Max(r => r.y1);
      double s = Math.Min((width - 0.4) / (maxX - minX), maxHeight / (maxY - minY));
      double ox = (width - (maxX - minX) * s) / 2;
      tooSmall = rects.RemoveAll(r => (r.x1 - r.x0) * s < MinPanel || (r.y1 - r.y0) * s < MinPanel);
      if (rects.Count == 0) return new List<Brick>();
      var areas = rects.Select(r => (r.x1 - r.x0) * (r.y1 - r.y0)).OrderBy(a => a).ToList();
      double small = areas[areas.Count / 3], large = areas[Math.Min(areas.Count - 1, 2 * areas.Count / 3)];
      // Panels all about one size are all brick; otherwise the smallest third is glass and the largest concrete.
      bool varied = areas[areas.Count - 1] > areas[0] * 1.15;
      var bricks = new List<Brick>();
      foreach (var r in rects)
      {
        double a = (r.x1 - r.x0) * (r.y1 - r.y0);
        var m = !varied ? BrickMaterial.Brick : a < small ? BrickMaterial.Glass : a >= large ? BrickMaterial.Concrete : BrickMaterial.Brick;
        double x0 = ox + (r.x0 - minX) * s, x1 = ox + (r.x1 - minX) * s, y1 = top - (maxY - r.y1) * s, y0 = top - (maxY - r.y0) * s;
        // Leave a hair of joint so neighbours do not touch, less on a panel too thin to spare it.
        double inset = Math.Min(0.02, Math.Min(x1 - x0, y1 - y0) / 4);
        bricks.Add(new Brick(x0 + inset, y0 + inset, x1 - inset, y1 - inset, m));
      }
      return bricks;
    }
  }
}
