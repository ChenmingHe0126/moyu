using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.ThreeD;
using Rhino.Geometry;

namespace Moyu.Components.ThreeD
{
  public class Load3DComponent : MoyuComponent
  {
    public Load3DComponent()
      : base("Load 3D", "Load",
        "Force on the voxel domain. A point loads the nearest node; a curve or surface spreads the force evenly over the nodes it touches.",
        "3D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Geometry", "G", "Point, curve, surface, brep or mesh where the force acts.", GH_ParamAccess.item);
      pManager.AddVectorParameter("Force", "F", "Total force vector in world space.", GH_ParamAccess.item, new Vector3d(0, 0, -1));
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Load", "L", "Load condition for the 3D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      IGH_GeometricGoo geometry = null;
      var force = Vector3d.Zero;
      if (!DA.GetData(0, ref geometry) || !DA.GetData(1, ref force)) return;

      var anchor = Inputs3D.ToAnchor(geometry);
      if (anchor == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Expected a point, curve, surface or mesh, got {geometry?.TypeName}.");
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
      Message = anchor.ToString();
      DA.SetData(0, new MoyuGoo<Load3D>(new Load3D(anchor, force)));
    }

    public override Guid ComponentGuid => new Guid("c14e2661-33d8-4273-bf95-c0159fc6118e");
  }
}
