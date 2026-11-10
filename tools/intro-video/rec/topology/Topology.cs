// Topology optimisation shots for the intro video: Topo2D (pixels, top view) and Topo3D (voxels, orbit).
//
// Both use Moyu's own code for everything that matters: the domains, the problem builders, the SIMP optimisers and
// the meshers behind Visualize 2D / Visualize 3D. The iterations are solved once (Presolve, on a worker thread) and
// kept in the AppDomain; a shot then only decides which real iteration each video frame shows. Nothing is blended or
// invented between iterations.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Moyu.Core;
using Moyu.ThreeD;
using Moyu.TwoD;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace MoyuRec
{
  /// <summary>key=value arguments with invariant-culture numbers.</summary>
  public sealed class Args
  {
    readonly Dictionary<string, string> _d;
    public Args(Dictionary<string, string> d) { _d = d ?? new Dictionary<string, string>(); }

    public static Args Parse(string s)
    {
      var d = new Dictionary<string, string>();
      foreach (var pair in (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
      {
        int eq = pair.IndexOf('=');
        if (eq > 0) d[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
      }
      return new Args(d);
    }

    public string Str(string key, string fallback) => _d.TryGetValue(key, out var v) ? v : fallback;
    public double Num(string key, double fallback) =>
      _d.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : fallback;
    public int Int(string key, int fallback) => (int)Math.Round(Num(key, fallback));
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
  }

  // ------------------------------------------------------------------------------------------------ the two cases

  /// <summary>A 2D case in the world XY plane, one world unit per pixel.</summary>
  public sealed class Case2D
  {
    public string Key;
    public int Nx, Ny;
    public PixelDomain2D Domain;
    public ProblemBuilder2D.Output Built;
    public SimpSettings Settings;

    /// <summary>
    /// case = tree (fixed foot in the middle of the base, load along the solid top edge), treevoid (same with a
    /// round void in the trunk's way), arch (two feet at the corners, round void), twin (two feet at the third points).
    /// </summary>
    public static Case2D Make(Args a)
    {
      string name = a.Str("case", "treevoid");
      int nx = a.Int("nx", 150), ny = a.Int("ny", 175);
      double vf = a.Num("vf", 0.3), radius = a.Num("r", 2.5), deck = a.Num("deck", 3);
      double foot = a.Num("foot", Math.Round(nx * 0.07)), hole = a.Num("hole", Math.Round(nx * 0.13));
      double holeX = a.Num("holex", 0.5), holeY = a.Num("holey", 0.52);
      var c = new Case2D { Nx = nx, Ny = ny };
      c.Key = $"2d|{name}|{nx}x{ny}|vf{Args.F(vf)}|r{Args.F(radius)}|deck{Args.F(deck)}|foot{Args.F(foot)}|hole{Args.F(hole)}@{Args.F(holeX)},{Args.F(holeY)}";

      Curve Rect(double x0, double y0, double x1, double y1) =>
        new Polyline(new[] { new Point3d(x0, y0, 0), new Point3d(x1, y0, 0), new Point3d(x1, y1, 0), new Point3d(x0, y1, 0), new Point3d(x0, y0, 0) }).ToPolylineCurve();
      Curve Seg(double x0, double x1, double y) => new LineCurve(new Point3d(x0, y, 0), new Point3d(x1, y, 0));

      c.Domain = PixelDomain2D.Create(new List<Curve> { Rect(0, 0, nx, ny) }, Math.Max(nx, ny), Plane.WorldXY, out string error);
      if (c.Domain == null) throw new InvalidOperationException("2D domain: " + error);
      if (c.Domain.Grid.Nx != nx || c.Domain.Grid.Ny != ny) throw new InvalidOperationException($"2D grid is {c.Domain.Grid.Nx}x{c.Domain.Grid.Ny}, expected {nx}x{ny}");

      var loads = new List<Load2D> { new Load2D(new Anchor2D(Seg(0, nx, ny)), new Vector3d(0, -1, 0)) };
      var supports = new List<Support2D>();
      var areas = new List<Area2D>();
      if (deck > 0) areas.Add(new Area2D(Rect(0, ny - deck, nx, ny), ElementKind.Solid));
      bool withHole = name == "treevoid" || name == "arch";
      if (withHole && hole > 0)
        areas.Add(new Area2D(new Circle(new Point3d(nx * holeX, ny * holeY, 0), hole).ToNurbsCurve(), ElementKind.Void));
      switch (name)
      {
        case "tree":
        case "treevoid":
          supports.Add(new Support2D(new Anchor2D(Seg(nx / 2.0 - foot, nx / 2.0 + foot, 0)), true, true));
          break;
        case "arch":
          supports.Add(new Support2D(new Anchor2D(Seg(0, foot, 0)), true, true));
          supports.Add(new Support2D(new Anchor2D(Seg(nx - foot, nx, 0)), true, true));
          break;
        case "twin":
          supports.Add(new Support2D(new Anchor2D(Seg(nx * 0.27 - foot, nx * 0.27 + foot, 0)), true, true));
          supports.Add(new Support2D(new Anchor2D(Seg(nx * 0.73 - foot, nx * 0.73 + foot, 0)), true, true));
          break;
        default: throw new InvalidOperationException("unknown 2D case " + name);
      }
      c.Built = ProblemBuilder2D.Build(c.Domain, loads, supports, areas, Material2D.Default, out error);
      if (c.Built == null) throw new InvalidOperationException("2D problem: " + error);
      // As SIMP Solver 2D builds them: penalty 3, 200 iterations, default tolerances.
      c.Settings = new SimpSettings { VolumeFraction = vf, Penalty = 3.0, FilterRadius = radius, MaxIterations = 200 };
      return c;
    }
  }

  /// <summary>A 3D case: a box at the origin, one world unit per voxel.</summary>
  public sealed class Case3D
  {
    public string Key;
    public int Sx, Sy, Sz;
    public VoxelDomain3D Domain;
    public ProblemBuilder3D.Output Built;
    public SimpSettings Settings;

    /// <summary>case = table (surface load on a solid slab, four pinned feet), cantilever (fixed end face, line load).</summary>
    public static Case3D Make(Args a)
    {
      string name = a.Str("case", "table");
      int sx = a.Int("sx", 40), sy = a.Int("sy", 40), sz = a.Int("sz", 44);
      double vf = a.Num("vf", 0.15), radius = a.Num("r", 1.5), slab = a.Num("slab", 3);
      double pad = a.Num("pad", 0), inset = a.Num("inset", 0);
      var c = new Case3D { Sx = sx, Sy = sy, Sz = sz };
      c.Key = $"3d|{name}|{sx}x{sy}x{sz}|vf{Args.F(vf)}|r{Args.F(radius)}|slab{Args.F(slab)}|pad{Args.F(pad)}|inset{Args.F(inset)}";

      Mesh BoxMesh(double x0, double y0, double z0, double x1, double y1, double z1) =>
        Mesh.CreateFromBox(new BoundingBox(new Point3d(x0, y0, z0), new Point3d(x1, y1, z1)), 1, 1, 1);

      c.Domain = VoxelDomain3D.Create(new List<Mesh> { BoxMesh(0, 0, 0, sx, sy, sz) }, Math.Max(sx, Math.Max(sy, sz)), null, out string error);
      if (c.Domain == null) throw new InvalidOperationException("3D domain: " + error);
      var g = c.Domain.Grid;
      if (g.Nx != sx || g.Ny != sy || g.Nz != sz) throw new InvalidOperationException($"3D grid is {g.Nx}x{g.Ny}x{g.Nz}, expected {sx}x{sy}x{sz} (use multiples of 4)");

      var loads = new List<Load3D>();
      var supports = new List<Support3D>();
      var areas = new List<Area3D>();
      switch (name)
      {
        case "table":
          loads.Add(new Load3D(new Anchor3D(Mesh.CreateFromPlane(new Plane(new Point3d(0, 0, sz), Vector3d.ZAxis), new Interval(0, sx), new Interval(0, sy), 1, 1)), new Vector3d(0, 0, -1)));
          if (slab > 0) areas.Add(new Area3D(BoxMesh(0, 0, sz - slab, sx, sy, sz), ElementKind.Solid));
          foreach (var (fx, fy) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
          {
            double px = fx == 0 ? inset : sx - inset, py = fy == 0 ? inset : sy - inset;
            if (pad > 0)
            {
              double x0 = fx == 0 ? px : px - pad, y0 = fy == 0 ? py : py - pad;
              supports.Add(new Support3D(new Anchor3D(Mesh.CreateFromPlane(new Plane(new Point3d(x0, y0, 0), Vector3d.ZAxis), new Interval(0, pad), new Interval(0, pad), 1, 1)), true, true, true));
            }
            else supports.Add(new Support3D(new Anchor3D(new Point3d(px, py, 0)), true, true, true));
          }
          break;
        case "cantilever":
          supports.Add(new Support3D(new Anchor3D(Mesh.CreateFromPlane(new Plane(Point3d.Origin, Vector3d.YAxis, Vector3d.ZAxis), new Interval(0, sy), new Interval(0, sz), 1, 1)), true, true, true));
          loads.Add(new Load3D(new Anchor3D(new LineCurve(new Point3d(sx, 0, 0), new Point3d(sx, sy, 0))), new Vector3d(0, 0, -1)));
          break;
        default: throw new InvalidOperationException("unknown 3D case " + name);
      }
      c.Built = ProblemBuilder3D.Build(c.Domain, loads, supports, areas, Material3D.Default, out error);
      if (c.Built == null) throw new InvalidOperationException("3D problem: " + error);
      // As SIMP Solver 3D builds them: penalty 3, 150 iterations, objective tolerance 5e-4.
      c.Settings = new SimpSettings { VolumeFraction = vf, Penalty = 3.0, FilterRadius = radius, MaxIterations = 150, ObjectiveTolerance = 5e-4 };
      return c;
    }
  }

  // ------------------------------------------------------------------------------------------------ solving

  /// <summary>
  /// Runs an optimiser to the end and keeps the density field of every iteration. The store uses framework types
  /// only (it lives in the AppDomain), so a run survives rebuilding and reloading this DLL.
  /// Called from the loader by reflection: Presolve.Start("2d" | "3d", args), Presolve.Status(key or null).
  /// </summary>
  public static class Presolve
  {
    const string Prefix = "moyurec:topo:";

    public static Dictionary<string, object> Get(string key) => AppDomain.CurrentDomain.GetData(Prefix + key) as Dictionary<string, object>;

    static Dictionary<string, object> Fresh(string key)
    {
      var d = new Dictionary<string, object>
      {
        ["snaps"] = new List<float[]>(), ["comp"] = new List<double>(), ["ms"] = new List<double>(), ["change"] = new List<double>(),
        ["state"] = "running", ["converged"] = false,
      };
      AppDomain.CurrentDomain.SetData(Prefix + key, d);
      AppDomain.CurrentDomain.SetData(Prefix + "last", key);
      return d;
    }

    public static string Start(string kind, string args)
    {
      var a = Args.Parse(args);
      SimpOptimizer opt;
      string key;
      if (kind == "2d")
      {
        var c = Case2D.Make(a);
        key = c.Key;
        opt = new SimpOptimizer2D(c.Built.Problem, c.Settings);
      }
      else
      {
        var c = Case3D.Make(a);
        key = c.Key;
        opt = new SimpOptimizer3D(c.Built.Problem, c.Settings);
      }
      var have = Get(key);
      if (have != null && a.Int("force", 0) == 0 && !((string)have["state"]).StartsWith("error")) return "have " + Describe(key, have);
      var d = Fresh(key);
      Task.Run(() => Run(opt, d));
      return "started " + key;
    }

    public static void Run(SimpOptimizer opt, Dictionary<string, object> d)
    {
      try
      {
        Add(d, opt, 0);
        var sw = new Stopwatch();
        while (!opt.IsFinished)
        {
          sw.Restart();
          opt.Step();
          sw.Stop();
          Add(d, opt, sw.Elapsed.TotalMilliseconds);
        }
        lock (d) { d["converged"] = opt.Converged; d["state"] = "done"; }
      }
      catch (Exception ex) { lock (d) d["state"] = "error: " + ex.Message; }
    }

    static void Add(Dictionary<string, object> d, SimpOptimizer opt, double ms)
    {
      var snap = new float[opt.XPhys.Length];
      for (int e = 0; e < snap.Length; e++) snap[e] = (float)opt.XPhys[e];
      lock (d)
      {
        ((List<float[]>)d["snaps"]).Add(snap);
        ((List<double>)d["comp"]).Add(opt.Compliance);
        ((List<double>)d["change"]).Add(opt.Change);
        ((List<double>)d["ms"]).Add(ms);
      }
    }

    /// <summary>The run of a case, solving it now (blocking) when Presolve.Start was not called first.</summary>
    public static Dictionary<string, object> Ensure(string key, Func<SimpOptimizer> make)
    {
      var d = Get(key);
      if (d != null && (string)d["state"] == "running") throw new InvalidOperationException("still solving: " + Describe(key, d));
      if (d == null || (string)d["state"] != "done")
      {
        d = Fresh(key);
        Run(make(), d);
      }
      if ((string)d["state"] != "done") throw new InvalidOperationException("solve failed: " + d["state"]);
      return d;
    }

    public static string Status(string key)
    {
      key = key ?? AppDomain.CurrentDomain.GetData(Prefix + "last") as string;
      var d = key == null ? null : Get(key);
      return d == null ? "nothing solved" : Describe(key, d);
    }

    public static string Describe(string key, Dictionary<string, object> d)
    {
      lock (d)
      {
        var ms = (List<double>)d["ms"];
        var comp = (List<double>)d["comp"];
        var change = (List<double>)d["change"];
        int n = ms.Count - 1;
        double total = ms.Sum(), mean = n > 0 ? total / n : 0;
        var sorted = ms.Skip(1).OrderBy(v => v).ToList();
        double median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0;
        return $"{key}: {d["state"]}, {n} iterations, converged {d["converged"]}, compliance {(n > 0 ? comp[n] : double.NaN):G5}, change {(n > 0 ? change[n] : double.NaN):G3}, " +
               $"solve {total / 1000:F1} s, {mean:F0} ms/iteration (median {median:F0})";
      }
    }
  }

  // ------------------------------------------------------------------------------------------------ drawing

  /// <summary>The conduit both shots draw through: a soft light backdrop, then whatever the shot hands it.</summary>
  sealed class Stage : DisplayConduit
  {
    readonly Guid _viewport;
    DisplayBitmap _backdrop;
    Size _backdropSize;
    public Color Top = Color.FromArgb(249, 250, 252), Bottom = Color.FromArgb(228, 232, 238);
    public BoundingBox Bounds = BoundingBox.Empty;
    public Action<DrawEventArgs> Scene, Overlay;
    public string Error;

    public Stage(Guid viewport) { _viewport = viewport; }

    bool Mine(DrawEventArgs e) => e.Viewport.Id == _viewport;

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      if (e.Viewport.Id == _viewport && Bounds.IsValid) e.IncludeBoundingBox(Bounds);
    }

    protected override void PreDrawObjects(DrawEventArgs e)
    {
      if (!Mine(e)) return;
      try
      {
        var size = e.Display.FrameSize;
        if (_backdrop == null || _backdropSize != size)
        {
          _backdrop?.Dispose();
          using (var bmp = Backdrop(size)) _backdrop = new DisplayBitmap(bmp);
          _backdropSize = size;
        }
        e.Display.EnableDepthWriting(false);
        e.Display.DrawBitmap(_backdrop, 0, 0);
        e.Display.EnableDepthWriting(true);
      }
      catch (Exception ex) { Error = "backdrop: " + ex.Message; }
    }

    Bitmap Backdrop(Size size)
    {
      int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
      var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
      using (var g = Graphics.FromImage(bmp))
      {
        using (var brush = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), Top, Bottom, 90f))
          g.FillRectangle(brush, 0, 0, w, h);
        // A wide soft light behind the subject, so the frame is not a flat ramp.
        using (var path = new GraphicsPath())
        {
          path.AddEllipse(-w * 0.35f, -h * 0.05f, w * 1.7f, h * 0.95f);
          using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(120, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
            g.FillPath(glow, path);
        }
      }
      return bmp;
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
      if (!Mine(e)) return;
      try { Scene?.Invoke(e); }
      catch (Exception ex)
      {
        e.Display.EnableDepthTesting(true);
        e.Display.EnableDepthWriting(true);
        Error = "scene: " + ex.Message;
      }
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
      if (!Mine(e)) return;
      try { Overlay?.Invoke(e); }
      catch (Exception ex) { Error = "overlay: " + ex.Message; }
    }

    public void Release()
    {
      Enabled = false;
      _backdrop?.Dispose();
      _backdrop = null;
    }
  }

  /// <summary>Loads and supports as Moyu's Glyphs draw them (same shapes and colours), sized for a 1080 x 1920 frame.</summary>
  static class Marks
  {
    /// <summary>Screen sizes are multiplied by this: the plugin's pixel sizes are meant for a desktop viewport.</summary>
    public static double K(DisplayPipeline d) => d.FrameSize.Width / 480.0;

    /// <summary>Capture pixels per world unit at a point, whichever size the pipeline's viewport reports.</summary>
    public static double PixelsPerUnit(DrawEventArgs e, Point3d at)
    {
      if (!e.Viewport.GetWorldToScreenScale(at, out double ppu) || ppu <= 0) return 1;
      return ppu * e.Display.FrameSize.Width / Math.Max(1, e.Viewport.Size.Width);
    }

    public static void Arrow(DrawEventArgs e, Point3d tip, Vector3d force, double k, double alpha = 1)
    {
      var dir = force;
      if (!dir.Unitize()) return;
      var color = Fade(Glyphs.LoadColor, alpha);
      double ppu = PixelsPerUnit(e, tip);
      double len = 45.0 * k / ppu, head = 14.0 * k / ppu;
      e.Display.DrawLine(tip - dir * len, tip - dir * (head * 0.6), color, Math.Max(1, (int)Math.Round(2 * k * 0.8)));
      e.Display.DrawArrowHead(tip, dir, color, 14.0 * k, 0.0);
    }

    public static void Support(DrawEventArgs e, Point3d at, bool pinned, double k, double alpha = 1) =>
      e.Display.DrawPoint(at, pinned ? PointStyle.Triangle : PointStyle.Circle, (float)(7 * k * PointScale), Fade(Glyphs.SupportColor, alpha));

    /// <summary>Calibration of DrawPoint's radius against the frame (set from a test capture).</summary>
    public static double PointScale = 1.0;

    public static Color Fade(Color c, double a) => Color.FromArgb((int)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c);

    public static IEnumerable<Point3d> Thin(IList<Point3d> pts, int max)
    {
      if (pts.Count <= max) return pts;
      double step = (pts.Count - 1) / (double)(max - 1);
      return Enumerable.Range(0, max).Select(i => pts[(int)Math.Round(i * step)]);
    }

    public static double Smooth(double u)
    {
      u = Math.Max(0, Math.Min(1, u));
      return u * u * (3 - 2 * u);
    }
  }

  // ------------------------------------------------------------------------------------------------ 2D shot

  /// <summary>
  /// 2D pixels, seen from above in a parallel view. 0 - start: the grey design domain with loads and supports;
  /// start - end: the real iterations at a constant rate, the last one landing on `end`; then the result is held.
  /// args (besides the case's): start=1.2, end=6, fill=0.8333 (domain width / frame width), bottom=0.715 (frame height
  /// fraction of the base), contours=always|end|never, arrows=13, feet=9.
  /// </summary>
  public sealed class Topo2D : IShot
  {
    public int Frames => 60 * 9;

    Case2D _c;
    List<float[]> _snaps;
    int _n, _f0, _f1, _shown = -1;
    string _contourMode;
    Stage _stage;
    Result2D _result;
    Mesh _mesh;
    List<Curve> _contours = new List<Curve>();
    List<Point3d> _loadTips, _feet;
    Vector3d _force;
    bool _noted;

    public void Setup(Rec rec)
    {
      var a = new Args(rec.Args);
      _c = Case2D.Make(a);
      var run = Presolve.Ensure(_c.Key, () => new SimpOptimizer2D(_c.Built.Problem, _c.Settings));
      _snaps = (List<float[]>)run["snaps"];
      _n = _snaps.Count - 1;
      _f0 = (int)Math.Round(a.Num("start", 1.2) * rec.Fps);
      _f1 = (int)Math.Round(a.Num("end", 6.0) * rec.Fps);
      _contourMode = a.Str("contours", "always");
      rec.Note(Presolve.Describe(_c.Key, run));
      rec.Note($"2D grid {_c.Nx}x{_c.Ny}; iterations 1..{_n} shown from frame {_f0} ({_f0 / (double)rec.Fps:F3} s) to frame {_f1} ({_f1 / (double)rec.Fps:F3} s) = {_n / ((_f1 - _f0) / (double)rec.Fps):F2} iterations per video second");

      _result = new Result2D { Domain = _c.Domain, Kinds = _c.Built.Problem.Kinds, Density = new double[_c.Domain.Grid.ElementCount], Loads = _c.Built.Loads, Supports = _c.Built.Supports };
      var load = _c.Built.Loads[0];
      _force = load.Force;
      _loadTips = Marks.Thin(load.Nodes.OrderBy(p => p.X).ToList(), a.Int("arrows", 13)).ToList();
      _feet = new List<Point3d>();
      foreach (var s in _c.Built.Supports)
        _feet.AddRange(Marks.Thin(s.Nodes.OrderBy(p => p.X).ToList(), a.Int("feet", 7)));

      // Parallel camera straight down, an exact number of video pixels per cell.
      var vp = rec.View.ActiveViewport;
      vp.ConstructionGridVisible = false;
      vp.ConstructionAxesVisible = false;
      vp.WorldAxesVisible = false;
      double fill = a.Num("fill", 150.0 / 180.0), bottom = a.Num("bottom", 0.715);
      double ppc = Math.Round(1080 * fill / _c.Nx * 2) / 2;          // video pixels per cell at full size
      double wf = 1080 / ppc, hf = wf * 16 / 9;
      double cx = _c.Nx / 2.0, cy = Math.Round((bottom - 0.5) * hf * ppc) / ppc;
      var vi = new ViewportInfo(vp);
      vi.ChangeToParallelProjection(true);
      vi.SetCameraLocation(new Point3d(cx, cy, 400));
      vi.SetCameraDirection(-Vector3d.ZAxis);
      vi.SetCameraUp(Vector3d.YAxis);
      vi.SetFrustum(-wf / 2, wf / 2, -hf / 2, hf / 2, 10, 800);
      vi.TargetPoint = new Point3d(cx, cy, 0);
      bool ok = vp.SetViewProjection(vi, true);
      rec.Note($"2D camera: parallel, frame {wf:F3} x {hf:F3} units, {ppc} px per cell, centre ({cx:F3}, {cy:F3}); SetViewProjection {ok}; domain spans {100 * _c.Nx * ppc / 1080:F1}% of the width, top edge at {100 * (0.5 - (_c.Ny - cy) / hf):F1}%, base at {100 * (0.5 + cy / hf):F1}% of the height");

      _stage = new Stage(rec.View.ActiveViewportID) { Bounds = new BoundingBox(new Point3d(-50, -50, -1), new Point3d(_c.Nx + 50, _c.Ny + 50, 1)), Scene = Draw };
      _stage.Enabled = true;
      Show(0);
    }

    int IterationAt(int frame) =>
      frame < _f0 ? 0 : Math.Min(_n, 1 + (int)Math.Floor((frame - _f0) * (double)(_n - 1) / Math.Max(1, _f1 - _f0) + 1e-9));

    void Show(int k)
    {
      if (k == _shown) return;
      _shown = k;
      var snap = _snaps[k];
      for (int e = 0; e < snap.Length; e++) _result.Density[e] = snap[e];
      _result.Iteration = k;
      _mesh?.Dispose();
      // Visualize 2D, Mode 0 (density pixels), its default colours and threshold.
      _mesh = ResultMesher.BuildMesh(_result, Moyu.TwoD.DisplayMode.Pixels, 0.5, Color.FromArgb(25, 25, 25), Color.FromArgb(240, 240, 240));
      foreach (var c in _contours) c.Dispose();
      _contours = _contourMode == "never" || (_contourMode == "end" && k < _n) ? new List<Curve>() : ResultMesher.BuildContours(_result, 0.5);
    }

    public void Frame(Rec rec, int i)
    {
      Show(IterationAt(i));
      if (_stage.Error != null) throw new InvalidOperationException(_stage.Error);
    }

    void Draw(DrawEventArgs e)
    {
      var d = e.Display;
      double k = Marks.K(d);
      if (!_noted)
      {
        _noted = true;
        _note = $"pipeline frame {d.FrameSize.Width}x{d.FrameSize.Height}, viewport {e.Viewport.Size.Width}x{e.Viewport.Size.Height}, dpi scale {d.DpiScale}";
      }
      d.EnableDepthTesting(false);
      if (_mesh != null && _mesh.Faces.Count > 0) d.DrawMeshFalseColors(_mesh);
      foreach (var pl in _c.Domain.Outlines) d.DrawPolyline(pl, Glyphs.DomainColor, Math.Max(1, (int)Math.Round(k)));
      foreach (var c in _contours) d.DrawCurve(c, Color.FromArgb(0, 122, 204), Math.Max(1, (int)Math.Round(1.5 * k)));
      foreach (var p in _loadTips) Marks.Arrow(e, p, _force, k);
      foreach (var p in _feet) Marks.Support(e, p, true, k);
      d.EnableDepthTesting(true);
    }

    string _note;

    public void Teardown(Rec rec)
    {
      if (_note != null) rec.Note(_note);
      _stage?.Release();
      _mesh?.Dispose();
      var vp = rec.View?.ActiveViewport;
      if (vp != null && vp.IsParallelProjection) vp.ChangeToPerspectiveProjection(true, 50);
    }
  }

  // ------------------------------------------------------------------------------------------------ 3D shot

  /// <summary>
  /// 3D voxels with a slow orbit. 0 - start: the full block with loads and supports; start - end: the real iterations
  /// at a constant rate (Visualize 3D voxel mode; its Threshold input rises from below the starting density to 0.5
  /// over the first `ramp` iterations, so the block is carved instead of vanishing); at `smooth` the display switches
  /// to Visualize 3D's smooth iso-surface. The camera eases through `sweep` degrees over the whole clip.
  /// args (besides the case's): start=0.6, end=6.5, smooth=7.5, ramp=12, az=12, sweep=68, el=20, lens=50,
  /// fill=0.86 (box width / frame width), edges=1 (voxel edges), shadow=1.
  /// </summary>
  public sealed class Topo3D : IShot
  {
    public int Frames => 60 * 11;

    Case3D _c;
    List<float[]> _snaps;
    int _n, _f0, _f1, _fs, _ramp, _shown = -2;
    bool _smoothShown, _edges, _shadow;
    Stage _stage;
    Result3D _result;
    Mesh _mesh;
    DisplayMaterial _material, _shade;
    List<Point3d> _loadTips, _feet;
    Vector3d _force;
    Box _box;
    Point3d[] _corners;
    Mesh[] _shadowRings;
    double _az0, _sweep, _el, _lens, _fill, _dist = 200, _sr, _su;
    double _tau0;
    string _note;

    public void Setup(Rec rec)
    {
      var a = new Args(rec.Args);
      _c = Case3D.Make(a);
      var run = Presolve.Ensure(_c.Key, () => new SimpOptimizer3D(_c.Built.Problem, _c.Settings));
      _snaps = (List<float[]>)run["snaps"];
      _n = _snaps.Count - 1;
      _f0 = (int)Math.Round(a.Num("start", 0.6) * rec.Fps);
      _f1 = (int)Math.Round(a.Num("end", 6.5) * rec.Fps);
      _fs = (int)Math.Round(a.Num("smooth", 7.5) * rec.Fps);
      _ramp = a.Int("ramp", 12);
      _tau0 = a.Num("tau0", 0.3 * _c.Settings.VolumeFraction);
      _edges = a.Int("edges", 1) != 0;
      _shadow = a.Int("shadow", 1) != 0;
      _az0 = a.Num("az", 12);
      _sweep = a.Num("sweep", 68);
      _el = a.Num("el", 20);
      _lens = a.Num("lens", 50);
      _fill = a.Num("fill", 0.86);
      rec.Note(Presolve.Describe(_c.Key, run));
      rec.Note($"3D grid {_c.Sx}x{_c.Sy}x{_c.Sz}; iterations 1..{_n} shown from frame {_f0} ({_f0 / (double)rec.Fps:F3} s) to frame {_f1} ({_f1 / (double)rec.Fps:F3} s) = {_n / ((_f1 - _f0) / (double)rec.Fps):F2} iterations per video second; smooth surface from frame {_fs} ({_fs / (double)rec.Fps:F3} s)");

      _result = new Result3D { Domain = _c.Domain, Kinds = _c.Built.Problem.Kinds, Density = new double[_c.Domain.Grid.ElementCount], Loads = _c.Built.Loads, Supports = _c.Built.Supports };
      // Visualize 3D's default colour.
      _material = new DisplayMaterial(Color.FromArgb(70, 110, 190));
      _shade = new DisplayMaterial(Color.Black, 0.955) { Emission = Color.FromArgb(40, 50, 70), Specular = Color.Black, Shine = 0 };
      _box = new Box(Plane.WorldXY, new Interval(0, _c.Sx), new Interval(0, _c.Sy), new Interval(0, _c.Sz));
      _corners = _box.GetCorners();

      _loadTips = new List<Point3d>();
      _force = _c.Built.Loads[0].Force;
      var nodes = _c.Built.Loads[0].Nodes;
      if (nodes.Length > 60)
      {
        // A surface load: an even 4 x 4 pick of the loaded nodes instead of every one.
        var bb = new BoundingBox(nodes);
        int m = a.Int("arrows", 4);
        for (int j = 0; j < m; j++)
          for (int i = 0; i < m; i++)
          {
            var want = new Point3d(bb.Min.X + (i + 0.5) / m * (bb.Max.X - bb.Min.X), bb.Min.Y + (j + 0.5) / m * (bb.Max.Y - bb.Min.Y), bb.Max.Z);
            _loadTips.Add(nodes.OrderBy(p => p.DistanceToSquared(want)).First());
          }
      }
      else _loadTips.AddRange(Marks.Thin(nodes, 9));
      _feet = new List<Point3d>();
      foreach (var s in _c.Built.Supports) _feet.AddRange(Marks.Thin(s.Nodes, s.Nodes.Length > 40 ? 36 : 4));

      // A soft contact shadow on the ground: stacked see-through rounded plates, each a little larger.
      var rings = new List<Mesh>();
      for (int r = 0; r < 9; r++)
      {
        double grow = -2 + r * 1.6, round = 3 + r * 1.6;
        rings.Add(RoundedPlate(-grow, -grow, _c.Sx + grow, _c.Sy + grow, round, -0.02 - 0.002 * r));
      }
      _shadowRings = rings.ToArray();

      var vp = rec.View.ActiveViewport;
      if (vp.IsParallelProjection) vp.ChangeToPerspectiveProjection(true, _lens);
      vp.ConstructionGridVisible = false;
      vp.ConstructionAxesVisible = false;
      vp.WorldAxesVisible = false;
      vp.Camera35mmLensLength = _lens;

      _stage = new Stage(rec.View.ActiveViewportID)
      {
        Bounds = new BoundingBox(new Point3d(-20, -20, -2), new Point3d(_c.Sx + 20, _c.Sy + 20, _c.Sz + 20)), Scene = Draw,
      };
      _stage.Enabled = true;
      Show(0, false);
      Aim(vp, 0, rec);
      rec.Note(_aimNote);
    }

    static Mesh RoundedPlate(double x0, double y0, double x1, double y1, double radius, double z)
    {
      var mesh = new Mesh();
      double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
      mesh.Vertices.Add(cx, cy, z);
      const int perCorner = 8;
      var centres = new[] { (x1 - radius, y1 - radius, 0.0), (x0 + radius, y1 - radius, 90.0), (x0 + radius, y0 + radius, 180.0), (x1 - radius, y0 + radius, 270.0) };
      foreach (var (px, py, a0) in centres)
        for (int s = 0; s <= perCorner; s++)
        {
          double ang = (a0 + 90.0 * s / perCorner) * Math.PI / 180;
          mesh.Vertices.Add(px + radius * Math.Cos(ang), py + radius * Math.Sin(ang), z);
        }
      int n = mesh.Vertices.Count - 1;
      for (int i = 0; i < n; i++) mesh.Faces.AddFace(0, 1 + i, 1 + (i + 1) % n);
      mesh.Normals.ComputeNormals();
      return mesh;
    }

    int IterationAt(int frame) =>
      frame < _f0 ? 0 : Math.Min(_n, 1 + (int)Math.Floor((frame - _f0) * (double)(_n - 1) / Math.Max(1, _f1 - _f0) + 1e-9));

    /// <summary>The Threshold input over the first iterations: from below the starting density up to the default 0.5.</summary>
    double Threshold(int k) => k >= _ramp ? 0.5 : _tau0 + (0.5 - _tau0) * Marks.Smooth(k / (double)_ramp);

    void Show(int k, bool smooth)
    {
      if (k == _shown && smooth == _smoothShown) return;
      _shown = k;
      _smoothShown = smooth;
      var snap = _snaps[k];
      for (int e = 0; e < snap.Length; e++) _result.Density[e] = snap[e];
      _result.Iteration = k;
      _mesh?.Dispose();
      _mesh = smooth ? ResultMesher3D.BuildSurface(_result, 0.5, 2) : ResultMesher3D.BuildVoxels(_result, Threshold(k));
    }

    double Azimuth(int frame, int total)
    {
      double u = frame / (double)Math.Max(1, total - 1);
      // Mostly a steady turn, eased in and out so neither end of the clip starts or stops dead.
      return _az0 + _sweep * (0.45 * u + 0.55 * Marks.Smooth(u));
    }

    string _aimNote;
    int _total;

    /// <summary>
    /// Orbits around the box and keeps it framed: the box spans `fill` of the frame width (or the 12-72 % height
    /// band, whichever is tighter), centred sideways, sitting in the band.
    /// </summary>
    void Aim(RhinoViewport vp, int frame, Rec rec)
    {
      if (_total == 0) _total = (int)rec.Arg("frames", Frames);
      double az = Azimuth(frame, _total) * Math.PI / 180, el = _el * Math.PI / 180;
      var f = -new Vector3d(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
      var r = Vector3d.CrossProduct(f, Vector3d.ZAxis);
      r.Unitize();
      var u = Vector3d.CrossProduct(r, f);
      var centre = _box.Center;
      // The box's top edge leaves room for the load arrows above it; the base sits on the 72 % line.
      const double top = 0.64, bottom = -0.44;
      double yc = (top + bottom) / 2, yh = (top - bottom) / 2;
      double wx = 0, hy = 0;
      for (int pass = 0; pass < 6; pass++)
      {
        var cam = centre + r * _sr + u * _su - f * _dist;
        vp.SetCameraLocations(cam + f * _dist, cam);
        vp.CameraUp = Vector3d.ZAxis;
        var xf = vp.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
        var size = vp.Size;
        double xmin = double.MaxValue, xmax = double.MinValue, ymin = double.MaxValue, ymax = double.MinValue;
        foreach (var c in _corners)
        {
          var s = xf * c;
          double nx = 2 * s.X / size.Width - 1, ny = 1 - 2 * s.Y / size.Height;
          xmin = Math.Min(xmin, nx); xmax = Math.Max(xmax, nx);
          ymin = Math.Min(ymin, ny); ymax = Math.Max(ymax, ny);
        }
        // World size of one NDC unit at the target's depth, from the projection itself.
        var t0 = xf * (cam + f * _dist);
        var t1 = xf * (cam + f * _dist + r);
        var t2 = xf * (cam + f * _dist + u);
        double perX = 1 / Math.Max(1e-9, Math.Abs(2 * (t1.X - t0.X) / size.Width));
        double perY = 1 / Math.Max(1e-9, Math.Abs(2 * (t2.Y - t0.Y) / size.Height));
        wx = (xmax - xmin) / 2; hy = (ymax - ymin) / 2;
        double scale = Math.Max(wx / _fill, hy / yh);
        _sr += (xmax + xmin) / 2 * perX;
        _su += ((ymax + ymin) / 2 - yc) * perY;
        _dist *= scale;
      }
      if (frame == 0) _aimNote = $"3D camera at frame 0: azimuth {Azimuth(0, _total):F1}°, elevation {_el}°, lens {_lens}, distance {_dist:F1}, box spans {100 * wx:F1}% of the width and {100 * hy:F1}% of the height";
    }

    public void Frame(Rec rec, int i)
    {
      Show(IterationAt(i), i >= _fs);
      Aim(rec.View.ActiveViewport, i, rec);
      if (_stage.Error != null) throw new InvalidOperationException(_stage.Error);
    }

    void Draw(DrawEventArgs e)
    {
      var d = e.Display;
      double k = Marks.K(d);
      if (_note == null) _note = $"pipeline frame {d.FrameSize.Width}x{d.FrameSize.Height}, viewport {e.Viewport.Size.Width}x{e.Viewport.Size.Height}, dpi scale {d.DpiScale}";
      if (_shadow)
      {
        d.EnableDepthWriting(false);
        foreach (var ring in _shadowRings) d.DrawMeshShaded(ring, _shade);
        d.EnableDepthWriting(true);
      }
      if (_mesh != null && _mesh.Faces.Count > 0)
      {
        d.DrawMeshShaded(_mesh, _material);
        if (_edges && !_smoothShown) d.DrawMeshWires(_mesh, Color.FromArgb(48, 78, 140), 1);
      }
      d.DrawBox(_box, Glyphs.DomainColor, Math.Max(1, (int)Math.Round(k * 0.7)));
      foreach (var p in _loadTips) Marks.Arrow(e, p, _force, k);
      foreach (var p in _feet) Marks.Support(e, p, true, k);
    }

    public void Teardown(Rec rec)
    {
      if (_note != null) rec.Note(_note);
      _stage?.Release();
      _mesh?.Dispose();
      _material?.Dispose();
      _shade?.Dispose();
      if (_shadowRings != null) foreach (var m in _shadowRings) m.Dispose();
    }
  }
}
