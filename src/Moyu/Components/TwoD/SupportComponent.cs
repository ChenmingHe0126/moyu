using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.TwoD;

namespace Moyu.Components.TwoD
{
  public class SupportComponent : MoyuComponent
  {
    public SupportComponent()
      : base("Support 2D", "Support",
        "Fixed displacement on the design domain. A point fixes the nearest node; a curve fixes every node it passes.",
        "2D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Geometry", "G", "Point or curve to hold in place.", GH_ParamAccess.item);
      pManager.AddBooleanParameter("Fix X", "X", "Fix movement along the domain plane X axis.", GH_ParamAccess.item, true);
      pManager.AddBooleanParameter("Fix Y", "Y", "Fix movement along the domain plane Y axis.", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Support", "S", "Support condition for the 2D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      IGH_GeometricGoo geometry = null;
      bool fixX = true, fixY = true;
      if (!DA.GetData(0, ref geometry)) return;
      DA.GetData(1, ref fixX);
      DA.GetData(2, ref fixY);

      var anchor = ToAnchor(geometry);
      if (anchor == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Expected a point or curve, got {geometry?.TypeName}.");
        return;
      }
      if (anchor.Point.HasValue && !anchor.Point.Value.IsValid)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The point is not valid (unset, NaN or infinite).");
        return;
      }
      if (!fixX && !fixY) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Neither X nor Y is fixed; this support does nothing.");

      Message = fixX && fixY ? "Pin" : fixX ? "Roller X" : fixY ? "Roller Y" : "Free";
      DA.SetData(0, new MoyuGoo<Support2D>(new Support2D(anchor, fixX, fixY)));
    }

    public override Guid ComponentGuid => new Guid("179c263a-c613-4447-84b7-2cd55a730e17");
  }
}
