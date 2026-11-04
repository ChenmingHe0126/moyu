using System;
using System.Collections.Generic;
using Moyu.Core;
using Rhino.Geometry;

namespace Moyu.ThreeD
{
  public enum DisplayMode3D
  {
    /// <summary>Blocky voxels at or above the threshold.</summary>
    Voxels = 0,
    /// <summary>Smoothed iso-surface at the threshold.</summary>
    Smooth = 1,
  }

  /// <summary>Turns a <see cref="Result3D"/> into meshes in world space.</summary>
  public static class ResultMesher3D
  {
    // Outward faces of a unit voxel: neighbour offset, then corners counter-clockwise seen from outside.
    static readonly int[][] Faces =
    {
      new[] { -1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0 },
      new[] { 1, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1 },
      new[] { 0, -1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1 },
      new[] { 0, 1, 0, 0, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0 },
      new[] { 0, 0, -1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0 },
      new[] { 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1 },
    };

    /// <summary>Faces between a voxel that passes <paramref name="filled"/> and one that does not (or the grid edge).</summary>
    public static Mesh VoxelFaces(VoxelDomain3D domain, Func<int, bool> filled)
    {
      var g = domain.Grid;
      var mesh = new Mesh();
      for (int k = 0; k < g.Nz; k++)
        for (int j = 0; j < g.Ny; j++)
          for (int i = 0; i < g.Nx; i++)
          {
            if (!filled(g.Element(i, j, k))) continue;
            foreach (var f in Faces)
            {
              int ni = i + f[0], nj = j + f[1], nk = k + f[2];
              bool inGrid = ni >= 0 && nj >= 0 && nk >= 0 && ni < g.Nx && nj < g.Ny && nk < g.Nz;
              if (inGrid && filled(g.Element(ni, nj, nk))) continue;
              int v = mesh.Vertices.Count;
              for (int c = 0; c < 4; c++)
                mesh.Vertices.Add(domain.ToWorld(i + f[3 + 3 * c], j + f[4 + 3 * c], k + f[5 + 3 * c]));
              mesh.Faces.AddFace(v, v + 1, v + 2, v + 3);
            }
          }
      mesh.Normals.ComputeNormals();
      return mesh;
    }

    public static Mesh BuildVoxels(Result3D r, double threshold) =>
      VoxelFaces(r.Domain, e => r.Kinds[e] != ElementKind.Outside && r.Density[e] >= threshold);

    /// <summary>Closed, smoothed iso-surface at the threshold (surface nets on voxel-centre densities).</summary>
    public static Mesh BuildSurface(Result3D r, double threshold, int smoothing = 2)
    {
      var g = r.Domain.Grid;
      var verts = new List<double[]>();
      var quads = new List<int[]>();
      SurfaceNets.Extract(g.Nx, g.Ny, g.Nz, SurfaceNets.VoxelField(g, r.Density, r.Kinds), threshold, verts, quads);
      if (smoothing > 0) SurfaceNets.Smooth(verts, quads, smoothing);

      var mesh = new Mesh();
      foreach (var v in verts) mesh.Vertices.Add(r.Domain.ToWorld(v[0] + 0.5, v[1] + 0.5, v[2] + 0.5));
      foreach (var q in quads) mesh.Faces.AddFace(q[0], q[1], q[2], q[3]);
      mesh.Normals.ComputeNormals();
      return mesh;
    }
  }
}
