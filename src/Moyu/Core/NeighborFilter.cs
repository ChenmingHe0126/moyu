using System;

namespace Moyu.Core
{
  /// <summary>
  /// Linear hat filter H_ei = max(0, r - dist(e, i)) between design elements only,
  /// so passive and outside elements never blur into the design.
  /// </summary>
  public sealed class NeighborFilter
  {
    /// <summary>Largest neighbour table (12 bytes per entry) the filter will build; beyond it the radius is refused.</summary>
    const long MaxEntries = 100_000_000L;

    readonly int[] _start;
    readonly int[] _index;
    readonly double[] _weight;
    readonly double[] _sum;

    public NeighborFilter(Grid2D grid, ElementKind[] kinds, double radius)
      : this(kinds, radius, grid.Nx, grid.Ny, 1) { }

    public NeighborFilter(Grid3D grid, ElementKind[] kinds, double radius)
      : this(kinds, radius, grid.Nx, grid.Ny, grid.Nz) { }

    /// <summary>Elements indexed (k * ny + j) * nx + i, which matches both <see cref="Grid2D"/> and <see cref="Grid3D"/>.</summary>
    NeighborFilter(ElementKind[] kinds, double radius, int nx, int ny, int nz)
    {
      int nel = nx * ny * nz;
      radius = Math.Max(1.0, radius);
      int reach = Math.Max(0, (int)Math.Ceiling(radius) - 1);
      _start = new int[nel + 1];
      _sum = new double[nel];

      // Refuse hopeless combinations from the ball volume before the O(nel · span³) loops run.
      long design = 0;
      foreach (var k in kinds) if (k == ElementKind.Design) design++;
      double ball = nz > 1 ? 4.19 * radius * radius * radius : 3.15 * radius * radius;
      if (design * ball > MaxEntries) throw TooLarge(radius, design * ball);

      // Two passes over the same neighbourhoods: the first counts, the second fills tables allocated at their final size.
      long total = 0;
      for (int pass = 0; pass < 2; pass++)
      {
        if (pass == 1)
        {
          if (total > MaxEntries) throw TooLarge(radius, total);
          _index = new int[total];
          _weight = new double[total];
        }
        long n = 0;
        for (int k = 0; k < nz; k++)
          for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
              int e = (k * ny + j) * nx + i;
              _start[e] = (int)n;
              if (kinds[e] != ElementKind.Design) continue;
              double s = 0.0;
              for (int kk = Math.Max(0, k - reach); kk <= Math.Min(nz - 1, k + reach); kk++)
                for (int jj = Math.Max(0, j - reach); jj <= Math.Min(ny - 1, j + reach); jj++)
                  for (int ii = Math.Max(0, i - reach); ii <= Math.Min(nx - 1, i + reach); ii++)
                  {
                    int f = (kk * ny + jj) * nx + ii;
                    if (kinds[f] != ElementKind.Design) continue;
                    double w = radius - Math.Sqrt((i - ii) * (i - ii) + (j - jj) * (j - jj) + (k - kk) * (k - kk));
                    if (w <= 0.0) continue;
                    if (pass == 1)
                    {
                      _index[n] = f;
                      _weight[n] = w;
                      s += w;
                    }
                    n++;
                  }
              _sum[e] = s;
            }
        _start[nel] = (int)n;
        total = n;
      }
    }

    static InvalidOperationException TooLarge(double radius, double entries) =>
      new InvalidOperationException(
        $"Filter radius {radius:0.#} is too large for this grid (about {entries / 1e6:0} million neighbour pairs); lower it or the resolution.");

    /// <summary>xPhys_e = Σ H_ei x_i / Σ H_ei for design pixels; other entries are left untouched.</summary>
    public void FilterDensity(double[] x, double[] xPhys)
    {
      for (int e = 0; e < _sum.Length; e++)
      {
        if (_sum[e] <= 0.0) continue;
        double acc = 0.0;
        for (int k = _start[e]; k < _start[e + 1]; k++) acc += _weight[k] * x[_index[k]];
        xPhys[e] = acc / _sum[e];
      }
    }

    /// <summary>Chain rule of <see cref="FilterDensity"/>: d_e ← Σ H_ie d_i / Σ_i (H symmetric).</summary>
    public void BackPropagate(double[] d, double[] result)
    {
      for (int e = 0; e < _sum.Length; e++)
      {
        if (_sum[e] <= 0.0) { result[e] = 0.0; continue; }
        double acc = 0.0;
        for (int k = _start[e]; k < _start[e + 1]; k++)
        {
          int i = _index[k];
          acc += _weight[k] * d[i] / _sum[i];
        }
        result[e] = acc;
      }
    }

    /// <summary>Classic sensitivity filter of Sigmund (1997).</summary>
    public void FilterSensitivity(double[] x, double[] dc, double[] result)
    {
      for (int e = 0; e < _sum.Length; e++)
      {
        if (_sum[e] <= 0.0) { result[e] = 0.0; continue; }
        double acc = 0.0;
        for (int k = _start[e]; k < _start[e + 1]; k++)
        {
          int i = _index[k];
          acc += _weight[k] * x[i] * dc[i];
        }
        result[e] = acc / (_sum[e] * Math.Max(1e-3, x[e]));
      }
    }
  }
}
