using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Moyu.Games.Breakout;
using Moyu.Games.Breakout.Core;
using Rhino.Geometry;

namespace Moyu.Components.Facade
{
  /// <summary>Brick bonds for Facade Study: one real bond, all of them in turn, or panels drawn as closed curves.</summary>
  public class BrickBondComponent : MoyuComponent
  {
    public BrickBondComponent()
      : base("Brick Bond", "Bond",
        "Lays out a brick facade: stretcher, stack, English or Flemish bond, a perforated screen or an arched opening; or panels from closed curves.",
        "Facade")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override bool NativePreview => true;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddIntegerParameter("Type", "T", "-1 all bonds in turn, 0 stretcher, 1 stack, 2 English, 3 Flemish, 4 screen wall, 5 arch.", GH_ParamAccess.item, -1);
      pManager.AddCurveParameter("Curves", "C", "Closed curves to use as panels instead of a bond (their bounding rectangles, in their own plane).", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Courses", "N", "Courses in the wall (6–14).", GH_ParamAccess.item, 10);
      pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Wall", "W", "The wall for Facade Study.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Bricks", "B", "Preview of the first wall on the World XZ plane at 2.4 m modules.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      int type = -1, courses = 10;
      var curves = new List<Curve>();
      DA.GetData(0, ref type);
      DA.GetDataList(1, curves);
      DA.GetData(2, ref courses);
      if (type < -1 || type > 5)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Type must be -1 (all in turn) or 0–5.");
        return;
      }
      courses = Math.Max(6, Math.Min(14, courses));

      CustomWall custom = null;
      var closed = curves.Where(c => c != null && c.IsClosed).ToList();
      if (curves.Count > 0 && closed.Count < curves.Count)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{curves.Count - closed.Count} open curve(s) ignored; panels must be closed.");
      if (closed.Count > 0)
      {
        // Measure the panels in the plane they were drawn in, on an upright frame there (up the plane's slope, or
        // World Y on a level plane), so a facade off the world axes keeps its true panel sizes.
        var points = closed.SelectMany(c => c.ToNurbsCurve()?.Points.Select(p => p.Location) ?? Enumerable.Empty<Point3d>()).ToList();
        Vector3d normal;
        if (Plane.FitPlaneToPoints(points, out Plane fit) != PlaneFitResult.Failure) normal = fit.ZAxis;
        else
        {
          // No plane through the points at all: take the thinnest world axis as the normal.
          var span = new BoundingBox(points).Diagonal;
          double sx = Math.Abs(span.X), sy = Math.Abs(span.Y), sz = Math.Abs(span.Z);
          normal = sz <= sx && sz <= sy ? Vector3d.ZAxis : sy <= sx ? Vector3d.YAxis : Vector3d.XAxis;
        }
        var up = Math.Abs(normal.Z) > 0.99 ? Vector3d.YAxis - normal * normal.Y : Vector3d.ZAxis - normal * normal.Z;
        up.Unitize();
        var along = Vector3d.CrossProduct(up, normal);
        // Left to right along +X (+Y on a YZ wall), as on the world planes; the flip falls at an odd angle, not on an axis or a diagonal.
        if (along.X * 2 + along.Y < 0) along = -along;
        var frame = new Plane(Point3d.Origin, along, up);
        custom = new CustomWall();
        foreach (var c in closed)
        {
          // In frame coordinates: X along the wall, Y up.
          var b = c.GetBoundingBox(frame);
          double u0 = b.Min.X, u1 = b.Max.X, v0 = b.Min.Y, v1 = b.Max.Y;
          if (u1 - u0 > 1e-9 && v1 - v0 > 1e-9) custom.Rects.Add((u0, v0, u1, v1));
        }
        Message = $"Custom · {custom.Rects.Count}";
      }
      else Message = type < 0 ? "All bonds" : Bonds.Names[type].Split(' ')[0];

      var spec = new WallSpec(type < 0 || custom != null ? (BondType?)null : (BondType)type, custom, courses);
      DA.SetData(0, new MoyuGoo<WallSpec>(spec));

      double size = FacadeStudyComponent.DefaultSize();
      var plane = FacadeStudyComponent.DefaultPlane;
      int tooSmall = 0;
      var bricks = custom != null ? Bonds.FitCustom(custom, 13, 17 - Bonds.Headroom, 17 * 0.45, out tooSmall) : Bonds.Lay(type < 0 ? BondType.Stretcher : (BondType)type, 1, 13, 17 - Bonds.Headroom, courses, new Random(1));
      if (custom != null && bricks.Count == 0)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No panel is large enough to play on; Facade Study will lay a bond instead.");
      else if (tooSmall > 0)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{tooSmall} panel(s) too small to play (narrower than a ball once scaled into the field) ignored.");
      Point3d At(double x, double y) => plane.Origin + (plane.XAxis * x + plane.YAxis * y) * size;
      DA.SetDataList(1, bricks.Select(b => new PolylineCurve(new[] { At(b.X0, b.Y0), At(b.X1, b.Y0), At(b.X1, b.Y1), At(b.X0, b.Y1), At(b.X0, b.Y0) })));
    }

    public override Guid ComponentGuid => new Guid("20176648-5aa5-4679-959c-cb355cd79773");
  }
}
