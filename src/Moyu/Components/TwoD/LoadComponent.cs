using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.TwoD;
using Rhino.Geometry;

namespace Moyu.Components.TwoD
{
  public class LoadComponent : MoyuComponent
  {
    public LoadComponent()
      : base("Load 2D", "Load",
        "Force on the design domain. A point loads the nearest node; a curve spreads the force evenly over the nodes it passes.",
        "2D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Geometry", "G", "Point or curve where the force acts.", GH_ParamAccess.item);
      pManager.AddVectorParameter("Force", "F", "Total force vector in world space. Only the part lying in the domain plane is used.", GH_ParamAccess.item, new Vector3d(0, -1, 0));
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Load", "L", "Load condition for the 2D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      IGH_GeometricGoo geometry = null;
      var force = Vector3d.Zero;
      if (!DA.GetData(0, ref geometry) || !DA.GetData(1, ref force)) return;

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
      // IsZero is false for NaN, which would otherwise surface in the solver as "not stable".
      if (!force.IsValid)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Force is not a valid vector (unset, NaN or infinite).");
        return;
      }
      if (force.IsZero)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Force is zero.");
        return;
      }
      DA.SetData(0, new MoyuGoo<Load2D>(new Load2D(anchor, force)));
    }

    public override Guid ComponentGuid => new Guid("715dd06c-3618-405d-9f17-95d32d9f1652");
  }
}
