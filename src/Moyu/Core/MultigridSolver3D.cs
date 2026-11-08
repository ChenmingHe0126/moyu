using System;
using System.Threading.Tasks;

namespace Moyu.Core
{
  /// <summary>
  /// Conjugate gradients preconditioned by one geometric multigrid V-cycle on a voxel grid
  /// (after Amir, Aage & Lazarov 2014). Coarse operators are Galerkin products built element by element
  /// from trilinear interpolation; the coarsest level is solved directly with a banded Cholesky.
  /// Fixed DOFs are eliminated on the fine level (identity rows); coarse levels see Pᵀ(ZKZ)P,
  /// plus the fixed DOFs' diagonal when that alone is singular (see <see cref="SetElementScale"/>).
  /// </summary>
  public sealed class MultigridSolver3D
  {
    const double Omega = 0.6;      // damped Jacobi
    const int Smoothing = 2;       // sweeps before and after the coarse correction

    sealed class Level
    {
      public Grid3D Grid;
      public int[] Edof;
      /// <summary>Per-element 24×24 matrices; null on the fine level, which uses scale × k₀.</summary>
      public double[] Ke;
      public double[] InvDiag;
      public double[] X, B, R, T;
    }

    readonly Level[] _levels;
    readonly double[] _ke0;
    readonly bool[] _fixed;
    readonly double[][] _childW;      // trilinear weights W[n, m] (8×8) for each of the 8 child positions
    readonly double[][] _childKe0;    // Tᵀ k₀ T per child position, for children without fixed DOFs
    double[] _scale;
    bool _keepFixedDiagonal;
    readonly bool _restrained;        // the fixed DOFs stop all six rigid motions
    BandedCholesky _coarse;
    readonly double[] _r, _z, _p, _q;

    public int LevelCount => _levels.Length;

    /// <summary>Largest level-1 Galerkin store (576 doubles per coarse element) the solver will allocate.</summary>
    const long MaxCoarseEntries = 150_000_000L;

    /// <param name="minCoarseNodes">Stop coarsening once a level has fewer nodes than this.</param>
    /// <param name="fineEdof">The fine grid's element DOF table when the caller already has one, so it is not built twice.</param>
    public MultigridSolver3D(Grid3D grid, double[] ke0, bool[] fixedDofs, int minCoarseNodes = 1500, int[] fineEdof = null)
    {
      _ke0 = ke0;
      _fixed = fixedDofs;
      int count = 1;
      for (int nx = grid.Nx, ny = grid.Ny, nz = grid.Nz;
           nx % 2 == 0 && ny % 2 == 0 && nz % 2 == 0 && (nx + 1) * (ny + 1) * (nz + 1) > minCoarseNodes;
           nx /= 2, ny /= 2, nz /= 2)
        count++;
      if (count > 1 && (long)(grid.Nx / 2) * (grid.Ny / 2) * (grid.Nz / 2) * 576 > MaxCoarseEntries)
        throw new InvalidOperationException(
          $"The voxel grid ({grid.Nx} × {grid.Ny} × {grid.Nz}) is too fine for the multigrid solver; lower the resolution.");

      _levels = new Level[count];
      for (int l = 0; l < count; l++)
      {
        int f = 1 << l;
        var g = l == 0 ? grid : new Grid3D(grid.Nx / f, grid.Ny / f, grid.Nz / f);
        var lv = new Level { Grid = g, Edof = l == 0 && fineEdof != null ? fineEdof : g.BuildElementDofs(), InvDiag = new double[g.DofCount] };
        if (l > 0)
        {
          lv.Ke = new double[g.ElementCount * 576];
          lv.X = new double[g.DofCount];
          lv.B = new double[g.DofCount];
        }
        lv.R = new double[g.DofCount];
        lv.T = new double[g.DofCount];
        _levels[l] = lv;
      }

      _childW = new double[8][];
      _childKe0 = new double[8][];
      for (int c = 0; c < 8; c++)
      {
        _childW[c] = ChildWeights(Grid3D.Corner[c, 0], Grid3D.Corner[c, 1], Grid3D.Corner[c, 2]);
        _childKe0[c] = new double[576];
        Galerkin(_childKe0[c], 0, ke0, 0, _childW[c], 1.0, new double[576]);
      }

      _restrained = RigidModesRestrained(grid, fixedDofs);
      int n = grid.DofCount;
      _r = new double[n];
      _z = new double[n];
      _p = new double[n];
      _q = new double[n];
    }

    /// <summary>
    /// Sets the fine-level element stiffness factors E(xₑ) and rebuilds every coarse operator.
    /// Returns false when the structure is a mechanism (the fixed DOFs leave a rigid motion free),
    /// or when the coarsest operator cannot be factorised.
    /// </summary>
    public bool SetElementScale(double[] scale)
    {
      _scale = scale;
      var fine = _levels[0];
      var g0 = fine.Grid;

      // Fine diagonal.
      var diag = fine.T;
      Array.Clear(diag, 0, diag.Length);
      for (int e = 0; e < g0.ElementCount; e++)
      {
        int o = e * 24;
        for (int a = 0; a < 24; a++) diag[fine.Edof[o + a]] += scale[e] * _ke0[a * 25];
      }
      for (int d = 0; d < diag.Length; d++)
        fine.InvDiag[d] = _fixed[d] ? 1.0 : (diag[d] > 0.0 ? 1.0 / diag[d] : 0.0);

      // Decided exactly up front: a near-singular factorisation of a mechanism can pass a pivot floor and PCG then
      // returns garbage.
      if (!_restrained) return false;
      BuildCoarseLevels();
      if (FactorCoarsest()) return true;
      // The structure is stable here, but masking can leave coarse directions that move only fixed fine DOFs
      // (Z P v = 0), e.g. a free node layer between fixed ones, and Pᵀ(ZKZ)P is then singular. Keeping the fixed DOFs'
      // diagonal stiffens exactly those directions. It depends only on which DOFs are fixed, so once needed it stays
      // on; solvers that never need it keep their numbers.
      if (_levels.Length == 1 || _keepFixedDiagonal) return false;
      _keepFixedDiagonal = true;
      BuildCoarseLevels();
      return FactorCoarsest();
    }

    /// <summary>
    /// Every element is at least Emin stiff, so the only motions without strain energy are the grid's six rigid ones,
    /// and the structure is stable exactly when the fixed DOFs stop all six. Their 6×6 Gram matrix over the fixed DOFs
    /// is summed in doubled, centred integer coordinates, so it is exact; its Cholesky pivots are measured against each
    /// mode's own diagonal, so one support DOF holding a mode still counts among millions holding the others.
    /// </summary>
    static bool RigidModesRestrained(Grid3D g, bool[] fixedDofs)
    {
      var gram = new double[36];
      var row = new double[6];
      for (int k = 0; k <= g.Nz; k++)
        for (int j = 0; j <= g.Ny; j++)
          for (int i = 0; i <= g.Nx; i++)
          {
            int node = g.Node(i, j, k);
            double x = 2 * i - g.Nx, y = 2 * j - g.Ny, z = 2 * k - g.Nz;
            for (int c = 0; c < 3; c++)
            {
              if (!fixedDofs[3 * node + c]) continue;
              // Value of each rigid mode (translations X, Y, Z, rotations about X, Y, Z) at this DOF.
              Array.Clear(row, 0, 6);
              row[c] = 1.0;
              if (c == 0) { row[4] = z; row[5] = -y; }
              else if (c == 1) { row[3] = -z; row[5] = x; }
              else { row[3] = y; row[4] = -x; }
              for (int a = 0; a < 6; a++)
                for (int b = 0; b < 6; b++) gram[a * 6 + b] += row[a] * row[b];
            }
          }
      for (int a = 0; a < 6; a++)
      {
        double own = gram[a * 7];
        double d = own;
        for (int t = 0; t < a; t++) d -= gram[a * 6 + t] * gram[a * 6 + t];
        // A free mode leaves only rounding (about 1e-16 of its diagonal); a mode nothing touches has a zero diagonal.
        if (!(d > 1e-10 * own)) return false;
        d = Math.Sqrt(d);
        gram[a * 7] = d;
        for (int b = a + 1; b < 6; b++)
        {
          double v = gram[b * 6 + a];
          for (int t = 0; t < a; t++) v -= gram[b * 6 + t] * gram[a * 6 + t];
          gram[b * 6 + a] = v / d;
        }
      }
      return true;
    }

    void BuildCoarseLevels()
    {
      for (int l = 1; l < _levels.Length; l++)
      {
        BuildCoarse(l);
        var lv = _levels[l];
        Array.Clear(lv.T, 0, lv.T.Length);
        for (int e = 0; e < lv.Grid.ElementCount; e++)
        {
          int o = e * 24;
          for (int a = 0; a < 24; a++) lv.T[lv.Edof[o + a]] += lv.Ke[e * 576 + a * 25];
        }
        for (int d = 0; d < lv.T.Length; d++) lv.InvDiag[d] = lv.T[d] > 0.0 ? 1.0 / lv.T[d] : 0.0;
      }
    }

    /// <summary>Solves K u = f by PCG starting from the given u. Returns the number of iterations used.</summary>
    public int Solve(double[] f, double[] u, double tolerance, int maxIterations, out double relativeResidual)
    {
      int n = u.Length;
      for (int d = 0; d < n; d++) if (_fixed[d]) u[d] = 0.0;

      double normF = Math.Sqrt(Dot(f, f));
      relativeResidual = 0.0;
      if (normF == 0.0) { Array.Clear(u, 0, n); return 0; }

      Apply(0, u, _q);
      for (int d = 0; d < n; d++) _r[d] = _fixed[d] ? 0.0 : f[d] - _q[d];
      relativeResidual = Math.Sqrt(Dot(_r, _r)) / normF;
      if (relativeResidual <= tolerance) return 0;

      Precondition(_r, _z);
      Array.Copy(_z, _p, n);
      double rz = Dot(_r, _z);
      for (int it = 1; it <= maxIterations; it++)
      {
        Apply(0, _p, _q);
        double alpha = rz / Dot(_p, _q);
        for (int d = 0; d < n; d++)
        {
          u[d] += alpha * _p[d];
          _r[d] -= alpha * _q[d];
        }
        relativeResidual = Math.Sqrt(Dot(_r, _r)) / normF;
        if (relativeResidual <= tolerance) return it;
        Precondition(_r, _z);
        double rzNew = Dot(_r, _z);
        double beta = rzNew / rz;
        rz = rzNew;
        for (int d = 0; d < n; d++) _p[d] = _z[d] + beta * _p[d];
      }
      return maxIterations;
    }

    void Precondition(double[] r, double[] z)
    {
      VCycle(0, r, z);
      for (int d = 0; d < z.Length; d++) if (_fixed[d]) z[d] = 0.0;
    }

    void VCycle(int l, double[] b, double[] x)
    {
      var lv = _levels[l];
      if (l == _levels.Length - 1)
      {
        _coarse.Solve(b, x);
        if (l == 0) for (int d = 0; d < x.Length; d++) if (_fixed[d]) x[d] = 0.0;
        return;
      }

      var inv = lv.InvDiag;
      for (int d = 0; d < x.Length; d++) x[d] = Omega * inv[d] * b[d];
      for (int s = 1; s < Smoothing; s++) Smooth(l, b, x);

      Apply(l, x, lv.T);
      for (int d = 0; d < x.Length; d++) lv.R[d] = b[d] - lv.T[d];
      var next = _levels[l + 1];
      Restrict(lv.Grid, lv.R, next.Grid, next.B);
      VCycle(l + 1, next.B, next.X);
      Prolong(next.Grid, next.X, lv.Grid, lv.T);
      for (int d = 0; d < x.Length; d++) x[d] += lv.T[d];
      if (l == 0) for (int d = 0; d < x.Length; d++) if (_fixed[d]) x[d] = 0.0;

      for (int s = 0; s < Smoothing; s++) Smooth(l, b, x);
    }

    void Smooth(int l, double[] b, double[] x)
    {
      var lv = _levels[l];
      Apply(l, x, lv.T);
      var inv = lv.InvDiag;
      for (int d = 0; d < x.Length; d++) x[d] += Omega * inv[d] * (b[d] - lv.T[d]);
    }

    /// <summary>y = A_l x. Element layers two apart share no nodes, so each half runs in parallel.</summary>
    void Apply(int l, double[] x, double[] y)
    {
      var lv = _levels[l];
      var g = lv.Grid;
      Array.Clear(y, 0, y.Length);
      bool fine = l == 0;
      int layers = g.Nz;
      for (int phase = 0; phase < 2; phase++)
      {
        int count = (layers - phase + 1) / 2;
        Parallel.For(0, count, t =>
        {
          int k = 2 * t + phase;
          var ue = new double[24];
          for (int j = 0; j < g.Ny; j++)
            for (int i = 0; i < g.Nx; i++)
            {
              int e = g.Element(i, j, k);
              int o = e * 24;
              for (int a = 0; a < 24; a++)
              {
                int d = lv.Edof[o + a];
                ue[a] = fine && _fixed[d] ? 0.0 : x[d];
              }
              double[] m = fine ? _ke0 : lv.Ke;
              int mo = fine ? 0 : e * 576;
              double s = fine ? _scale[e] : 1.0;
              for (int a = 0; a < 24; a++)
              {
                int d = lv.Edof[o + a];
                if (fine && _fixed[d]) continue;
                double acc = 0.0;
                int row = mo + a * 24;
                for (int c = 0; c < 24; c++) acc += m[row + c] * ue[c];
                y[d] += s * acc;
              }
            }
        });
      }
      if (fine)
        for (int d = 0; d < y.Length; d++) if (_fixed[d]) y[d] = x[d];
    }

    /// <summary>Galerkin coarse element matrices: K_E = Σ_children Tᵀ K_child T.</summary>
    void BuildCoarse(int l)
    {
      var lv = _levels[l];
      var prev = _levels[l - 1];
      var g = lv.Grid;
      var gp = prev.Grid;
      bool fromFine = l == 1;
      Parallel.For(0, g.Nz, K =>
      {
        var tmp = new double[576];
        var masked = new double[576];
        for (int J = 0; J < g.Ny; J++)
          for (int I = 0; I < g.Nx; I++)
          {
            int E = g.Element(I, J, K);
            int eo = E * 576;
            Array.Clear(lv.Ke, eo, 576);
            for (int c = 0; c < 8; c++)
            {
              int child = gp.Element(2 * I + Grid3D.Corner[c, 0], 2 * J + Grid3D.Corner[c, 1], 2 * K + Grid3D.Corner[c, 2]);
              if (!fromFine)
              {
                Galerkin(lv.Ke, eo, prev.Ke, child * 576, _childW[c], 1.0, tmp);
                continue;
              }
              double s = _scale[child];
              int co = child * 24;
              bool anyFixed = false;
              for (int a = 0; a < 24 && !anyFixed; a++) anyFixed = _fixed[prev.Edof[co + a]];
              if (!anyFixed)
              {
                var gk = _childKe0[c];
                for (int t = 0; t < 576; t++) lv.Ke[eo + t] += s * gk[t];
                continue;
              }
              // Zero rows and columns of fixed DOFs: the coarse levels see Pᵀ(ZKZ)P (plus their diagonal if kept).
              for (int a = 0; a < 24; a++)
              {
                bool fa = _fixed[prev.Edof[co + a]];
                for (int b = 0; b < 24; b++)
                  masked[a * 24 + b] = fa || _fixed[prev.Edof[co + b]] ? 0.0 : _ke0[a * 24 + b];
                if (fa && _keepFixedDiagonal) masked[a * 25] = _ke0[a * 25];
              }
              Galerkin(lv.Ke, eo, masked, 0, _childW[c], s, tmp);
            }
          }
      });
    }

    bool FactorCoarsest()
    {
      int l = _levels.Length - 1;
      var lv = _levels[l];
      var g = lv.Grid;
      if (_coarse == null) _coarse = new BandedCholesky(g.DofCount, g.BandWidth);
      _coarse.Clear();
      bool fine = l == 0;
      for (int e = 0; e < g.ElementCount; e++)
      {
        int o = e * 24;
        for (int a = 0; a < 24; a++)
        {
          int r = lv.Edof[o + a];
          if (fine && _fixed[r]) continue;
          for (int b = 0; b < 24; b++)
          {
            int c = lv.Edof[o + b];
            if (c < r || (fine && _fixed[c])) continue;
            double v = fine ? _scale[e] * _ke0[a * 24 + b] : lv.Ke[e * 576 + a * 24 + b];
            _coarse.Add(r, c, v);
          }
        }
      }
      // Identity rows for eliminated DOFs (fine) and for coarse DOFs that only touch fixed fine DOFs,
      // scaled like the assembled diagonal so the factorisation's relative pivot floor never rejects them.
      double unit = 0.0;
      for (int d = 0; d < g.DofCount; d++) unit = Math.Max(unit, _coarse.A[d * _coarse.W]);
      if (unit <= 0.0) unit = 1.0;
      for (int d = 0; d < g.DofCount; d++)
        if ((fine && _fixed[d]) || _coarse.A[d * _coarse.W] == 0.0) _coarse.SetDiagonal(d, unit);
      return _coarse.Factorize();
    }

    /// <summary>Fine → coarse: transpose of trilinear prolongation, gathered per coarse node.</summary>
    static void Restrict(Grid3D fine, double[] rf, Grid3D coarse, double[] rc)
    {
      Parallel.For(0, coarse.Nz + 1, K =>
      {
        for (int J = 0; J <= coarse.Ny; J++)
          for (int I = 0; I <= coarse.Nx; I++)
          {
            double sx = 0, sy = 0, sz = 0;
            for (int dk = -1; dk <= 1; dk++)
            {
              int fk = 2 * K + dk;
              if (fk < 0 || fk > fine.Nz) continue;
              double wk = dk == 0 ? 1.0 : 0.5;
              for (int dj = -1; dj <= 1; dj++)
              {
                int fj = 2 * J + dj;
                if (fj < 0 || fj > fine.Ny) continue;
                double wj = wk * (dj == 0 ? 1.0 : 0.5);
                for (int di = -1; di <= 1; di++)
                {
                  int fi = 2 * I + di;
                  if (fi < 0 || fi > fine.Nx) continue;
                  double w = wj * (di == 0 ? 1.0 : 0.5);
                  int f = 3 * fine.Node(fi, fj, fk);
                  sx += w * rf[f];
                  sy += w * rf[f + 1];
                  sz += w * rf[f + 2];
                }
              }
            }
            int c = 3 * coarse.Node(I, J, K);
            rc[c] = sx;
            rc[c + 1] = sy;
            rc[c + 2] = sz;
          }
      });
    }

    /// <summary>Coarse → fine trilinear interpolation.</summary>
    static void Prolong(Grid3D coarse, double[] xc, Grid3D fine, double[] xf)
    {
      Parallel.For(0, fine.Nz + 1, fk =>
      {
        for (int fj = 0; fj <= fine.Ny; fj++)
          for (int fi = 0; fi <= fine.Nx; fi++)
          {
            double sx = 0, sy = 0, sz = 0;
            int k0 = fk / 2, k1 = (fk + 1) / 2;
            int j0 = fj / 2, j1 = (fj + 1) / 2;
            int i0 = fi / 2, i1 = (fi + 1) / 2;
            for (int K = k0; K <= k1; K++)
            {
              double wk = k0 == k1 ? 1.0 : 0.5;
              for (int J = j0; J <= j1; J++)
              {
                double wj = wk * (j0 == j1 ? 1.0 : 0.5);
                for (int I = i0; I <= i1; I++)
                {
                  double w = wj * (i0 == i1 ? 1.0 : 0.5);
                  int c = 3 * coarse.Node(I, J, K);
                  sx += w * xc[c];
                  sy += w * xc[c + 1];
                  sz += w * xc[c + 2];
                }
              }
            }
            int f = 3 * fine.Node(fi, fj, fk);
            xf[f] = sx;
            xf[f + 1] = sy;
            xf[f + 2] = sz;
          }
      });
    }

    /// <summary>
    /// Trilinear weights of a child element's 8 nodes in terms of the parent's 8 nodes:
    /// child at offset (cx, cy, cz) ∈ {0,1}³ occupies one octant of the parent.
    /// </summary>
    static double[] ChildWeights(int cx, int cy, int cz)
    {
      var w = new double[64];
      for (int n = 0; n < 8; n++)
      {
        double tx = (cx + Grid3D.Corner[n, 0]) / 2.0, ty = (cy + Grid3D.Corner[n, 1]) / 2.0, tz = (cz + Grid3D.Corner[n, 2]) / 2.0;
        for (int m = 0; m < 8; m++)
          w[n * 8 + m] = Phi(Grid3D.Corner[m, 0], tx) * Phi(Grid3D.Corner[m, 1], ty) * Phi(Grid3D.Corner[m, 2], tz);
      }
      return w;
    }

    static double Phi(int corner, double t) => corner == 0 ? 1.0 - t : t;

    /// <summary>dst += scale · Tᵀ S T with T[3n+a, 3m+b] = W[n, m] δ_ab.</summary>
    static void Galerkin(double[] dst, int dstOff, double[] src, int srcOff, double[] w, double scale, double[] tmp)
    {
      // tmp = S T
      for (int r = 0; r < 24; r++)
        for (int m = 0; m < 8; m++)
          for (int b = 0; b < 3; b++)
          {
            double acc = 0.0;
            for (int n = 0; n < 8; n++)
            {
              double wv = w[n * 8 + m];
              if (wv != 0.0) acc += src[srcOff + r * 24 + 3 * n + b] * wv;
            }
            tmp[r * 24 + 3 * m + b] = acc;
          }
      // dst += scale · Tᵀ tmp
      for (int m = 0; m < 8; m++)
        for (int a = 0; a < 3; a++)
          for (int c = 0; c < 24; c++)
          {
            double acc = 0.0;
            for (int n = 0; n < 8; n++)
            {
              double wv = w[n * 8 + m];
              if (wv != 0.0) acc += wv * tmp[(3 * n + a) * 24 + c];
            }
            dst[dstOff + (3 * m + a) * 24 + c] += scale * acc;
          }
    }

    static double Dot(double[] a, double[] b)
    {
      double s = 0.0;
      for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
      return s;
    }
  }
}
