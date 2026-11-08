using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.ThreeD;

namespace Moyu.Components.ThreeD
{
  public class Support3DComponent : MoyuComponent
  {
    public Support3DComponent()
      : base("Support 3D", "Support",
        "Fixed displacement on the voxel domain. A point fixes the nearest node; a curve or surface fixes every node it touches.",
        "3D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Geometry", "G", "Point, curve, surface, brep or mesh to hold in place.", GH_ParamAccess.item);
      pManager.AddBooleanParameter("Fix X", "X", "Fix movement along the domain X axis.", GH_ParamAccess.item, true);
      pManager.AddBooleanParameter("Fix Y", "Y", "Fix movement along the domain Y axis.", GH_ParamAccess.item, true);
      pManager.AddBooleanParameter("Fix Z", "Z", "Fix movement along the domain Z axis.", GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Support", "S", "Support condition for the 3D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      IGH_GeometricGoo geometry = null;
      bool fixX = true, fixY = true, fixZ = true;
      if (!DA.GetData(0, ref geometry)) return;
      DA.GetData(1, ref fixX);
      DA.GetData(2, ref fixY);
      DA.GetData(3, ref fixZ);

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
      if (!fixX && !fixY && !fixZ) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No direction is fixed; this support does nothing.");

      Message = fixX && fixY && fixZ ? "Fixed" : $"Fix {(fixX ? "X" : "")}{(fixY ? "Y" : "")}{(fixZ ? "Z" : "")}";
      DA.SetData(0, new MoyuGoo<Support3D>(new Support3D(anchor, fixX, fixY, fixZ)));
    }

    public override Guid ComponentGuid => new Guid("f28a4501-8822-452d-a95c-f8514dad6ece");
  }
}
