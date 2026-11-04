using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.ThreeD;
using Rhino.Geometry;

namespace Moyu.Components.ThreeD
{
  public class Boundary3DComponent : MoyuComponent
  {
    public Boundary3DComponent()
      : base("Boundary 3D", "Bnd3D",
        "Design domain: closed solids (breps or meshes) voxelised into a cube grid. A solid nested inside another becomes a hole.",
        "3D")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGeometryParameter("Boundary", "B", "Closed breps or meshes enclosing the design region. Nested solids are holes.", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Resolution", "R", "Number of voxels along the longest side. Solve time grows roughly with the cube of this.", GH_ParamAccess.item, 32);
      pManager.AddPlaneParameter("Plane", "P", "Optional grid orientation. Default: World XY.", GH_ParamAccess.item);
      pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Domain", "D", "Voxel design domain for the 3D solver.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Voxel Size", "S", "Edge length of one voxel in model units.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var geometry = new List<IGH_GeometricGoo>();
      int resolution = 32;
      var plane = Plane.Unset;
      if (!DA.GetDataList(0, geometry)) return;
      DA.GetData(1, ref resolution);
      bool hasPlane = DA.GetData(2, ref plane);

      if (resolution > 160)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Resolution capped at 160 voxels.");
        resolution = 160;
      }

      var meshes = new List<Mesh>();
      foreach (var goo in geometry)
      {
        if (goo == null) continue;
        var mesh = Inputs3D.ToMesh(goo, out bool closed);
        if (mesh == null)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cannot use {goo.TypeName} as a boundary; give a closed brep or mesh.");
          return;
        }
        if (!closed) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A boundary is not closed; voxels may leak through its gaps.");
        meshes.Add(mesh);
      }

      var domain = VoxelDomain3D.Create(meshes, resolution, hasPlane ? plane : (Plane?)null, out string error);
      if (domain == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        return;
      }
      int voxels = domain.Grid.ElementCount;
      if (voxels > 1000000)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
          $"{voxels} voxels: expect tens of seconds to minutes per iteration and gigabytes of memory; 64–96 along the longest side is usually plenty.");
      else if (voxels > 150000)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{voxels} voxels: each iteration may take several seconds.");

      Message = $"{domain.Grid.Nx} × {domain.Grid.Ny} × {domain.Grid.Nz}";
      DA.SetData(0, new MoyuGoo<VoxelDomain3D>(domain));
      DA.SetData(1, domain.VoxelSize);
    }

    public override Guid ComponentGuid => new Guid("c53783f5-76d5-4b4d-a59b-663ac770091e");
  }
}
