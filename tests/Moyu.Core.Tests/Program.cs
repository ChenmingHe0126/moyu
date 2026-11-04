using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Moyu.Core;

namespace Moyu.Core.Tests
{
  static class Program
  {
    static int _failures;

    static int Main(string[] args)
    {
      PatchTest();
      ContourTest();
      LiveUpdateTest();
      VolumeRaiseTest();
      NaNHealTest();
      SizeGuardTest();
      Mbb(FilterKind.Sensitivity, 1.5, print: true);
      Mbb(FilterKind.Density, 1.5, print: true);
      Tests3D.PatchTest(Check);
      Tests3D.SizeGuard(Check);
      Tests3D.MultigridMatchesDirect(Check);
      Tests3D.SurfaceNetsClosed(Check);
      Tests3D.Cantilever(Check);
      TestsStack.Run(Check);
      TestsSnake.Run(Check);
      TestsBreakout.Run(Check);
      TestsSwift.Run(Check);
      if (args.Contains("--bench")) Benchmark();
      if (args.Contains("--bench")) Tests3D.Benchmark();
      if (args.Contains("--canopy")) Tests3D.CanopyTrace();
      if (args.Contains("--trace3d")) Tests3D.CantileverTrace();
      if (args.Contains("--trace")) Trace();
      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    internal static void Check(bool ok, string what)
    {
      Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
      if (!ok) _failures++;
    }

    /// <summary>Uniform tension on a solid block; Q4 elements reproduce constant strain exactly.</summary>
    static void PatchTest()
    {
      Console.WriteLine("Patch test (10x4 solid block, unit tension)");
      int nx = 10, ny = 4;
      var g = new Grid2D(nx, ny);
      var p = new Problem2D(g) { YoungsModulus = 2.0, PoissonRatio = 0.3 };
      for (int e = 0; e < g.ElementCount; e++) p.Kinds[e] = ElementKind.Solid;
      for (int j = 0; j <= ny; j++)
      {
        p.Fixed[2 * g.Node(0, j)] = true;
        p.Force[2 * g.Node(nx, j)] = (j == 0 || j == ny) ? 0.5 / ny : 1.0 / ny;
      }
      p.Fixed[2 * g.Node(0, 0) + 1] = true;

      var opt = new SimpOptimizer2D(p, new SimpSettings());
      opt.Step();
      double ux = opt.Displacement[2 * g.Node(nx, ny)];
      double uy = opt.Displacement[2 * g.Node(0, ny) + 1];
      double uxExpected = 1.0 * nx / (2.0 * ny);
      double uyExpected = -0.3 * 1.0 / 2.0;
      Check(Math.Abs(ux - uxExpected) < 1e-9, $"ux = {ux:F12} (expected {uxExpected})");
      Check(Math.Abs(uy - uyExpected) < 1e-9, $"uy = {uy:F12} (expected {uyExpected})");
      Check(Math.Abs(opt.Compliance - uxExpected) < 1e-9, $"compliance = {opt.Compliance:F12}");
      Check(opt.Converged, "no design pixels → converged immediately");

      // A huge modulus must not push the fixed-DOF identity rows under the relative pivot floor.
      p.YoungsModulus = 1e16;
      var stiff = new SimpOptimizer2D(p, new SimpSettings());
      stiff.Step();
      Check(Math.Abs(stiff.Compliance - uxExpected * 2.0 / 1e16) < 1e-24, $"E = 1e16 still factorises (compliance {stiff.Compliance:E3})");
    }

    /// <summary>Raising the volume fraction after convergence must regrow material without NaN or a runaway bisection.</summary>
    static void VolumeRaiseTest()
    {
      Console.WriteLine("Volume raise test (MBB 60x20, 0.3 → 0.4 after convergence)");
      var (p, s) = MbbProblem(60, 20, FilterKind.Density, 1.5);
      s.VolumeFraction = 0.3;
      var opt = new SimpOptimizer2D(p, s);
      while (!opt.IsFinished) opt.Step();
      Check(opt.Converged && Math.Abs(opt.Volume - 0.3) < 2e-3, $"converged at volume {opt.Volume:F4}");

      var raised = s.Clone();
      raised.VolumeFraction = 0.4;
      opt.Update(p, raised);
      var sw = Stopwatch.StartNew();
      opt.Step();
      sw.Stop();
      Check(!opt.X.Any(double.IsNaN) && !opt.XPhys.Any(double.IsNaN), "no NaN densities after the raise");
      Check(!double.IsNaN(opt.Volume) && !double.IsInfinity(opt.Volume) && opt.Volume > 0.3,
        $"volume finite and grew to {opt.Volume:F4} in one step ({sw.ElapsedMilliseconds} ms)");
      Check(opt.X.All(x => x >= 0.0 && x <= 1.0), "densities stay within [0, 1]");
      for (int k = 0; k < 10; k++) opt.Step();
      Check(!double.IsNaN(opt.Compliance) && Math.Abs(opt.Volume - 0.4) < 2e-3, $"reaches the new target ({opt.Volume:F4}) and keeps solving");
    }

    /// <summary>An optimiser seeded from a NaN volume fraction heals on the next valid update, without a Reset.</summary>
    static void NaNHealTest()
    {
      Console.WriteLine("NaN heal test (MBB 30x10, volume fraction NaN → 0.4)");
      var (p, s) = MbbProblem(30, 10, FilterKind.Density, 1.5);
      var bad = s.Clone();
      bad.VolumeFraction = double.NaN;
      var opt = new SimpOptimizer2D(p, bad);
      Check(opt.X.Any(double.IsNaN), "a NaN volume fraction poisons the densities");
      var good = s.Clone();
      good.VolumeFraction = 0.4;
      opt.Update(p, good);
      Check(opt.X.All(x => !double.IsNaN(x) && !double.IsInfinity(x)), "update re-seeds the non-finite densities");
      bool threw = false;
      try { opt.Step(); }
      catch (Exception) { threw = true; }
      Check(!threw && !double.IsNaN(opt.Compliance) && !double.IsInfinity(opt.Volume) && Math.Abs(opt.Volume - 0.4) < 0.01,
        $"steps again without an exception (volume {opt.Volume:F4})");
    }

    /// <summary>Oversized filters and stiffness factors are refused with the exception the solver component reports.</summary>
    static void SizeGuardTest()
    {
      Console.WriteLine("Size guard test");
      var big = new Grid2D(600, 600);
      var kinds = new ElementKind[big.ElementCount];
      Check(Throws<InvalidOperationException>(() => new NeighborFilter(big, kinds, 12.0)), "600x600 with radius 12 is refused");
      Check(Throws<InvalidOperationException>(() => new BandedCholesky(200000, 2000)), "3.2 GB band store is refused");

      // The two-pass build must reproduce a hat filter: a uniform field stays uniform and sums are symmetric.
      var g = new Grid2D(9, 6);
      kinds = new ElementKind[g.ElementCount];
      for (int e = 0; e < kinds.Length; e++) kinds[e] = e % 7 == 0 ? ElementKind.Outside : ElementKind.Design;
      var filter = new NeighborFilter(g, kinds, 2.5);
      var x = new double[g.ElementCount];
      var xp = new double[g.ElementCount];
      for (int e = 0; e < x.Length; e++) x[e] = kinds[e] == ElementKind.Design ? 0.4 : 0.0;
      filter.FilterDensity(x, xp);
      Check(Enumerable.Range(0, x.Length).All(e => kinds[e] != ElementKind.Design || Math.Abs(xp[e] - 0.4) < 1e-12), "uniform field stays uniform");
      // Adjoint identity ⟨H x, d⟩ = ⟨x, Hᵀ d⟩ over random fields.
      var rnd = new Random(3);
      var d = new double[g.ElementCount];
      var back = new double[g.ElementCount];
      for (int e = 0; e < x.Length; e++) { x[e] = rnd.NextDouble(); d[e] = rnd.NextDouble(); }
      filter.FilterDensity(x, xp);
      filter.BackPropagate(d, back);
      double lhs = 0.0, rhs = 0.0;
      for (int e = 0; e < x.Length; e++)
        if (kinds[e] == ElementKind.Design) { lhs += xp[e] * d[e]; rhs += x[e] * back[e]; }
      Check(Math.Abs(lhs - rhs) < 1e-12 * Math.Abs(lhs), $"back-propagation is the filter's adjoint ({lhs:F6} vs {rhs:F6})");
    }

    internal static bool Throws<T>(Action action) where T : Exception
    {
      try { action(); }
      catch (T) { return true; }
      catch { return false; }
      return false;
    }

    static void ContourTest()
    {
      Console.WriteLine("Contour test");
      var g = new Grid2D(7, 7);
      var d = new double[g.ElementCount];
      for (int j = 2; j <= 4; j++)
        for (int i = 2; i <= 4; i++)
          d[g.Element(i, j)] = 1.0;
      var nodal = MarchingSquares.NodalField(g, d, null);
      var loops = MarchingSquares.Contour(g.Nx, g.Ny, nodal, 0.5);
      Check(loops.Count == 1, $"one loop around a 3x3 solid block (got {loops.Count})");
      var loop = loops[0];
      bool closed = loop[0][0] == loop[loop.Count - 1][0] && loop[0][1] == loop[loop.Count - 1][1];
      Check(closed, "loop is closed");
      double minX = loop.Min(q => q[0]), maxX = loop.Max(q => q[0]);
      double minY = loop.Min(q => q[1]), maxY = loop.Max(q => q[1]);
      Check(Math.Abs(minX - 2) < 1e-9 && Math.Abs(maxX - 5) < 1e-9 && Math.Abs(minY - 2) < 1e-9 && Math.Abs(maxY - 5) < 1e-9,
        $"0.5 contour runs along the pixel edges: x[{minX},{maxX}] y[{minY},{maxY}]");

      // Two separate blobs and a solid touching the grid edge must all close.
      var d2 = new double[g.ElementCount];
      d2[g.Element(0, 0)] = 1.0;
      d2[g.Element(4, 3)] = 1.0;
      var loops2 = MarchingSquares.Contour(g.Nx, g.Ny, MarchingSquares.NodalField(g, d2, null), 0.2);
      Check(loops2.Count == 2, $"two edge-touching blobs → two loops (got {loops2.Count})");
    }

    /// <summary>Moving the load mid-run keeps densities and clears convergence.</summary>
    static void LiveUpdateTest()
    {
      Console.WriteLine("Live update test");
      var (p, s) = MbbProblem(30, 10, FilterKind.Density, 1.5);
      var opt = new SimpOptimizer2D(p, s);
      for (int k = 0; k < 15; k++) opt.Step();
      var before = (double[])opt.XPhys.Clone();
      var (p2, _) = MbbProblem(30, 10, FilterKind.Density, 1.5);
      Array.Clear(p2.Force, 0, p2.Force.Length);
      p2.Force[2 * p2.Grid.Node(15, 10) + 1] = -1.0;
      opt.Update(p2, s);
      Check(!opt.Converged && opt.Iteration == 15, "update keeps iteration count and clears convergence");
      Check(before.SequenceEqual(opt.XPhys), "update keeps densities");
      opt.Step();
      Check(!double.IsNaN(opt.Compliance), "steps fine after update");

      // An exhausted iteration budget is renewed when the problem changes.
      var (p3, s3) = MbbProblem(30, 10, FilterKind.Density, 1.5);
      s3.MaxIterations = 8;
      var opt3 = new SimpOptimizer2D(p3, s3);
      while (!opt3.IsFinished) opt3.Step();
      Check(opt3.ReachedMaxIterations && opt3.Iteration == 8, "stops at the iteration budget");
      opt3.Update(p2, s3);
      Check(!opt3.IsFinished, "changing the load renews the budget");
      while (!opt3.IsFinished) opt3.Step();
      Check(opt3.Iteration == 16, $"second budget used ({opt3.Iteration})");
    }

    static (Problem2D, SimpSettings) MbbProblem(int nx, int ny, FilterKind filter, double rmin)
    {
      var g = new Grid2D(nx, ny);
      var p = new Problem2D(g);
      // Half MBB beam: symmetry on the left edge, roller at bottom right, load at top left.
      for (int j = 0; j <= ny; j++) p.Fixed[2 * g.Node(0, j)] = true;
      p.Fixed[2 * g.Node(nx, 0) + 1] = true;
      p.Force[2 * g.Node(0, ny) + 1] = -1.0;
      var s = new SimpSettings { VolumeFraction = 0.5, Penalty = 3.0, FilterRadius = rmin, Filter = filter, MaxIterations = 300 };
      return (p, s);
    }

    static void Mbb(FilterKind filter, double rmin, bool print)
    {
      Console.WriteLine($"MBB 60x20, volfrac 0.5, penal 3, rmin {rmin}, {filter} filter");
      var (p, s) = MbbProblem(60, 20, filter, rmin);
      var opt = new SimpOptimizer2D(p, s);
      var sw = Stopwatch.StartNew();
      while (!opt.IsFinished) opt.Step();
      sw.Stop();
      Console.WriteLine($"  iterations {opt.Iteration}, compliance {opt.Compliance:F3}, volume {opt.Volume:F4}, {sw.ElapsedMilliseconds} ms");
      Check(opt.Converged, "converged before the iteration cap");
      Check(Math.Abs(opt.Volume - 0.5) < 2e-3, "volume constraint met");
      bool monotoneTail = opt.History.Skip(opt.History.Count / 2).Zip(opt.History.Skip(opt.History.Count / 2 + 1), (a, b) => b <= a * 1.02).All(x => x);
      Check(monotoneTail, "compliance settles (no late blow-up)");
      Check(opt.History[0] > opt.Compliance, $"compliance improved from {opt.History[0]:F2}");
      if (print) Console.WriteLine(Ascii(opt));
    }

    static string Ascii(SimpOptimizer2D opt)
    {
      var g = opt.Grid;
      var sb = new StringBuilder();
      const string ramp = " .:-=+*#%@";
      for (int j = g.Ny - 1; j >= 0; j--)
      {
        sb.Append("  ");
        for (int i = 0; i < g.Nx; i++)
        {
          double x = opt.XPhys[g.Element(i, j)];
          sb.Append(ramp[Math.Min(ramp.Length - 1, (int)(x * ramp.Length))]);
        }
        sb.AppendLine();
      }
      return sb.ToString();
    }

    /// <summary>Cantilever 90x45 as in the Rhino scenario: prints how compliance and change evolve.</summary>
    static void Trace()
    {
      var g = new Grid2D(90, 45);
      var p = new Problem2D(g);
      for (int j = 0; j <= 45; j++) { p.Fixed[2 * g.Node(0, j)] = true; p.Fixed[2 * g.Node(0, j) + 1] = true; }
      p.Force[2 * g.Node(90, 22) + 1] = -1.0;
      var opt = new SimpOptimizer2D(p, new SimpSettings { VolumeFraction = 0.4, FilterRadius = 2.0, MaxIterations = 400 });
      while (!opt.IsFinished)
      {
        opt.Step();
        int k = opt.Iteration;
        if (k % 10 == 0 || opt.IsFinished)
        {
          double rel = k > 5 ? Enumerable.Range(k - 5, 5).Max(t => Math.Abs(opt.History[t] - opt.History[t - 1]) / opt.History[t]) : double.NaN;
          Console.WriteLine($"  it {k,3}  c {opt.Compliance,10:F4}  change {opt.Change:F4}  maxRelDc(5) {rel:E2}");
        }
      }
    }

    static void Benchmark()
    {
      foreach (var (nx, ny) in new[] { (60, 30), (90, 45), (120, 60), (160, 80), (200, 100), (100, 100), (250, 125), (300, 150) })
      {
        var (p, s) = MbbProblem(nx, ny, FilterKind.Density, 2.5);
        var opt = new SimpOptimizer2D(p, s);
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 5; k++) opt.Step();
        Console.WriteLine($"  bench {nx}x{ny}: {sw.ElapsedMilliseconds / 5.0:F0} ms / iteration");
      }
    }
  }
}
