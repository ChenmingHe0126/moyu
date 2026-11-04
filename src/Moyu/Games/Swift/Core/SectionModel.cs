using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Swift.Core
{
  /// <summary>A cross wall as the boss sees it, in view units (x across the screen, y up from the floor).</summary>
  public sealed class CrossWall
  {
    public double X0, X1, Lo, Hi;
    public double Middle => (X0 + X1) / 2;
    public double Gap => Hi - Lo;
    public double Centre => (Lo + Hi) / 2;
  }

  /// <summary>A bay between two cross walls (or a wall and the edge of the drawing), with its rooflight if it has one.</summary>
  public sealed class Bay
  {
    public double X0, X1;
    public string Name;
    public bool HasRooflight;
    public double LightX0, LightX1;
    public double Mid => (X0 + X1) / 2;
    public double Width => X1 - X0;
  }

  /// <summary>A smoothed airflow path with the air speed at each point (m/s).</summary>
  public sealed class Streamline
  {
    public readonly List<V2> Points = new List<V2>();
    public readonly List<double> Speeds = new List<double>();
    public bool Stack;
  }

  /// <summary>A noon sun ray through a rooflight: it ends on the floor, or on the cross wall that stops it.</summary>
  public sealed class SunRay
  {
    public V2 From, To;
    /// <summary>The rooflight edge it passes.</summary>
    public double Edge;
    public bool Summer, OnWall;
  }

  /// <summary>What the game hands the section: the walls on screen, the flight so far and the wind, in view units.</summary>
  public sealed class SectionInput
  {
    public double Left = -8, Right = 36;
    public List<CrossWall> Walls = new List<CrossWall>();
    /// <summary>The swift's path so far, oldest first.</summary>
    public List<V2> Path = new List<V2>();
    /// <summary>Where the swift is: the measuring point P1.</summary>
    public V2 Probe = new V2(SwiftGame.BirdX, SwiftGame.Height / 2);
    /// <summary>Offset of the back wall's window rhythm, as the game draws it.</summary>
    public double WindowPhase;
    public double Metres = 0.75;
    /// <summary>Reference wind speed, m/s.</summary>
    public double Wind = 4;
  }

  /// <summary>Figures for the boss-key climate section.</summary>
  public sealed class ClimateMetrics
  {
    public double MeanDf, Sda, MinDf, CrossAch, StackAch, MeanSpeed, ProbeSpeed, Wind, Volume, OpeningArea, RooflightArea;
    public int Bays, Rooflights;
  }

  /// <summary>
  /// The boss-key section is the game's own section, drawn as an architect would: the floor and roof slabs and the
  /// cross walls are cut where the game has them, each opening where the game has it, and the back wall's windows
  /// are seen beyond. It reads as a long gallery: each bay wide enough gets a rooflight, which brings noon sun at
  /// both solstices (a generic 40° N site) and a daylight factor along the workplane; air crosses the gallery through the
  /// openings — the swift's own flight is the main streamline — and rises out through the rooflights by stack
  /// effect, with the standard orifice-flow estimates. Coordinates are the game's view units,
  /// <see cref="SectionInput.Metres"/> metres each.
  /// </summary>
  public sealed class SectionModel
  {
    public const double Height = SwiftGame.Height, FloorSlab = 0.6, RoofSlab = 0.55, Kerb = 0.45, Latitude = 40.0,
      SummerAltitude = 90 - Latitude + 23.44, WinterAltitude = 90 - Latitude - 23.44, WorkplaneMetres = 0.85, DepthMetres = 6.0,
      OpeningWidthMetres = 1.2, MinBayForLight = 4.0, LightWidth = 2.4, VentShare = 0.15;
    /// <summary>The metres a view unit may stand for: the figures, workplane and levels stay those of a house at any Size.</summary>
    public const double MinMetres = 0.1, MaxMetres = 5;
    const double Cd = 0.61, DeltaCp = 0.6, DeltaT = 2.5, Tin = 295, G = 9.81;
    static readonly string[] Names = { "GALLERY", "READING ROOM", "STUDIO", "WORKSHOP", "LOUNGE", "ARCHIVE" };

    public readonly SectionInput Input;
    public readonly List<V2[]> Cut = new List<V2[]>();
    public readonly List<Bay> Bays = new List<Bay>();
    public readonly List<Streamline> Streamlines = new List<Streamline>();
    public readonly List<SunRay> Rays = new List<SunRay>();
    public readonly List<(double x, double y, double df)> Daylight = new List<(double, double, double)>();
    public readonly ClimateMetrics Metrics = new ClimateMetrics();
    double _crossQ, _stackQ;

    public double Metres => Input.Metres;
    public double Workplane => WorkplaneMetres / Metres;
    public double RoofTop => Height + RoofSlab;
    public IReadOnlyList<CrossWall> Walls => Input.Walls;

    public SectionModel(SectionInput input)
    {
      Input = input;
      input.Walls = input.Walls.Where(w => w.X1 > input.Left && w.X0 < input.Right).OrderBy(w => w.X0).ToList();
      BuildBays();
      BuildCut();
      BuildDaylight();
      BuildSun();
      Measure();
      BuildAir();
    }

    // ---------------------------------------------------------------- bays and cut

    void BuildBays()
    {
      double x = Input.Left;
      int k = 0;
      foreach (var w in Walls.Concat(new[] { new CrossWall { X0 = Input.Right, X1 = Input.Right } }))
      {
        var b = new Bay { X0 = x, X1 = Math.Max(x, w.X0), Name = Names[k % Names.Length] + " " + (k + 1) };
        x = w.X1;
        // A wall straddling the edge of the drawing leaves no bay on its far side.
        if (b.Width < 1e-6) continue;
        // A rooflight over every bay wide enough, centred, clear of the walls.
        if (b.Width >= MinBayForLight)
        {
          double lw = Math.Min(LightWidth, b.Width - 1.6);
          b.HasRooflight = true;
          b.LightX0 = b.Mid - lw / 2;
          b.LightX1 = b.Mid + lw / 2;
        }
        Bays.Add(b);
        k++;
      }
    }

    void Rect(double x0, double y0, double x1, double y1)
    {
      if (x1 - x0 < 1e-6 || y1 - y0 < 1e-6) return;
      Cut.Add(new[] { new V2(x0, y0), new V2(x1, y0), new V2(x1, y1), new V2(x0, y1) });
    }

    void BuildCut()
    {
      Rect(Input.Left, -FloorSlab, Input.Right, 0);
      // The roof slab, broken by the rooflights, each with an upstand kerb either side.
      double x = Input.Left;
      foreach (var b in Bays.Where(b => b.HasRooflight))
      {
        Rect(x, Height, b.LightX0, RoofTop);
        Rect(b.LightX0 - 0.18, RoofTop, b.LightX0, RoofTop + Kerb);
        Rect(b.LightX1, RoofTop, b.LightX1 + 0.18, RoofTop + Kerb);
        x = b.LightX1;
      }
      Rect(x, Height, Input.Right, RoofTop);
      foreach (var w in Walls)
      {
        Rect(Math.Max(Input.Left, w.X0), 0, Math.Min(Input.Right, w.X1), w.Lo);
        Rect(Math.Max(Input.Left, w.X0), w.Hi, Math.Min(Input.Right, w.X1), Height);
      }
    }

    // ---------------------------------------------------------------- daylight and sun

    /// <summary>Daylight factor along the workplane: a little side light from the back windows, the rooflight over each bay.</summary>
    void BuildDaylight()
    {
      double y = Workplane;
      foreach (var b in Bays)
        for (double x = b.X0 + 0.3; x <= b.X1 - 0.3 + 1e-9; x += 0.4)
        {
          double df = 0.7;
          if (b.HasRooflight)
          {
            double drop = Height - y, w = b.LightX1 - b.LightX0;
            df += 24 * w / drop / (1 + Math.Pow((x - b.Mid) / (0.25 * drop), 2));
          }
          Daylight.Add((x, y, df));
        }
    }

    /// <summary>The rooflight the sun study is drawn through: the one nearest the middle of the screen.</summary>
    public Bay SunBay => Bays.Where(b => b.HasRooflight).OrderBy(b => Math.Abs(b.Mid - 14)).FirstOrDefault();

    /// <summary>
    /// Sun from the south (left) at both solstices through the edges of one rooflight (the middle one, so the sheet
    /// stays readable), stopped by the first solid wall it meets: its face, or the sill inside its thickness.
    /// </summary>
    void BuildSun()
    {
      var b = SunBay;
      if (b == null) return;
      foreach (bool summer in new[] { true, false })
      {
        double tan = Math.Tan((summer ? SummerAltitude : WinterAltitude) * Math.PI / 180);
        foreach (double edge in new[] { b.LightX0 + 0.1, b.LightX1 - 0.1 })
          {
            const double rise = 2.2;
            var from = new V2(edge - rise / tan, Height + rise);
            var to = new V2(edge + Height / tan, 0);
            bool onWall = false;
            foreach (var w in Walls.Where(w => w.X0 > edge))
            {
              double y = Height - (w.X0 - edge) * tan;
              if (y <= 0) break;
              if (y < w.Lo || y > w.Hi)
              {
                to = new V2(w.X0, y);
                onWall = true;
                break;
              }
              // Through the opening at the face, but dropping below the sill before the far face.
              if (Height - (w.X1 - edge) * tan < w.Lo)
              {
                to = new V2(edge + (Height - w.Lo) / tan, w.Lo);
                onWall = true;
                break;
              }
            }
            Rays.Add(new SunRay { Summer = summer, From = from, To = to, Edge = edge, OnWall = onWall });
          }
      }
    }

    // ---------------------------------------------------------------- air

    /// <summary>Effective area of openings in series: 1/A² = Σ 1/Aᵢ².</summary>
    public static double Series(params double[] areas) => areas.Length == 0 ? 0 : 1 / Math.Sqrt(areas.Sum(a => 1 / (a * a)));

    public double OpeningArea(CrossWall w) => w.Gap * Metres * OpeningWidthMetres;

    void Measure()
    {
      var m = Metrics;
      m.Wind = Input.Wind;
      m.Bays = Bays.Count;
      m.Rooflights = Bays.Count(b => b.HasRooflight);
      m.Volume = (Input.Right - Input.Left) * Metres * Height * Metres * DepthMetres;
      var areas = Walls.Select(OpeningArea).ToArray();
      m.OpeningArea = areas.Sum();
      m.RooflightArea = Bays.Where(b => b.HasRooflight).Sum(b => (b.LightX1 - b.LightX0) * Metres * DepthMetres * 0.5);
      // Cross flow through every opening in turn (the ends of the gallery stand open).
      double crossA = areas.Length > 0 ? Series(areas) : Height * Metres * OpeningWidthMetres;
      _crossQ = Cd * crossA * Input.Wind * Math.Sqrt(DeltaCp);
      // Stack flow: in low through the openings, out through the rooflight vents, over the gallery's height.
      double vents = m.RooflightArea * VentShare;
      _stackQ = vents > 0 && areas.Length > 0 ? Cd * Series(vents, areas.Sum()) * Math.Sqrt(2 * G * Height * Metres * DeltaT / Tin) : 0;
      m.CrossAch = _crossQ * 3600 / m.Volume;
      m.StackAch = _stackQ * 3600 / m.Volume;
      if (Daylight.Count > 0)
      {
        m.MeanDf = Daylight.Average(s => s.df);
        m.MinDf = Daylight.Min(s => s.df);
        m.Sda = Daylight.Count(s => s.df >= 2) / (double)Daylight.Count;
      }
    }

    CrossWall WallAt(double x) => Walls.FirstOrDefault(w => x >= w.X0 - 0.4 && x <= w.X1 + 0.4);

    /// <summary>
    /// The main streamline is the swift's flight so far, carried on through the openings still ahead; two more follow
    /// it, pinching together through each opening. Speeds: the flow through the gallery's cross-section in the bays
    /// (as a jet across the house's depth), through the opening's area at a wall.
    /// </summary>
    void BuildAir()
    {
      double Cap(double v) => Math.Min(1.6, v);
      double bayV = Cap(_crossQ / (Height * Metres * DepthMetres) * 6);
      double Speed(V2 p)
      {
        var w = WallAt(p.X);
        return w != null ? Cap(_crossQ / OpeningArea(w)) : bayV;
      }
      // The flight so far, averaged over windows of about a third of a bay so the flap arcs smooth out.
      var pts = new List<V2>();
      double probeX = Input.Probe.X;
      foreach (var group in Input.Path.Where(p => p.X >= Input.Left && p.X < probeX - 1.2).GroupBy(p => Math.Floor((p.X - Input.Left) / 2.8)).OrderBy(g => g.Key))
        pts.Add(new V2(group.Average(p => p.X), group.Average(p => p.Y)));
      if (pts.Count == 0) pts.Add(new V2(Input.Left, Input.Probe.Y));
      pts.Add(Input.Probe);
      double prevY = Input.Probe.Y, prevX = probeX;
      // Only openings still ahead of the probe, so the line never doubles back when the swift is inside a wall.
      foreach (var w in Walls.Where(w => w.Middle > probeX + 0.5))
      {
        if (w.X0 - prevX > 2) pts.Add(new V2((prevX + w.X0) / 2, (prevY + w.Centre) / 2));
        pts.Add(new V2(w.Middle, w.Centre));
        prevY = w.Centre;
        prevX = w.X1;
      }
      pts.Add(new V2(Input.Right + 1, prevY));
      Streamlines.Add(Smooth(pts.Select(p => (p, Speed(p))).ToList(), false));
      foreach (double side in new[] { -1.0, 1.0 })
      {
        var shifted = pts.Select(p =>
        {
          var wall = WallAt(p.X);
          double off = wall != null ? Math.Max(0.2, wall.Gap / 2 - 0.55) : 1.1;
          double y = Math.Max(0.6, Math.Min(Height - 0.6, p.Y + side * off));
          return (new V2(p.X, y), Speed(p) * 0.9);
        }).ToList();
        Streamlines.Add(Smooth(shifted, false));
      }
      // Stack: warm air rising to each rooflight and out.
      int lights = Math.Max(1, Metrics.Rooflights);
      double ventV = Cap(_stackQ / lights / Math.Max(0.1, Metrics.RooflightArea * VentShare / lights));
      foreach (var b in Bays.Where(b => b.HasRooflight))
      {
        double cx = (b.LightX0 + b.LightX1) / 2;
        Streamlines.Add(Smooth(new List<(V2, double)>
        {
          (new V2(cx - 1.0, Height * 0.45), 0.12),
          (new V2(cx - 0.5, Height * 0.8), 0.3),
          (new V2(cx, Height - 0.2), ventV),
          (new V2(cx + 0.5, RoofTop + Kerb + 0.9), ventV * 0.8),
          (new V2(cx + 1.6, RoofTop + Kerb + 1.4), 0.4),
        }, true));
      }
      var inside = Streamlines.Where(s => !s.Stack).SelectMany(s => s.Points.Zip(s.Speeds, (p, v) => (p, v)))
        .Where(q => q.p.X > Input.Left && q.p.X < Input.Right).ToList();
      Metrics.MeanSpeed = inside.Count > 0 ? inside.Average(q => q.v) : 0;
      var main = Streamlines[0];
      int near = Enumerable.Range(0, main.Points.Count).OrderBy(i => main.Points[i].DistanceTo(Input.Probe)).First();
      Metrics.ProbeSpeed = main.Speeds[near];
    }

    /// <summary>A Catmull-Rom curve through the waypoints, with the speed eased between them.</summary>
    static Streamline Smooth(List<(V2 p, double v)> pts, bool stack)
    {
      var s = new Streamline { Stack = stack };
      const int per = 10;
      for (int i = 0; i + 1 < pts.Count; i++)
      {
        var p0 = pts[Math.Max(0, i - 1)].p;
        var p1 = pts[i].p;
        var p2 = pts[i + 1].p;
        var p3 = pts[Math.Min(pts.Count - 1, i + 2)].p;
        for (int k = 0; k < per; k++)
        {
          double t = k / (double)per, t2 = t * t, t3 = t2 * t;
          double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
          double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
          double e = t * t * (3 - 2 * t);
          s.Points.Add(new V2(x, y));
          s.Speeds.Add(pts[i].v + (pts[i + 1].v - pts[i].v) * e);
        }
      }
      s.Points.Add(pts[pts.Count - 1].p);
      s.Speeds.Add(pts[pts.Count - 1].v);
      return s;
    }

    // ---------------------------------------------------------------- from the game

    /// <summary>
    /// The section of the course as it is on screen: the same walls and openings, the flight so far, the wind from the
    /// course speed. The metre scale is kept within <see cref="MinMetres"/>–<see cref="MaxMetres"/> whatever the Size.
    /// </summary>
    public static SectionInput FromGame(SwiftGame g, double metresPerUnit, double left = -8, double right = 36) => new SectionInput
    {
      Left = left,
      Right = right,
      Walls = g.Walls.Select(w =>
      {
        double c = w.CentreAt(g.Elapsed);
        return new CrossWall { X0 = w.X - g.Distance, X1 = w.Right - g.Distance, Lo = c - w.Gap / 2, Hi = c + w.Gap / 2 };
      }).ToList(),
      Path = g.Trail.Select(p => new V2(p.X - g.Distance, p.Y)).ToList(),
      Probe = new V2(SwiftGame.BirdX, g.Y),
      WindowPhase = g.Distance % SwiftGame.WindowPitch,
      Metres = metresPerUnit > 0 ? Math.Max(MinMetres, Math.Min(MaxMetres, metresPerUnit)) : 0.75,
      Wind = Math.Round(1.5 + g.Speed * 0.5, 1),
    };
  }
}
