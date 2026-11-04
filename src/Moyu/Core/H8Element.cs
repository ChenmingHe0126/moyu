using System;

namespace Moyu.Core
{
  /// <summary>
  /// Stiffness of a unit-cube trilinear hexahedron with E = 1 (isotropic linear elasticity),
  /// integrated with 2×2×2 Gauss points. Nodes follow <see cref="Grid3D.Corner"/>; DOFs (ux, uy, uz) per node.
  /// </summary>
  public static class H8Element
  {
    public static double[] Stiffness(double nu)
    {
      double f = 1.0 / ((1.0 + nu) * (1.0 - 2.0 * nu));
      var d = new double[6, 6];
      for (int a = 0; a < 3; a++)
        for (int b = 0; b < 3; b++)
          d[a, b] = f * (a == b ? 1.0 - nu : nu);
      for (int a = 3; a < 6; a++) d[a, a] = f * (1.0 - 2.0 * nu) / 2.0;

      var ke = new double[24 * 24];
      var bm = new double[6, 24];
      var db = new double[6, 24];
      double g = 1.0 / Math.Sqrt(3.0);
      double detJ = 1.0 / 8.0; // [-1,1]³ → unit cube

      foreach (double xi in new[] { -g, g })
        foreach (double eta in new[] { -g, g })
          foreach (double zeta in new[] { -g, g })
          {
            Array.Clear(bm, 0, bm.Length);
            for (int n = 0; n < 8; n++)
            {
              double sx = 2 * Grid3D.Corner[n, 0] - 1, sy = 2 * Grid3D.Corner[n, 1] - 1, sz = 2 * Grid3D.Corner[n, 2] - 1;
              // dN/dx = 2 dN/dξ on the unit cube.
              double dx = 2.0 * 0.125 * sx * (1 + sy * eta) * (1 + sz * zeta);
              double dy = 2.0 * 0.125 * sy * (1 + sx * xi) * (1 + sz * zeta);
              double dz = 2.0 * 0.125 * sz * (1 + sx * xi) * (1 + sy * eta);
              int c = 3 * n;
              bm[0, c] = dx;
              bm[1, c + 1] = dy;
              bm[2, c + 2] = dz;
              bm[3, c] = dy; bm[3, c + 1] = dx;
              bm[4, c + 1] = dz; bm[4, c + 2] = dy;
              bm[5, c] = dz; bm[5, c + 2] = dx;
            }
            for (int r = 0; r < 6; r++)
              for (int c = 0; c < 24; c++)
              {
                double s = 0.0;
                for (int t = 0; t < 6; t++) s += d[r, t] * bm[t, c];
                db[r, c] = s;
              }
            for (int r = 0; r < 24; r++)
              for (int c = 0; c < 24; c++)
              {
                double s = 0.0;
                for (int t = 0; t < 6; t++) s += bm[t, r] * db[t, c];
                ke[r * 24 + c] += s * detJ;
              }
          }
      return ke;
    }
  }
}
