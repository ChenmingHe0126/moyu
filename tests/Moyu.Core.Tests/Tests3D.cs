using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Moyu.Core;

namespace Moyu.Core.Tests
{
  static class Tests3D
  {
    /// <summary>Uniform tension on a solid block with symmetry rollers; trilinear hexahedra are exact here.</summary>
    public static void PatchTest(Action<bool, string> check)
    {
      Console.WriteLine("3D patch test (4x3x2 solid block, unit tension)");
      int nx = 4, ny = 3, nz = 2;
      var g = new Grid3D(nx, ny, nz);
      var p = new Problem3D(g) { YoungsModulus = 2.0, PoissonRatio = 0.3 };
      for (int e = 0; e < g.ElementCount; e++) p.Kinds[e] = ElementKind.Solid;
      for (int k = 0; k <= nz; k++)
        for (int j = 0; j <= ny; j++)
          for (int i = 0; i <= nx; i++)
          {
            int n = g.Node(i, j, k);
            if (i == 0) p.Fixed[3 * n] = true;
            if (j == 0) p.Fixed[3 * n + 1] = true;
            if (k == 0) p.Fixed[3 * n + 2] = true;
          }
      // Unit traction on x = nx: every face square hands a quarter to each of its corners.
      for (int k = 0; k < nz; k++)
        for (int j = 0; j < ny; j++)
          foreach (var (dj, dk) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
            p.Force[3 * g.Node(nx, j + dj, k + dk)] += 0.25;

      var opt = new SimpOptimizer3D(p, new SimpSettings()) { SolverTolerance = 1e-12 };
      opt.Step();
      double ux = opt.Displacement[3 * g.Node(nx, ny, nz)];
      double uy = opt.Displacement[3 * g.Node(0, ny, 0) + 1];
      double uz = opt.Displacement[3 * g.Node(0, 0, nz) + 2];
      check(Math.Abs(ux - nx / 2.0) < 1e-8, $"ux = {ux:F10} (expected {nx / 2.0})");
      check(Math.Abs(uy + 0.3 * ny / 2.0) < 1e-8, $"uy = {uy:F10} (expected {-0.3 * ny / 2.0})");
      check(Math.Abs(uz + 0.3 * nz / 2.0) < 1e-8, $"uz = {uz:F10} (expected {-0.3 * nz / 2.0})");
    }

    /// <summary>Grids whose level-1 Galerkin store would not fit are refused before anything is allocated.</summary>
    public static void SizeGuard(Action<bool, string> check)
    {
      Console.WriteLine("3D size guard");
      var ke = H8Element.Stiffness(0.3);
      var g = new Grid3D(160, 160, 160);
      check(Program.Throws<InvalidOperationException>(() => new MultigridSolver3D(g, ke, new bool[g.DofCount])), "160³ voxels are refused by the multigrid solver");
    }

    /// <summary>MGCG on a high-contrast density field must reproduce the direct solution.</summary>
    public static void MultigridMatchesDirect(Action<bool, string> check)
    {
      Console.WriteLine("Multigrid CG vs direct solve (16x8x8, contrast 1e-9)");
      var g = new Grid3D(16, 8, 8);
      var ke = H8Element.Stiffness(0.3);
      var fixedDofs = new bool[g.DofCount];
      for (int k = 0; k <= g.Nz; k++)
        for (int j = 0; j <= g.Ny; j++)
          for (int c = 0; c < 3; c++) fixedDofs[3 * g.Node(0, j, k) + c] = true;
      var f = new double[g.DofCount];
      f[3 * g.Node(g.Nx, g.Ny / 2, 0) + 2] = -1.0;
      var rnd = new Random(7);
      var scale = new double[g.ElementCount];
      for (int e = 0; e < scale.Length; e++) scale[e] = rnd.NextDouble() < 0.4 ? 1e-9 : 0.05 + rnd.NextDouble();

      var direct = new MultigridSolver3D(g, ke, fixedDofs, minCoarseNodes: int.MaxValue);
      direct.SetElementScale(scale);
      var ud = new double[g.DofCount];
      direct.Solve(f, ud, 1e-12, 5, out _);

      var mg = new MultigridSolver3D(g, ke, fixedDofs, minCoarseNodes: 20, fineEdof: g.BuildElementDofs());
      check(mg.LevelCount == 4, $"multigrid uses {mg.LevelCount} levels");
      mg.SetElementScale(scale);
      var um = new double[g.DofCount];
      int its = mg.Solve(f, um, 1e-10, 500, out double res);
      double err = Math.Sqrt(ud.Zip(um, (a, b) => (a - b) * (a - b)).Sum() / ud.Sum(a => a * a));
      check(res <= 1e-10 && err < 1e-7, $"{its} PCG iterations, relative difference {err:E2}");
    }

    public static void SurfaceNetsClosed(Action<bool, string> check)
    {
      Console.WriteLine("Surface nets");
      var g = new Grid3D(6, 5, 4);
      var d = new double[g.ElementCount];
      for (int k = 1; k <= 2; k++)
        for (int j = 1; j <= 3; j++)
          for (int i = 0; i <= 3; i++) // touches the grid boundary on purpose
            d[g.Element(i, j, k)] = 1.0;
      d[g.Element(5, 4, 3)] = 1.0; // separate corner voxel
      var verts = new List<double[]>();
      var quads = new List<int[]>();
      SurfaceNets.Extract(g.Nx, g.Ny, g.Nz, SurfaceNets.VoxelField(g, d, null), 0.5, verts, quads);
      var (closed, volume) = Inspect(verts, quads);
      check(closed, $"closed: every edge shared by an even number of quads ({quads.Count} quads)");
      check(volume > 0, $"outward normals (signed volume {volume:F2})");
      SurfaceNets.Smooth(verts, quads, 3);
      check(Inspect(verts, quads).volume > 0, "still outward after smoothing");
    }

    static (bool closed, double volume) Inspect(List<double[]> v, List<int[]> quads)
    {
      var edges = new Dictionary<(int, int), int>();
      double vol = 0.0;
      foreach (var q in quads)
      {
        for (int a = 0; a < 4; a++)
        {
          int x = q[a], y = q[(a + 1) % 4];
          var key = x < y ? (x, y) : (y, x);
          edges[key] = edges.TryGetValue(key, out int c) ? c + 1 : 1;
        }
        foreach (var (a, b, c) in new[] { (q[0], q[1], q[2]), (q[0], q[2], q[3]) })
        {
          var p = v[a]; var r = v[b]; var s = v[c];
          vol += (p[0] * (r[1] * s[2] - r[2] * s[1]) - p[1] * (r[0] * s[2] - r[2] * s[0]) + p[2] * (r[0] * s[1] - r[1] * s[0])) / 6.0;
        }
      }
      // Naive surface nets may pinch where voxels touch only along an edge (4 quads on one edge);
      // the surface is still closed as long as every edge count is even.
      return (edges.Values.All(c => c % 2 == 0), vol);
    }

    static (Problem3D, SimpSettings) CantileverProblem(int nx, int ny, int nz)
    {
      var g = new Grid3D(nx, ny, nz);
      var p = new Problem3D(g);
      for (int k = 0; k <= nz; k++)
        for (int j = 0; j <= ny; j++)
          for (int c = 0; c < 3; c++) p.Fixed[3 * g.Node(0, j, k) + c] = true;
      // Line load down along the bottom edge of the free end.
      for (int j = 0; j <= ny; j++) p.Force[3 * g.Node(nx, j, 0) + 2] = -1.0 / (ny + 1);
      var s = new SimpSettings { VolumeFraction = 0.15, FilterRadius = 1.5, MaxIterations = 150 };
      return (p, s);
    }

    public static void Cantilever(Action<bool, string> check)
    {
      Console.WriteLine("3D cantilever 24x12x12, volfrac 0.15");
      var (p, s) = CantileverProblem(24, 12, 12);
      var opt = new SimpOptimizer3D(p, s);
      var sw = Stopwatch.StartNew();
      int cg = 0;
      while (!opt.IsFinished) { opt.Step(); cg += opt.LastSolverIterations; }
      sw.Stop();
      Console.WriteLine($"  {opt.Iteration} iterations, compliance {opt.Compliance:F3}, volume {opt.Volume:F4}, " +
        $"{sw.ElapsedMilliseconds} ms, {opt.MultigridLevels} MG levels, {cg / (double)opt.Iteration:F1} CG iterations per analysis");
      check(opt.Converged, "converged before the iteration cap");
      check(Math.Abs(opt.Volume - 0.15) < 2e-3, "volume constraint met");
      check(opt.History[0] > 2 * opt.Compliance, $"compliance improved from {opt.History[0]:F2}");

      var verts = new List<double[]>();
      var quads = new List<int[]>();
      SurfaceNets.Extract(p.Grid.Nx, p.Grid.Ny, p.Grid.Nz, SurfaceNets.VoxelField(p.Grid, opt.XPhys, p.Kinds), 0.5, verts, quads);
      var (closed, volume) = Inspect(verts, quads);
      // The 0.5 surface wraps the voxels at or above 0.5 (minus chamfered corners).
      int solid = opt.XPhys.Count(x => x >= 0.5);
      check(closed && volume > 0.6 * solid && volume < 1.2 * solid,
        $"iso-surface of the result is closed and encloses {volume:F0} voxels ({solid} voxels at or above 0.5)");
    }

    /// <summary>Canopy as in the Rhino test: four corner pins, roof load, solid roof slab, void passage.</summary>
    public static void CanopyTrace()
    {
      int nx = 32, ny = 32, nz = 16;
      var g = new Grid3D(nx, ny, nz);
      var p = new Problem3D(g);
      for (int k = 0; k < nz; k++)
        for (int j = 0; j < ny; j++)
          for (int i = 0; i < nx; i++)
          {
            int e = g.Element(i, j, k);
            if (k == nz - 1) p.Kinds[e] = ElementKind.Solid;
            else if (k < 6 && i >= 10 && i < 22) p.Kinds[e] = ElementKind.Void;
          }
      foreach (var (i, j) in new[] { (0, 0), (nx, 0), (nx, ny), (0, ny) })
        for (int c = 0; c < 3; c++) p.Fixed[3 * g.Node(i, j, 0) + c] = true;
      for (int j = 0; j <= ny; j++)
        for (int i = 0; i <= nx; i++) p.Force[3 * g.Node(i, j, nz) + 2] = -1.0 / ((nx + 1) * (ny + 1));
      var opt = new SimpOptimizer3D(p, new SimpSettings { VolumeFraction = 0.15, FilterRadius = 1.5, MaxIterations = 200 });
      var sw = Stopwatch.StartNew();
      while (!opt.IsFinished)
      {
        opt.Step();
        if (opt.Iteration % 10 == 0 || opt.IsFinished)
          Console.WriteLine($"  canopy it {opt.Iteration,3}: {opt.LastSolverIterations,4} CG its, c {opt.Compliance:G5}, change {opt.Change:F3}, {sw.ElapsedMilliseconds} ms");
      }
    }

    /// <summary>The Rhino scenario's cantilever (32×16×16, volume 0.2): how compliance and change evolve.</summary>
    public static void CantileverTrace()
    {
      var (p, s) = CantileverProblem(32, 16, 16);
      s.VolumeFraction = 0.2;
      s.MaxIterations = 300;
      var opt = new SimpOptimizer3D(p, s);
      var sw = Stopwatch.StartNew();
      while (!opt.IsFinished)
      {
        opt.Step();
        int k = opt.Iteration;
        if (k % 10 == 0 || opt.IsFinished)
        {
          double rel = k > 10 ? Enumerable.Range(k - 10, 10).Max(t => Math.Abs(opt.History[t] - opt.History[t - 1]) / opt.History[t]) : double.NaN;
          Console.WriteLine($"  trace3d it {k,3}: c {opt.Compliance:F4}, change {opt.Change:F3}, max rel dc(10) {rel:E2}, CG {opt.LastSolverIterations}, {sw.ElapsedMilliseconds} ms");
        }
      }
    }

    public static void Benchmark()
    {
      foreach (var (nx, ny, nz) in new[] { (24, 12, 12), (32, 16, 16), (48, 24, 24), (64, 32, 32) })
      {
        var (p, s) = CantileverProblem(nx, ny, nz);
        var opt = new SimpOptimizer3D(p, s);
        var sw = Stopwatch.StartNew();
        int cg = 0;
        for (int k = 0; k < 6; k++) { opt.Step(); cg += opt.LastSolverIterations; }
        Console.WriteLine($"  bench 3D {nx}x{ny}x{nz} ({nx * ny * nz} voxels): {sw.ElapsedMilliseconds / 6.0:F0} ms / iteration, {cg / 6.0:F1} CG its, {opt.MultigridLevels} levels");
      }
    }
  }
}
