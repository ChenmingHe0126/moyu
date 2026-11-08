using System;

namespace Moyu.Core
{
  /// <summary>SIMP on a 3D voxel grid: trilinear hexahedra, multigrid-preconditioned CG with warm starts.</summary>
  public sealed class SimpOptimizer3D : SimpOptimizer
  {
    double[] _ke;
    readonly int[] _edof;
    readonly double[] _u;
    readonly double[] _f;
    readonly double[] _scale;
    MultigridSolver3D _solver;

    public readonly Grid3D Grid;
    /// <summary>Nodal displacements of the last analysis; also the starting guess of the next one.</summary>
    public double[] Displacement => _u;
    public Problem3D Problem => (Problem3D)CurrentProblem;
    /// <summary>Relative residual the CG iterations stop at.</summary>
    public double SolverTolerance = 1e-6;
    public int SolverMaxIterations = 200;
    /// <summary>CG iterations of the last analysis.</summary>
    public int LastSolverIterations { get; private set; }
    public int MultigridLevels => _solver?.LevelCount ?? 0;

    public SimpOptimizer3D(Problem3D problem, SimpSettings settings)
      : base(problem, settings, new NeighborFilter(problem.Grid, problem.Kinds, settings.FilterRadius))
    {
      Grid = problem.Grid;
      _u = new double[Grid.DofCount];
      _f = new double[Grid.DofCount];
      _scale = new double[Grid.ElementCount];
      _edof = Grid.BuildElementDofs();
      _ke = H8Element.Stiffness(problem.PoissonRatio);
    }

    protected override bool IsSameGrid(ProblemBase problem) =>
      problem is Problem3D p && p.Grid.Nx == Grid.Nx && p.Grid.Ny == Grid.Ny && p.Grid.Nz == Grid.Nz;

    protected override NeighborFilter CreateFilter(ProblemBase problem, double radius) =>
      new NeighborFilter(Grid, problem.Kinds, radius);

    protected override void OnProblemReplaced(ProblemBase previous, ProblemBase next)
    {
      if (next.PoissonRatio != previous.PoissonRatio)
      {
        _ke = H8Element.Stiffness(next.PoissonRatio);
        _solver = null;
      }
      // The multigrid hierarchy bakes in which DOFs are fixed.
      for (int d = 0; d < next.Fixed.Length && _solver != null; d++)
        if (next.Fixed[d] != previous.Fixed[d]) _solver = null;
    }

    protected override void Analyse(double[] xPhys, double penal, double emin, double e0, double[] energies)
    {
      var p = CurrentProblem;
      for (int e = 0; e < _scale.Length; e++) _scale[e] = emin + Math.Pow(xPhys[e], penal) * (e0 - emin);
      if (_solver == null) _solver = new MultigridSolver3D(Grid, _ke, p.Fixed, fineEdof: _edof);
      if (!_solver.SetElementScale(_scale))
        throw new InvalidOperationException("The structure is not stable: add supports that stop it from sliding and rotating.");

      for (int d = 0; d < _f.Length; d++) _f[d] = p.Fixed[d] ? 0.0 : p.Force[d];
      // A failed solve (e.g. a NaN load) can leave non-finite displacements; warm-starting from them would fail forever.
      for (int d = 0; d < _u.Length; d++)
        if (double.IsNaN(_u[d]) || double.IsInfinity(_u[d])) { Array.Clear(_u, 0, _u.Length); break; }
      LastSolverIterations = _solver.Solve(_f, _u, SolverTolerance, SolverMaxIterations, out double residual);
      if (double.IsNaN(residual))
        throw new InvalidOperationException("The structure is not stable: add supports that stop it from sliding and rotating.");
      if (residual > 1e-2)
        throw new InvalidOperationException(
          $"The analysis did not converge (residual {residual:0.0e0} after {LastSolverIterations} CG iterations): " +
          "the structure may be unstable (add supports), or press Reset to discard the stale starting guess.");

      int nel = xPhys.Length;
      System.Threading.Tasks.Parallel.For(0, nel, e =>
      {
        int o = e * 24;
        double acc = 0.0;
        for (int a = 0; a < 24; a++)
        {
          double ua = _u[_edof[o + a]];
          if (ua == 0.0) continue;
          double row = 0.0;
          for (int b = 0; b < 24; b++) row += _ke[a * 24 + b] * _u[_edof[o + b]];
          acc += ua * row;
        }
        energies[e] = acc;
      });
    }
  }
}
