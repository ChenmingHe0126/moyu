using System;
using Grasshopper.Kernel;
using Moyu.Games.Snake;
using Rhino;

namespace Moyu.Components.Landscape
{
  /// <summary>Ready-made site surfaces for Path Study: terrain, torus, tube, saddle roof and sphere.</summary>
  public class SiteSurfaceComponent : MoyuComponent
  {
    /// <summary>Default site size: 40 m.</summary>
    public const double DefaultMetres = 40;

    public SiteSurfaceComponent()
      : base("Site Surface", "Site",
        "Generates a site surface to study: 0 rolling terrain, 1 torus, 2 tube, 3 saddle roof, 4 sphere.",
        Panels.Landscape)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override bool NativePreview => true;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddIntegerParameter("Type", "T", "0 rolling terrain, 1 torus, 2 tube, 3 saddle roof, 4 sphere.", GH_ParamAccess.item, 0);
      pManager.AddNumberParameter("Size", "S", "Overall size in model units. Default: 40 m.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Seed", "Sd", "Varies the terrain and the tube's route.", GH_ParamAccess.item, 1);
      pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddSurfaceParameter("Surface", "S", "The site surface.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      int type = 0, seed = 1;
      double size = DefaultSize();
      DA.GetData(0, ref type);
      DA.GetData(1, ref size);
      DA.GetData(2, ref seed);
      if (type < 0 || type >= SiteSurfaces.Names.Length)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Type must be 0 (terrain), 1 (torus), 2 (tube), 3 (saddle roof) or 4 (sphere).");
        return;
      }
      if (!(size > 0))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size must be positive.");
        return;
      }
      var brep = SiteSurfaces.Create(type, size, seed);
      if (brep == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not build the surface.");
        return;
      }
      Message = SiteSurfaces.Names[type];
      DA.SetData(0, brep);
    }

    public static double DefaultSize()
    {
      var doc = RhinoDoc.ActiveDoc;
      return doc == null ? DefaultMetres : DefaultMetres * RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
    }

    public override Guid ComponentGuid => new Guid("2f7d0cbc-3355-4c84-acf5-930e93c82e1a");
  }
}
