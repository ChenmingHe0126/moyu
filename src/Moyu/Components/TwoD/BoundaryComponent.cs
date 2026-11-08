using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Moyu.TwoD;
using Rhino.Geometry;

namespace Moyu.Components.TwoD
{
  public class BoundaryComponent : MoyuComponent
  {
    public BoundaryComponent()
      : base("Boundary 2D", "Bnd2D",
        "Design domain: closed planar curves rasterised into a square pixel grid. Nested curves become holes.",
        Panels.TwoD)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddCurveParameter("Boundary", "B", "Closed planar curves enclosing the design region. Nested curves are holes.", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Resolution", "R", "Number of pixels along the longer side of the boundary. Solve time grows quickly with this.", GH_ParamAccess.item, 80);
      pManager.AddPlaneParameter("Plane", "P", "Optional grid plane. Default: World XY / XZ / YZ when the curve lies in one, otherwise the curve plane.", GH_ParamAccess.item);
      pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Domain", "D", "Pixel design domain for the 2D solver.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Pixel Size", "S", "Edge length of one pixel in model units.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var curves = new List<Curve>();
      int resolution = 80;
      var plane = Plane.Unset;
      if (!DA.GetDataList(0, curves)) return;
      DA.GetData(1, ref resolution);
      bool hasPlane = DA.GetData(2, ref plane);

      if (resolution > 600)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Resolution capped at 600 pixels.");
        resolution = 600;
      }

      var domain = PixelDomain2D.Create(curves, resolution, hasPlane ? plane : (Plane?)null, out string error);
      if (domain == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        return;
      }
      if (domain.InsideCount > 40000)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{domain.InsideCount} pixels: each iteration may take a second or more.");

      Message = $"{domain.Grid.Nx} × {domain.Grid.Ny}";
      DA.SetData(0, new MoyuGoo<PixelDomain2D>(domain));
      DA.SetData(1, domain.PixelSize);
    }

    public override Guid ComponentGuid => new Guid("bea6c6f3-581f-47c7-859b-ffadd46d11b1");
  }
}
