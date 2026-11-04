namespace Moyu.Core
{
  /// <summary>
  /// Plane-stress stiffness of a unit-square bilinear element with E = 1 and unit thickness.
  /// For square elements the matrix does not depend on the element size.
  /// Node order: LL, LR, UR, UL (counter-clockwise, +Y up); DOFs (ux, uy) per node.
  /// </summary>
  public static class Q4Element
  {
    public static double[] Stiffness(double nu)
    {
      double[] k =
      {
        0.5 - nu / 6.0, 0.125 + nu / 8.0, -0.25 - nu / 12.0, -0.125 + 3.0 * nu / 8.0,
        -0.25 + nu / 12.0, -0.125 - nu / 8.0, nu / 6.0, 0.125 - 3.0 * nu / 8.0,
      };
      int[,] map =
      {
        { 0, 1, 2, 3, 4, 5, 6, 7 },
        { 1, 0, 7, 6, 5, 4, 3, 2 },
        { 2, 7, 0, 5, 6, 3, 4, 1 },
        { 3, 6, 5, 0, 7, 2, 1, 4 },
        { 4, 5, 6, 7, 0, 1, 2, 3 },
        { 5, 4, 3, 2, 1, 0, 7, 6 },
        { 6, 3, 4, 1, 2, 7, 0, 5 },
        { 7, 2, 1, 4, 3, 6, 5, 0 },
      };
      double scale = 1.0 / (1.0 - nu * nu);
      var ke = new double[64];
      for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
          ke[r * 8 + c] = scale * k[map[r, c]];
      return ke;
    }
  }
}
