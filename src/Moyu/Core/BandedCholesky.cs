using System;
#if NETCOREAPP
using System.Numerics;
#endif

namespace Moyu.Core
{
  /// <summary>
  /// Symmetric positive-definite matrix in upper band storage, factorised in place as K = Uᵀ U.
  /// Entry K[r, c] (r ≤ c &lt; r + W) lives at A[r * W + (c - r)].
  /// </summary>
  public sealed class BandedCholesky
  {
    public readonly int N;
    public readonly int W;
    public readonly double[] A;

#if NETCOREAPP
    static readonly int Lanes = Vector<double>.Count;
#endif

    /// <summary>Largest factor the solver will allocate; the band store grows with N · W, so square 2D domains hit it first.</summary>
    const long MaxBytes = 1_500_000_000L;

    public BandedCholesky(int n, int bandWidth)
    {
      N = n;
      W = Math.Min(bandWidth, n);
      long bytes = (long)N * W * sizeof(double);
      if (bytes > MaxBytes)
        throw new InvalidOperationException(
          $"The stiffness matrix would need {bytes / 1e9:0.#} GB; lower the resolution (the shorter side of the domain drives the cost).");
      A = new double[N * W];
    }

    public void Clear() => Array.Clear(A, 0, A.Length);

    /// <summary>Adds to the upper triangle; callers pass r ≤ c.</summary>
    public void Add(int r, int c, double v) => A[r * W + (c - r)] += v;

    public void SetDiagonal(int r, double v) => A[r * W] = v;

    /// <summary>Returns false when a pivot is non-positive, i.e. the structure is a mechanism.</summary>
    public bool Factorize()
    {
      int n = N, w = W;
      double[] a = A;
      double maxDiag = 0.0;
      for (int i = 0; i < n; i++) maxDiag = Math.Max(maxDiag, a[i * w]);
      double pivotFloor = maxDiag * 1e-15;

      for (int i = 0; i < n; i++)
      {
        int ri = i * w;
        double d = a[ri];
        if (!(d > pivotFloor)) return false;
        double uii = Math.Sqrt(d);
        a[ri] = uii;
        double inv = 1.0 / uii;
        int kmax = Math.Min(w, n - i);
        for (int k = 1; k < kmax; k++) a[ri + k] *= inv;

        for (int k1 = 1; k1 < kmax; k1++)
        {
          double uij = a[ri + k1];
          if (uij == 0.0) continue;
          // Row i+k1 -= u_ij * (tail of row i); both runs are contiguous, so this vectorises.
          int dst = (i + k1) * w, src = ri + k1, len = kmax - k1, t = 0;
#if NETCOREAPP
          var vu = new Vector<double>(uij);
          for (; t <= len - Lanes; t += Lanes)
            (new Vector<double>(a, dst + t) - vu * new Vector<double>(a, src + t)).CopyTo(a, dst + t);
#endif
          for (; t < len; t++)
            a[dst + t] -= uij * a[src + t];
        }
      }
      return true;
    }

    /// <summary>Solves K x = b using the factor from <see cref="Factorize"/>; b and x may alias.</summary>
    public void Solve(double[] b, double[] x)
    {
      int n = N, w = W;
      double[] a = A;
      if (!ReferenceEquals(b, x)) Array.Copy(b, x, n);

      // Uᵀ y = b
      for (int i = 0; i < n; i++)
      {
        int ri = i * w;
        double yi = x[i] / a[ri];
        x[i] = yi;
        int kmax = Math.Min(w, n - i);
        for (int k = 1; k < kmax; k++) x[i + k] -= a[ri + k] * yi;
      }
      // U x = y
      for (int i = n - 1; i >= 0; i--)
      {
        int ri = i * w;
        double s = x[i];
        int kmax = Math.Min(w, n - i);
        for (int k = 1; k < kmax; k++) s -= a[ri + k] * x[i + k];
        x[i] = s / a[ri];
      }
    }
  }
}
