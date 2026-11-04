using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Core;
using Moyu.TwoD;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Moyu.ThreeD
{
  /// <summary>
  /// The 3D design domain: a plane-aligned voxel grid plus the voxels whose centres fall inside the
  /// boundary solids. The plane origin sits on the grid corner; grid units = plane coordinates / voxel size.
  /// </summary>
  public sealed class VoxelDomain3D : IMoyuPreview
  {
    public readonly Plane Plane;
    public readonly double VoxelSize;
    public readonly Grid3D Grid;
    /// <summary>Per voxel ((k·Ny + j)·Nx + i): centre lies inside the boundary.</summary>
    public readonly bool[] Inside;

    Mesh _shell;

    VoxelDomain3D(Plane plane, double voxelSize, Grid3D grid)
    {
      Plane = plane;
      VoxelSize = voxelSize;
      Grid = grid;
      Inside = new bool[grid.ElementCount];
    }

    public int InsideCount => Inside.Count(b => b);

    public Point3d ToWorld(double gx, double gy, double gz) =>
      Plane.Origin + Plane.XAxis * (gx * VoxelSize) + Plane.YAxis * (gy * VoxelSize) + Plane.ZAxis * (gz * VoxelSize);

    public Point3d ToGrid(Point3d p)
    {
      var v = p - Plane.Origin;
      return new Point3d(v * Plane.XAxis / VoxelSize, v * Plane.YAxis / VoxelSize, v * Plane.ZAxis / VoxelSize);
    }

    /// <summary>World → grid units (the inverse of <see cref="ToWorld"/>).</summary>
    public Transform WorldToGrid => Transform.Scale(Point3d.Origin, 1.0 / VoxelSize) * Transform.PlaneToPlane(Plane, Plane.WorldXY);

    public BoundingBox Bounds
    {
      get
      {
        var box = BoundingBox.Empty;
        for (int c = 0; c < 8; c++)
          box.Union(ToWorld(Grid3D.Corner[c, 0] * Grid.Nx, Grid3D.Corner[c, 1] * Grid.Ny, Grid3D.Corner[c, 2] * Grid.Nz));
        return box;
      }
    }

    /// <summary>
    /// Voxels whose centres lie inside the given closed meshes. Each mesh is filled by ray parity along X;
    /// several meshes combine even-odd, so a solid nested in another becomes a hole.
    /// </summary>
    public bool[] Rasterize(IEnumerable<Mesh> worldMeshes)
    {
      int nx = Grid.Nx, ny = Grid.Ny, nz = Grid.Nz;
      var mask = new bool[Grid.ElementCount];
      var xform = WorldToGrid;
      var xs = new List<double>();
      foreach (var source in worldMeshes)
      {
        var m = source.DuplicateMesh();
        m.Transform(xform);
        var box = m.GetBoundingBox(true);
        for (int k = 0; k < nz; k++)
          for (int j = 0; j < ny; j++)
          {
            // A tiny irrational offset keeps rays off triangle edges that line up with voxel centres.
            double y = j + 0.5 + 1.2345e-4, z = k + 0.5 + 2.3456e-4;
            if (y < box.Min.Y || y > box.Max.Y || z < box.Min.Z || z > box.Max.Z) continue;
            var hits = Intersection.MeshLine(m, new Line(new Point3d(box.Min.X - 1, y, z), new Point3d(box.Max.X + 1, y, z)), out _);
            if (hits == null || hits.Length == 0) continue;
            xs.Clear();
            foreach (var h in hits.Select(p => p.X).OrderBy(v => v))
              if (xs.Count == 0 || h - xs[xs.Count - 1] > 1e-7) xs.Add(h);
            bool inside = false;
            int next = 0;
            for (int i = 0; i < nx; i++)
            {
              double c = i + 0.5;
              while (next < xs.Count && xs[next] < c) { inside = !inside; next++; }
              if (inside) mask[Grid.Element(i, j, k)] ^= true;
            }
          }
      }
      return mask;
    }

    /// <summary>Outer faces of the inside voxels, for previewing the voxelisation.</summary>
    public Mesh Shell => _shell ?? (_shell = ResultMesher3D.VoxelFaces(this, e => Inside[e]));

    public void Draw(DisplayPipeline d, RhinoViewport vp, Color? highlight)
    {
      d.DrawMeshWires(Shell, highlight ?? Color.FromArgb(170, 170, 170));
    }

    public override string ToString() =>
      $"Voxel domain 3D ({Grid.Nx}×{Grid.Ny}×{Grid.Nz}, {InsideCount} voxels, size {VoxelSize:G4})";

    /// <summary>
    /// Builds the grid around closed solids. <paramref name="resolution"/> is the number of voxels along the
    /// longest side; each count is rounded up to a multiple of 4 (8 for fine grids) so multigrid can coarsen it.
    /// </summary>
    public static VoxelDomain3D Create(IList<Mesh> solids, int resolution, Plane? plane, out string error)
    {
      error = null;
      solids = solids?.Where(m => m != null && m.IsValid && m.Faces.Count > 0).ToList();
      if (solids == null || solids.Count == 0) { error = "No valid boundary solid."; return null; }
      resolution = Math.Max(4, resolution);
      var basePlane = plane.HasValue && plane.Value.IsValid ? plane.Value : Plane.WorldXY;

      var toLocal = Transform.PlaneToPlane(basePlane, Plane.WorldXY);
      var box = BoundingBox.Empty;
      foreach (var m in solids)
      {
        var local = m.DuplicateMesh();
        local.Transform(toLocal);
        box.Union(local.GetBoundingBox(true));
      }
      var size = box.Diagonal;
      double longest = Math.Max(size.X, Math.Max(size.Y, size.Z));
      if (!(longest > 0)) { error = "Boundary has no volume."; return null; }

      double h = longest / resolution;
      int multiple = resolution > 64 ? 8 : 4;
      int Count(double extent) => Math.Max(multiple, (int)Math.Ceiling(Math.Ceiling(extent / h - 1e-6) / multiple) * multiple);
      var grid = new Grid3D(Count(size.X), Count(size.Y), Count(size.Z));

      var origin = basePlane.Origin + basePlane.XAxis * box.Min.X + basePlane.YAxis * box.Min.Y + basePlane.ZAxis * box.Min.Z;
      var domain = new VoxelDomain3D(new Plane(origin, basePlane.XAxis, basePlane.YAxis), h, grid);
      var inside = domain.Rasterize(solids);
      Array.Copy(inside, domain.Inside, inside.Length);
      if (domain.InsideCount == 0) { error = "Resolution too coarse: no voxel centre lies inside the boundary."; return null; }
      return domain;
    }
  }
}
