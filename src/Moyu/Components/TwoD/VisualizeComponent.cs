using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.TwoD;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Components.TwoD
{
  public class VisualizeComponent : MoyuComponent
  {
    static readonly Color ContourColor = Color.FromArgb(0, 122, 204);

    // Rebuilt only when the result or a display input changes, so a ticking Timer stays cheap.
    Result2D _cacheResult;
    string _cacheKey;
    Mesh _cacheMesh;
    List<Curve> _cacheContours;

    // What the viewport shows; cleared before every solution so a failed upstream leaves nothing stale.
    Result2D _result;
    Mesh _mesh;
    List<Curve> _contours = new List<Curve>();
    bool _showConditions;

    public VisualizeComponent()
      : base("Visualize 2D", "Vis",
        "Shows a 2D optimisation result: density pixels, iso-contours and the loads and supports that were applied.",
        Panels.TwoD)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Result", "R", "Result from SIMP Solver 2D.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Mode", "M", "0 = density pixels, 1 = solid pixels above the threshold, 2 = smooth shading.", GH_ParamAccess.item, 0);
      pManager.AddNumberParameter("Threshold", "T", "Density treated as solid for mode 1 and the contour.", GH_ParamAccess.item, 0.5);
      pManager.AddColourParameter("Solid Colour", "Cs", "Colour of full density.", GH_ParamAccess.item, Color.FromArgb(25, 25, 25));
      pManager.AddColourParameter("Void Colour", "Cv", "Colour of zero density.", GH_ParamAccess.item, Color.FromArgb(240, 240, 240));
      pManager.AddBooleanParameter("Conditions", "Cn", "Draw the applied loads (red) and supports (blue).", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddMeshParameter("Mesh", "M", "Coloured pixel mesh.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Contour", "C", "Closed outlines at the threshold density, ready for surfacing or offsetting.", GH_ParamAccess.list);
      pManager.AddNumberParameter("Density", "D", "Density per pixel, row by row from the lower-left corner (0 outside the boundary).", GH_ParamAccess.list);
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
      _contours = new List<Curve>();
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Visualize 2D shows one result; extra results are ignored.");
        return;
      }
      IGH_Goo goo = null;
      int mode = 0;
      double threshold = 0.5;
      Color solid = Color.Black, empty = Color.White;
      bool conditions = true;
      DA.GetData(0, ref goo);
      DA.GetData(1, ref mode);
      DA.GetData(2, ref threshold);
      DA.GetData(3, ref solid);
      DA.GetData(4, ref empty);
      DA.GetData(5, ref conditions);

      if (!MoyuGoo<Result2D>.TryGet(goo, out var result))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, goo == null ? "Result input needs the output of SIMP Solver 2D." : WrongInput("Result", goo, "SIMP Solver 2D"));
        return;
      }
      if (mode < 0 || mode > 2)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Mode must be 0, 1 or 2; using 0.");
        mode = 0;
      }
      threshold = Math.Min(0.99, Math.Max(0.01, threshold));

      string key = $"{result.Revision}|{mode}|{threshold}|{solid.ToArgb()}|{empty.ToArgb()}";
      if (!ReferenceEquals(result, _cacheResult) || key != _cacheKey)
      {
        _cacheMesh = ResultMesher.BuildMesh(result, (DisplayMode)mode, threshold, solid, empty);
        _cacheContours = ResultMesher.BuildContours(result, threshold);
        _cacheResult = result;
        _cacheKey = key;
      }
      _result = result;
      _mesh = _cacheMesh;
      _contours = _cacheContours;
      _showConditions = conditions;

      Message = ((DisplayMode)mode).ToString();
      DA.SetData(0, _mesh);
      DA.SetDataList(1, _contours);
      DA.SetDataList(2, result.Density);
    }

    public override BoundingBox ClippingBox => _result?.Domain.Bounds ?? BoundingBox.Empty;

    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
      if (_mesh != null && _mesh.Faces.Count > 0) args.Display.DrawMeshFalseColors(_mesh);
    }

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
      if (_result == null) return;
      var d = args.Display;
      foreach (var pl in _result.Domain.Outlines) d.DrawPolyline(pl, Glyphs.DomainColor, 1);
      var contourColor = Attributes.Selected ? args.WireColour_Selected : ContourColor;
      foreach (var c in _contours) d.DrawCurve(c, contourColor, 2);
      if (!_showConditions) return;

      foreach (var load in _result.Loads)
        foreach (var p in Thin(load.Nodes, 24))
          Glyphs.Arrow(d, args.Viewport, p, load.Force, Glyphs.LoadColor);
      foreach (var s in _result.Supports)
        foreach (var p in s.Nodes)
          Glyphs.Support(d, p, s.FixX, s.FixY, Glyphs.SupportColor);
    }

    /// <summary>Evenly spaced subset so long curve loads don't turn into a wall of arrows.</summary>
    static IEnumerable<Point3d> Thin(Point3d[] pts, int max)
    {
      if (pts.Length <= max) return pts;
      double step = (pts.Length - 1) / (double)(max - 1);
      return Enumerable.Range(0, max).Select(k => pts[(int)Math.Round(k * step)]);
    }

    public override Guid ComponentGuid => new Guid("2a76e4f3-1e42-46e8-94e3-d00c400409f7");
  }
}
