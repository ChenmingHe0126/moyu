using System;

namespace Moyu.Core
{
  /// <summary>SIMP on a 2D pixel grid: bilinear plane-stress elements, banded Cholesky direct solve.</summary>
  public sealed class SimpOptimizer2D : SimpOptimizer
  {
    double[] _ke;
    readonly int[] _edof;
    readonly double[] _u;
    BandedCholesky _k;

    public readonly Grid2D Grid;
    /// <summary>Nodal displacements of the last analysis.</summary>
    public double[] Displacement => _u;
    public Problem2D Problem => (Problem2D)CurrentProblem;

    public SimpOptimizer2D(Problem2D problem, SimpSettings settings)
      : base(problem, settings, new NeighborFilter(problem.Grid, problem.Kinds, settings.FilterRadius))
    {
      Grid = problem.Grid;
      _u = new double[Grid.DofCount];
      _edof = Grid.BuildElementDofs();
      _ke = Q4Element.Stiffness(problem.PoissonRatio);
    }

    protected override bool IsSameGrid(ProblemBase problem) =>
      problem is Problem2D p && p.Grid.Nx == Grid.Nx && p.Grid.Ny == Grid.Ny;

    protected override NeighborFilter CreateFilter(ProblemBase problem, double radius) =>
      new NeighborFilter(Grid, problem.Kinds, radius);

    protected override void OnProblemReplaced(ProblemBase previous, ProblemBase next)
    {
      if (next.PoissonRatio != previous.PoissonRatio) _ke = Q4Element.Stiffness(next.PoissonRatio);
    }

    /// <summary>Assembles K(xPhys), solves K u = f and stores the element energies uₑᵀ k₀ uₑ.</summary>
    protected override void Analyse(double[] xPhys, double penal, double emin, double e0, double[] energies)
    {
      var p = CurrentProblem;
      if (_k == null) _k = new BandedCholesky(Grid.DofCount, Grid.BandWidth);
      _k.Clear();

      bool[] fix = p.Fixed;
      int nel = xPhys.Length;
      for (int e = 0; e < nel; e++)
      {
        double stiffness = emin + Math.Pow(xPhys[e], penal) * (e0 - emin);
        int o = e * 8;
        for (int a = 0; a < 8; a++)
        {
          int r = _edof[o + a];
          if (fix[r]) continue;
          for (int b = 0; b < 8; b++)
          {
            int col = _edof[o + b];
            if (col < r || fix[col]) continue;
            _k.Add(r, col, stiffness * _ke[a * 8 + b]);
          }
        }
      }
      // Identity rows scaled by E, so the factorisation's relative pivot floor never rejects them.
      for (int d = 0; d < fix.Length; d++)
        if (fix[d]) _k.SetDiagonal(d, e0);

      if (!_k.Factorize())
        throw new InvalidOperationException("The structure is not stable: add supports that stop it from sliding and rotating.");

      for (int d = 0; d < _u.Length; d++) _u[d] = fix[d] ? 0.0 : p.Force[d];
      _k.Solve(_u, _u);

      for (int e = 0; e < nel; e++)
      {
        int o = e * 8;
        double acc = 0.0;
        for (int a = 0; a < 8; a++)
        {
          double ua = _u[_edof[o + a]];
          if (ua == 0.0) continue;
          double row = 0.0;
          for (int b = 0; b < 8; b++) row += _ke[a * 8 + b] * _u[_edof[o + b]];
          acc += ua * row;
        }
        energies[e] = acc;
      }
    }
  }
}
