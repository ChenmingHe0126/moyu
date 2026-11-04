using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Moyu.Games.Stack;
using Moyu.Games.Stack.Core;

namespace Moyu.Components.Massing
{
  /// <summary>Combines a built-in unit set with custom Voxel Units into the library Massing Study deals from.</summary>
  public class UnitLibraryComponent : MoyuComponent
  {
    public UnitLibraryComponent()
      : base("Unit Library", "Lib",
        "The units Massing Study deals: a built-in set (Easy, Standard, Hard) plus any custom Voxel Units.",
        "Massing")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Units", "U", "Custom units from Voxel Unit.", GH_ParamAccess.list);
      pManager.AddIntegerParameter("Preset", "P", "0 = Easy (3- and flat 4-voxel units), 1 = Standard (all eight 4-voxel units), 2 = Hard (adds 5-voxel units), -1 = custom units only.", GH_ParamAccess.item, 1);
      pManager[0].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Library", "L", "Connect to Massing Study.", GH_ParamAccess.item);
      pManager.AddTextParameter("Units", "N", "Names of the units in the library.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      int preset = PieceSets.Standard;
      DA.GetData(1, ref preset);
      if (preset < -1 || preset > 2)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Preset must be -1 (custom only), 0 (Easy), 1 (Standard) or 2 (Hard).");
        return;
      }
      var errors = new List<string>();
      var custom = Collect<StackUnit>(DA, 0, "Units", errors);
      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      if (errors.Count > 0) return;

      var pieces = new List<PieceDef>();
      if (preset >= 0) pieces.AddRange(PieceSets.Get(preset));
      foreach (var u in custom)
      {
        var twin = pieces.FirstOrDefault(p => Polycube.SameShape(p.Cells, u.Piece.Cells));
        if (twin != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{u.Piece.Name} has the same shape as {twin.Name}; both are dealt.");
        pieces.Add(u.Piece);
      }
      if (pieces.Count == 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No units: connect Voxel Units or choose a preset.");
        return;
      }
      string description = (preset >= 0 ? PieceSets.Names[preset] : "Custom") + (preset >= 0 && custom.Count > 0 ? $" + {custom.Count}" : "");
      Message = description;
      DA.SetData(0, new MoyuGoo<UnitLibrary>(new UnitLibrary(pieces, description)));
      DA.SetDataList(1, pieces.Select(p => p.Name));
    }

    public override Guid ComponentGuid => new Guid("4c9a1e73-58d2-4b0f-a6e1-91f3d0c7b528");
  }
}
