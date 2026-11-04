using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Games.Stack.Core;
using Moyu.TwoD;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Games.Stack
{
  /// <summary>A custom piece made from Rhino geometry, remembering where it was drawn so the voxels preview in place.</summary>
  public sealed class StackUnit : IMoyuPreview
  {
    public readonly PieceDef Piece;
    /// <summary>World corner of voxel (0, 0, 0) and the voxel edge length.</summary>
    public readonly Plane Frame;
    public readonly double VoxelSize;

    public StackUnit(PieceDef piece, Plane frame, double voxelSize)
    {
      Piece = piece;
      Frame = frame;
      VoxelSize = voxelSize;
    }

    public Mesh ToMesh()
    {
      var mesh = new Mesh();
      double s = VoxelSize;
      foreach (var c in Piece.Cells)
        mesh.Append(Mesh.CreateFromBox(new Box(Frame, new Interval(c.X * s, (c.X + 1) * s), new Interval(c.Y * s, (c.Y + 1) * s), new Interval(c.Z * s, (c.Z + 1) * s)), 1, 1, 1));
      mesh.Normals.ComputeNormals();
      return mesh;
    }

    Mesh _preview;

    public BoundingBox Bounds => (_preview ?? (_preview = ToMesh())).GetBoundingBox(false);

    public void Draw(DisplayPipeline display, RhinoViewport viewport, Color? highlight)
    {
      var mesh = _preview ?? (_preview = ToMesh());
      var colour = Color.FromArgb(Piece.Colour);
      display.DrawMeshShaded(mesh, new DisplayMaterial(colour, 0.35));
      display.DrawMeshWires(mesh, highlight ?? Color.FromArgb(120, 50, 20));
    }

    public override string ToString() => $"Voxel unit \"{Piece.Name}\" ({Piece.Cells.Length} voxels, {Piece.OrientationCount} orientations)";

    /// <summary>
    /// Voxels whose centres lie inside any of the closed meshes, on a grid starting at the meshes' bounding box
    /// corner. Overlapping sketch boxes simply merge.
    /// </summary>
    public static List<Int3> Voxelize(IList<Mesh> meshes, double size, out Plane frame)
    {
      var box = BoundingBox.Empty;
      foreach (var m in meshes) box.Union(m.GetBoundingBox(true));
      frame = new Plane(box.Min, Vector3d.XAxis, Vector3d.YAxis);
      var cells = new List<Int3>();
      if (!box.IsValid || size <= 0) return cells;
      int nx = Math.Max(1, (int)Math.Round(box.Diagonal.X / size)), ny = Math.Max(1, (int)Math.Round(box.Diagonal.Y / size)), nz = Math.Max(1, (int)Math.Round(box.Diagonal.Z / size));
      if ((long)nx * ny * nz > 4096) return null;
      double tol = size * 1e-4;
      for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
          for (int x = 0; x < nx; x++)
          {
            var p = box.Min + new Vector3d((x + 0.5) * size, (y + 0.5) * size, (z + 0.5) * size);
            if (meshes.Any(m => m.IsPointInside(p, tol, false))) cells.Add(new Int3(x, y, z));
          }
      return cells;
    }

    /// <summary>The smallest bounding-box edge among the inputs: one sketch box = one voxel thick.</summary>
    public static double AutoSize(IEnumerable<Mesh> meshes)
    {
      double best = double.MaxValue;
      foreach (var m in meshes)
      {
        var d = m.GetBoundingBox(true).Diagonal;
        foreach (var v in new[] { d.X, d.Y, d.Z })
          if (v > 1e-9 && v < best) best = v;
      }
      return best == double.MaxValue ? 0 : best;
    }
  }

  /// <summary>The pieces a game deals: a built-in preset plus any custom units.</summary>
  public sealed class UnitLibrary
  {
    public readonly IReadOnlyList<PieceDef> Pieces;
    public readonly string Description;

    public UnitLibrary(IReadOnlyList<PieceDef> pieces, string description)
    {
      Pieces = pieces;
      Description = description;
    }

    public override string ToString() => $"Unit library: {Description} ({Pieces.Count} pieces)";
  }
}
