using System;
using Grasshopper.Kernel;
using Moyu.ThreeD;

namespace Moyu.Components.ThreeD
{
  public class Material3DComponent : MoyuComponent
  {
    public Material3DComponent()
      : base("Material 3D", "Mat",
        "Isotropic linear-elastic material. E only scales compliance; ν changes the layout slightly.",
        "3D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddNumberParameter("Young's Modulus", "E", "Stiffness of solid material, in any consistent unit.", GH_ParamAccess.item, 1.0);
      pManager.AddNumberParameter("Poisson Ratio", "Nu", "Lateral contraction ratio, between -1 and 0.5 (steel ≈ 0.3).", GH_ParamAccess.item, 0.3);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Material", "M", "Material for the 3D solver.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      double e = 1.0, nu = 0.3;
      DA.GetData(0, ref e);
      DA.GetData(1, ref nu);
      // An infinite E (e.g. a Panel holding 1e400) would reach the solver as NaN stiffness and read as "not stable".
      if (!(e > 0.0) || double.IsInfinity(e))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Young's modulus must be a positive, finite number.");
        return;
      }
      if (!(nu > -1.0 && nu < 0.5))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Poisson ratio must lie between -1 and 0.5.");
        return;
      }
      DA.SetData(0, new MoyuGoo<Material3D>(new Material3D(e, nu)));
    }

    public override Guid ComponentGuid => new Guid("41f6f74f-2c53-4bc3-8dc4-2f1d6072ce91");
  }
}
