using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Components.ThreeD;
using Moyu.Games.Stack;
using Moyu.Games.Stack.Core;
using Rhino.Geometry;

namespace Moyu.Components.Massing
{
  /// <summary>
  /// A custom unit for Massing Study, from sketched solids (voxel centres inside them), voxel-centre points, or a text
  /// pattern. Checks that it is one face-connected piece of at most 10 voxels and 4 along any side.
  /// </summary>
  public class VoxelUnitComponent : MoyuComponent
  {
    /// <summary>Most points the Points input accepts.</summary>
    const int MaxPoints = 64;

    public VoxelUnitComponent()
      : base("Voxel Unit", "Unit",
        "Turns sketched solids, points or a text pattern into a voxel unit for Massing Study. One face-connected piece, up to 10 voxels, at most 4 along any side.",
        "Massing")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Geometry", "G", "Closed breps or meshes; every voxel whose centre lies inside one of them is kept. Boxes need not sit on a grid.", GH_ParamAccess.list);
      pManager.AddPointParameter("Points", "Pt", "Alternative to Geometry: one point per voxel centre.", GH_ParamAccess.list);
      pManager.AddTextParameter("Pattern", "Pa", "Alternative: layers bottom-up separated by '|', rows by '/' or new lines, '#' = voxel, '.' or a space = empty, so do not pad '/' or '|' with spaces. E.g. ###/#.#|#..", GH_ParamAccess.item);
      pManager.AddNumberParameter("Size", "S", "Voxel edge. Default: the thinnest side of the sketched solids, the closest spacing of the points, or 1 for a pattern.", GH_ParamAccess.item);
      pManager.AddTextParameter("Name", "N", "Name of the unit.", GH_ParamAccess.item, "Custom");
      pManager.AddColourParameter("Colour", "C", "Colour of the unit while it falls.", GH_ParamAccess.item, Color.FromArgb(255, 122, 69));
      pManager.AddNumberParameter("Weight", "W", "How often it is dealt: 1 = once per round of pieces, 0.5 = every other round.", GH_ParamAccess.item, 1.0);
      for (int i = 0; i < 4; i++) pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Unit", "U", "Voxel unit; connect to Unit Library.", GH_ParamAccess.item);
      pManager.AddMeshParameter("Voxels", "V", "The voxels as a mesh, in place.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Count", "N", "Number of voxels.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Orientations", "O", "Distinct ways the unit can be turned (24 for a shape with no symmetry).", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var geometry = new List<IGH_GeometricGoo>();
      var points = new List<Point3d>();
      string pattern = null, name = "Custom";
      double size = 0, weight = 1;
      var colour = Color.FromArgb(255, 122, 69);
      DA.GetDataList(0, geometry);
      DA.GetDataList(1, points);
      DA.GetData(2, ref pattern);
      bool hasSize = DA.GetData(3, ref size);
      DA.GetData(4, ref name);
      DA.GetData(5, ref colour);
      DA.GetData(6, ref weight);
      if (hasSize && (!(size > 0) || double.IsInfinity(size)))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size must be a positive finite number.");
        return;
      }
      if (!(weight > 0)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Weight is not positive, so this unit is never dealt.");
      else if (weight > PieceDef.MaxWeight) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Weight is capped at {PieceDef.MaxWeight} (copies per round of pieces).");

      List<Int3> cells;
      Plane frame;
      geometry.RemoveAll(g => g == null);
      if (geometry.Count > 0)
      {
        var meshes = new List<Mesh>();
        foreach (var goo in geometry)
        {
          var mesh = Inputs3D.ToMesh(goo, out bool closed);
          if (mesh == null)
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cannot use {goo.TypeName}; give closed breps or meshes.");
            return;
          }
          if (!closed) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A solid is not closed; the inside test may miss voxels.");
          meshes.Add(mesh);
        }
        if (!hasSize) size = StackUnit.AutoSize(meshes);
        cells = StackUnit.Voxelize(meshes, size, out frame);
        if (cells == null)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The sketch is far more than 4 voxels of {size:G4} on a side; give a larger Size.");
          return;
        }
      }
      else if (points.Count > 0)
      {
        // Some slack over MaxCells for duplicate points; anything larger is a point cloud, refused before the O(n²) spacing pass.
        if (points.Count > MaxPoints)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{points.Count} points: a unit has at most {PieceDef.MaxCells} voxel centres.");
          return;
        }
        if (!hasSize) size = ClosestSpacing(points);
        if (!(size > 0))
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Give a Size: the points do not show a spacing.");
          return;
        }
        var min = new Point3d(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)) - new Vector3d(size, size, size) / 2;
        frame = new Plane(min, Vector3d.XAxis, Vector3d.YAxis);
        cells = points.Select(p => new Int3((int)Math.Floor((p.X - min.X) / size), (int)Math.Floor((p.Y - min.Y) / size), (int)Math.Floor((p.Z - min.Z) / size))).Distinct().ToList();
      }
      else if (!string.IsNullOrWhiteSpace(pattern))
      {
        if (!hasSize) size = 1;
        cells = Polycube.ParsePattern(pattern).ToList();
        frame = Plane.WorldXY;
        if (!new HashSet<Int3>(cells).SetEquals(LegacyPattern(pattern)))
          AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
            "Spaces and blank rows in the pattern now count as empty cells, so this pattern gives a different unit than Moyu 1.0.0 did. Remove any spaces that are only padding.");
      }
      else
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give Geometry, Points or a Pattern.");
        return;
      }

      string why = PieceDef.Validate(cells, 10, 10, 20);
      if (why != null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, why);
        return;
      }
      // PieceDef shifts the cells to start at the origin; move the frame along so the preview stays in place.
      var low = new Int3(cells.Min(c => c.X), cells.Min(c => c.Y), cells.Min(c => c.Z));
      frame.Origin += (frame.XAxis * low.X + frame.YAxis * low.Y + frame.ZAxis * low.Z) * size;

      var piece = new PieceDef(string.IsNullOrWhiteSpace(name) ? "Custom" : name, cells, colour.ToArgb(), weight, isCustom: true);
      var unit = new StackUnit(piece, frame, size);
      Message = $"{piece.Cells.Length} voxels · ×{piece.ScoreMultiplier:0.0#}";
      DA.SetData(0, new MoyuGoo<StackUnit>(unit));
      DA.SetData(1, unit.ToMesh());
      DA.SetData(2, piece.Cells.Length);
      DA.SetData(3, piece.OrientationCount);
    }

    static double ClosestSpacing(List<Point3d> points)
    {
      double best = double.MaxValue;
      for (int i = 0; i < points.Count; i++)
        for (int j = i + 1; j < points.Count; j++)
        {
          double d = points[i].DistanceTo(points[j]);
          if (d > 1e-9 && d < best) best = d;
        }
      return best == double.MaxValue ? 0 : best;
    }

    /// <summary>
    /// How Moyu 1.0.0 read a pattern (every row trimmed, blank rows dropped). Only used to point out patterns whose shape
    /// changed when spaces started to count as empty cells.
    /// </summary>
    static Int3[] LegacyPattern(string pattern)
    {
      var cells = new List<Int3>();
      var layers = pattern.Split('|');
      for (int z = 0; z < layers.Length; z++)
      {
        var rows = layers[z].Split(new[] { '/', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        for (int y = 0; y < rows.Length; y++)
        {
          var row = rows[y].Trim();
          for (int x = 0; x < row.Length; x++)
            if ("#xXoO1█".IndexOf(row[x]) >= 0) cells.Add(new Int3(x, y, z));
        }
      }
      return Polycube.Normalize(cells);
    }

    public override Guid ComponentGuid => new Guid("b8e2c4d1-7a93-4f56-8c0e-3e5a1f6d2b47");
  }
}
