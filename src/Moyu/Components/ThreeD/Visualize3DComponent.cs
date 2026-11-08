using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.ThreeD;
using Moyu.TwoD;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Components.ThreeD
{
  public class Visualize3DComponent : MoyuComponent
  {
    // Rebuilt only when the result or a display input changes, so a ticking Timer stays cheap.
    Result3D _cacheResult;
    string _cacheKey;
    Mesh _cacheMesh, _cacheSurface;

    // What the viewport shows; cleared before every solution so a failed upstream leaves nothing stale.
    Result3D _result;
    Mesh _mesh;
    DisplayMaterial _material;
    bool _showConditions;

    public Visualize3DComponent()
      : base("Visualize 3D", "Vis",
        "Shows a 3D optimisation result as voxels or a smooth surface, with the loads and supports that were applied.",
        Panels.ThreeD)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Result", "R", "Result from SIMP Solver 3D.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Mode", "M", "0 = voxels at or above the threshold, 1 = smooth iso-surface.", GH_ParamAccess.item, 1);
      pManager.AddNumberParameter("Threshold", "T", "Density treated as solid.", GH_ParamAccess.item, 0.5);
      pManager.AddColourParameter("Colour", "C", "Display colour of the material.", GH_ParamAccess.item, Color.FromArgb(70, 110, 190));
      pManager.AddBooleanParameter("Conditions", "Cn", "Draw the applied loads (red) and supports (blue).", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddMeshParameter("Mesh", "M", "Mesh of the selected mode.", GH_ParamAccess.item);
      pManager.AddMeshParameter("Surface", "S", "Closed, smoothed iso-surface at the threshold, ready for further modelling.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Density", "D", "Density per voxel, x fastest, then y, then z (0 outside the boundary).", GH_ParamAccess.list);
    }

    protected override void BeforeSolveInstance()
    {
      ClearView();
      base.BeforeSolveInstance();
    }

    // Expiry clears the view too, in case a solution with no input data never reaches BeforeSolveInstance.
    public override void ClearData()
    {
      ClearView();
      base.ClearData();
    }

    void ClearView()
    {
      _result = null;
      _mesh = null;
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Visualize 3D shows one result; extra results are ignored.");
        return;
      }
      IGH_Goo goo = null;
      int mode = 1;
      double threshold = 0.5;
      Color colour = Color.SteelBlue;
      bool conditions = true;
      DA.GetData(0, ref goo);
      DA.GetData(1, ref mode);
      DA.GetData(2, ref threshold);
      DA.GetData(3, ref colour);
      DA.GetData(4, ref conditions);

      if (!MoyuGoo<Result3D>.TryGet(goo, out var result))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, goo == null ? "Result input needs the output of SIMP Solver 3D." : WrongInput("Result", goo, "SIMP Solver 3D"));
        return;
      }
      if (mode < 0 || mode > 1)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Mode must be 0 or 1; using 1.");
        mode = 1;
      }
      threshold = Math.Min(0.99, Math.Max(0.01, threshold));

      string key = $"{result.Revision}|{threshold}";
      if (!ReferenceEquals(result, _cacheResult) || key != _cacheKey)
      {
        _cacheMesh = ResultMesher3D.BuildVoxels(result, threshold);
        _cacheSurface = ResultMesher3D.BuildSurface(result, threshold);
        _cacheResult = result;
        _cacheKey = key;
      }
      _result = result;
      _mesh = (DisplayMode3D)mode == DisplayMode3D.Voxels ? _cacheMesh : _cacheSurface;
      // Compare by ARGB: a named colour never equals the plain one the material hands back.
      if (_material == null || _material.Diffuse.ToArgb() != colour.ToArgb())
      {
        _material?.Dispose();
        _material = new DisplayMaterial(colour);
      }
      _showConditions = conditions;

      Message = ((DisplayMode3D)mode).ToString();
      DA.SetData(0, _mesh);
      DA.SetData(1, _cacheSurface);
      DA.SetDataList(2, result.Density);
    }

    public override void RemovedFromDocument(GH_Document document)
    {
      _material?.Dispose();
      _material = null;
      base.RemovedFromDocument(document);
    }

    public override BoundingBox ClippingBox => _result?.Domain.Bounds ?? BoundingBox.Empty;

    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
      if (_mesh == null || _mesh.Faces.Count == 0 || _material == null) return;
      args.Display.DrawMeshShaded(_mesh, Attributes.Selected ? args.ShadeMaterial_Selected : _material);
    }

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
      if (_result == null) return;
      var d = args.Display;
      d.DrawBox(new Box(_result.Domain.Plane, new Interval(0, _result.Domain.Grid.Nx * _result.Domain.VoxelSize),
        new Interval(0, _result.Domain.Grid.Ny * _result.Domain.VoxelSize), new Interval(0, _result.Domain.Grid.Nz * _result.Domain.VoxelSize)),
        Glyphs.DomainColor, 1);
      if (!_showConditions) return;

      foreach (var load in _result.Loads)
        foreach (var p in Thin(load.Nodes, 32))
          Glyphs.Arrow(d, args.Viewport, p, load.Force, Glyphs.LoadColor);
      foreach (var s in _result.Supports)
      {
        bool all = s.FixX && s.FixY && s.FixZ;
        foreach (var p in Thin(s.Nodes, 400)) Glyphs.Support(d, p, all, all, Glyphs.SupportColor);
      }
    }

    /// <summary>Evenly spaced subset so large surface loads don't turn into a forest of arrows.</summary>
    static IEnumerable<Point3d> Thin(Point3d[] pts, int max)
    {
      if (pts.Length <= max) return pts;
      double step = (pts.Length - 1) / (double)(max - 1);
      return Enumerable.Range(0, max).Select(k => pts[(int)Math.Round(k * step)]);
    }

    public override Guid ComponentGuid => new Guid("b3991b95-9c98-460d-8cbc-456b09dd6122");
  }
}
