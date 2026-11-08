using System;
using Grasshopper.Kernel;
using Moyu.Core;
using Moyu.TwoD;
using Rhino.Geometry;

namespace Moyu.Components.TwoD
{
  public class AreaComponent : MoyuComponent
  {
    public AreaComponent()
      : base("Area 2D", "Area",
        "Non-design region inside the boundary: always solid (e.g. a slab) or always void (e.g. an opening). Later areas override earlier ones.",
        Panels.TwoD)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddCurveParameter("Region", "R", "Closed curve in the domain plane.", GH_ParamAccess.item);
      pManager.AddBooleanParameter("Solid", "S", "True: keep the region filled with material. False: keep it empty.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Area", "A", "Passive area for the 2D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      Curve region = null;
      bool solid = false;
      if (!DA.GetData(0, ref region)) return;
      DA.GetData(1, ref solid);

      if (!region.IsClosed)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Region must be a closed curve.");
        return;
      }
      Message = solid ? "Solid" : "Void";
      DA.SetData(0, new MoyuGoo<Area2D>(new Area2D(region.DuplicateCurve(), solid ? ElementKind.Solid : ElementKind.Void)));
    }

    public override Guid ComponentGuid => new Guid("efb05a6a-ac41-409d-8d6a-2a5cf3ae194c");
  }
}
