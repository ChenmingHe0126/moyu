using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Core;
using Moyu.ThreeD;

namespace Moyu.Components.ThreeD
{
  public class Area3DComponent : MoyuComponent
  {
    public Area3DComponent()
      : base("Area 3D", "Area",
        "Non-design region inside the boundary: always solid (e.g. a slab) or always empty (e.g. a passage). Later areas override earlier ones.",
        "3D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Region", "R", "Closed brep or mesh.", GH_ParamAccess.item);
      pManager.AddBooleanParameter("Solid", "S", "True: keep the region filled with material. False: keep it empty.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Area", "A", "Passive region for the 3D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      IGH_GeometricGoo geometry = null;
      bool solid = false;
      if (!DA.GetData(0, ref geometry)) return;
      DA.GetData(1, ref solid);

      var mesh = Inputs3D.ToMesh(geometry, out bool closed);
      if (mesh == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cannot use {geometry?.TypeName} as a region; give a closed brep or mesh.");
        return;
      }
      if (!closed) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The region is not closed; voxels may leak through its gaps.");
      Message = solid ? "Solid" : "Void";
      DA.SetData(0, new MoyuGoo<Area3D>(new Area3D(mesh, solid ? ElementKind.Solid : ElementKind.Void)));
    }

    public override Guid ComponentGuid => new Guid("5b06ab3e-bc65-422f-be54-23933b2a5fa8");
  }
}
