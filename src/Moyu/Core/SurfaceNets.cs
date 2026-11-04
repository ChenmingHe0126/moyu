using System;
using System.Collections.Generic;

namespace Moyu.Core
{
  /// <summary>
  /// Iso-surfaces of a sampled scalar field by naive surface nets: one vertex per boundary cell
  /// (the mean of its edge crossings), one quad per sign-changing lattice edge. The field is padded
  /// with zeros, so every surface is closed; quads are wound so normals point out of the material.
  /// </summary>
  public static class SurfaceNets
  {
    /// <summary>
    /// Voxel densities with outside voxels set to 0, sampled at voxel centres. With the iso-level at 0.5
    /// the surface sits on the faces between solid and empty voxels, so thin members keep their thickness.
    /// </summary>
    public static double[] VoxelField(Grid3D grid, double[] density, ElementKind[] kinds)
    {
      var f = new double[grid.ElementCount];
      for (int e = 0; e < f.Length; e++)
        f[e] = kinds != null && kinds[e] == ElementKind.Outside ? 0.0 : density[e];
      return f;
    }

    /// <summary>
    /// Extracts the surface of a field sampled on an sx × sy × sz lattice (natural order, (k·sy+j)·sx+i).
    /// Vertices come out in lattice coordinates; quads hold 4 vertex indices each.
    /// </summary>
    public static void Extract(int sx, int sy, int sz, double[] values, double level, List<double[]> vertices, List<int[]> quads)
    {
      int px = sx + 2, py = sy + 2, pz = sz + 2; // padded lattice
      double V(int I, int J, int K)
      {
        int i = I - 1, j = J - 1, k = K - 1;
        if (i < 0 || j < 0 || k < 0 || i >= sx || j >= sy || k >= sz) return 0.0;
        return values[(k * sy + j) * sx + i];
      }

      int cx = px - 1, cy = py - 1, cz = pz - 1;
      var cellVertex = new int[cx * cy * cz];
      for (int t = 0; t < cellVertex.Length; t++) cellVertex[t] = -1;
      int Cell(int I, int J, int K) => (K * cy + J) * cx + I;

      var corner = new double[8];
      for (int K = 0; K < cz; K++)
        for (int J = 0; J < cy; J++)
          for (int I = 0; I < cx; I++)
          {
            int inside = 0;
            for (int n = 0; n < 8; n++)
            {
              corner[n] = V(I + Grid3D.Corner[n, 0], J + Grid3D.Corner[n, 1], K + Grid3D.Corner[n, 2]);
              if (corner[n] >= level) inside++;
            }
            if (inside == 0 || inside == 8) continue;

            double mx = 0, my = 0, mz = 0;
            int count = 0;
            for (int e = 0; e < 12; e++)
            {
              int a = Edges[e, 0], b = Edges[e, 1];
              if ((corner[a] >= level) == (corner[b] >= level)) continue;
              double t = (level - corner[a]) / (corner[b] - corner[a]);
              mx += Grid3D.Corner[a, 0] + t * (Grid3D.Corner[b, 0] - Grid3D.Corner[a, 0]);
              my += Grid3D.Corner[a, 1] + t * (Grid3D.Corner[b, 1] - Grid3D.Corner[a, 1]);
              mz += Grid3D.Corner[a, 2] + t * (Grid3D.Corner[b, 2] - Grid3D.Corner[a, 2]);
              count++;
            }
            cellVertex[Cell(I, J, K)] = vertices.Count;
            vertices.Add(new[] { I - 1 + mx / count, J - 1 + my / count, K - 1 + mz / count });
          }

      void Quad(bool lowInside, int a, int b, int c, int d)
      {
        quads.Add(lowInside ? new[] { a, b, c, d } : new[] { a, d, c, b });
      }

      // Each sign-changing edge is shared by four cells; the edge's own axis may start at the padded
      // ring (index 0), the other two need a cell on both sides.
      for (int K = 0; K < cz; K++)
        for (int J = 0; J < cy; J++)
          for (int I = 0; I < cx; I++)
          {
            bool here = V(I, J, K) >= level;
            // Counter-clockwise seen from the + end of each edge.
            if (J > 0 && K > 0 && here != (V(I + 1, J, K) >= level))
              Quad(here, cellVertex[Cell(I, J - 1, K - 1)], cellVertex[Cell(I, J, K - 1)], cellVertex[Cell(I, J, K)], cellVertex[Cell(I, J - 1, K)]);
            if (I > 0 && K > 0 && here != (V(I, J + 1, K) >= level))
              Quad(here, cellVertex[Cell(I - 1, J, K - 1)], cellVertex[Cell(I - 1, J, K)], cellVertex[Cell(I, J, K)], cellVertex[Cell(I, J, K - 1)]);
            if (I > 0 && J > 0 && here != (V(I, J, K + 1) >= level))
              Quad(here, cellVertex[Cell(I - 1, J - 1, K)], cellVertex[Cell(I, J - 1, K)], cellVertex[Cell(I, J, K)], cellVertex[Cell(I - 1, J, K)]);
          }
    }

    /// <summary>Taubin smoothing (λ/μ passes), which rounds off voxel steps without shrinking the shape.</summary>
    public static void Smooth(List<double[]> vertices, List<int[]> quads, int iterations)
    {
      int n = vertices.Count;
      // Compressed adjacency: every quad edge once per direction, then each vertex's run sorted and de-duplicated,
      // which is far lighter than a hash set per vertex on results with hundreds of thousands of vertices.
      var start = new int[n + 1];
      foreach (var q in quads)
        for (int a = 0; a < 4; a++) { start[q[a] + 1]++; start[q[(a + 1) % 4] + 1]++; }
      for (int v = 0; v < n; v++) start[v + 1] += start[v];
      var adj = new int[start[n]];
      var fill = (int[])start.Clone();
      foreach (var q in quads)
        for (int a = 0; a < 4; a++)
        {
          int x = q[a], y = q[(a + 1) % 4];
          adj[fill[x]++] = y;
          adj[fill[y]++] = x;
        }
      var count = new int[n];
      for (int v = 0; v < n; v++)
      {
        int s = start[v], len = start[v + 1] - s;
        Array.Sort(adj, s, len);
        int kept = 0;
        for (int k = 0; k < len; k++)
          if (kept == 0 || adj[s + k] != adj[s + kept - 1]) adj[s + kept++] = adj[s + k];
        count[v] = kept;
      }

      var next = new double[n][];
      for (int it = 0; it < 2 * iterations; it++)
      {
        double factor = it % 2 == 0 ? 0.5 : -0.53;
        for (int v = 0; v < n; v++)
        {
          var p = vertices[v];
          int c = count[v];
          if (c == 0) { next[v] = p; continue; }
          double mx = 0, my = 0, mz = 0;
          for (int k = start[v]; k < start[v] + c; k++)
          {
            var u = vertices[adj[k]];
            mx += u[0]; my += u[1]; mz += u[2];
          }
          next[v] = new[] { p[0] + factor * (mx / c - p[0]), p[1] + factor * (my / c - p[1]), p[2] + factor * (mz / c - p[2]) };
        }
        for (int v = 0; v < n; v++) vertices[v] = next[v];
      }
    }

    static readonly int[,] Edges =
    {
      { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
      { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
      { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };
  }
}
