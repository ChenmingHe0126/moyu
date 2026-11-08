using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Moyu.Games.Swift;
using Moyu.Games.Swift.Core;
using Rhino.Geometry;

namespace Moyu.Components.Section
{
  /// <summary>Course settings for Airflow Study: wind exposure, mode, opening and bay factors, and an optional fixed seed.</summary>
  public class StoreyGridComponent : MoyuComponent
  {
    public StoreyGridComponent()
      : base("Storey Grid", "Grid",
        "Sets out the section grid for Airflow Study: wind exposure class, opening height and bay width factors, and a fixed seed for a repeatable layout.",
        Panels.Section)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override bool NativePreview => true;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddIntegerParameter("Exposure", "E", "Wind exposure: 0 sheltered, 1 normal, 2 exposed.", GH_ParamAccess.item, 1);
      pManager.AddIntegerParameter("Mode", "M", "0 classic (a touch ends the flight), 1 Zen (bump through and carry on).", GH_ParamAccess.item, 0);
      pManager.AddNumberParameter("Opening", "O", "Opening height factor (0.8–1.4).", GH_ParamAccess.item, 1);
      pManager.AddNumberParameter("Bay", "B", "Bay width factor (0.8–1.5).", GH_ParamAccess.item, 1);
      pManager.AddIntegerParameter("Seed", "S", "Fixed seed for the same layout every time; 0 for a new one each run.", GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddGenericParameter("Grid", "G", "Course settings for Airflow Study.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Section", "Sc", "Preview of the floor, ceiling and first walls on the World XZ plane at 0.75 m units.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      int exposure = 1, mode = 0, seed = 0;
      double opening = 1, bay = 1;
      DA.GetData(0, ref exposure);
      DA.GetData(1, ref mode);
      DA.GetData(2, ref opening);
      DA.GetData(3, ref bay);
      DA.GetData(4, ref seed);
      if (exposure < 0 || exposure > 2 || mode < 0 || mode > 1)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Exposure must be 0–2 and Mode 0 or 1.");
        return;
      }
      // Math.Min / Max pass NaN through, so a NaN factor falls back to the standard course first.
      bool nan = double.IsNaN(opening) || double.IsNaN(bay);
      if (double.IsNaN(opening)) opening = 1;
      if (double.IsNaN(bay)) bay = 1;
      double o = Math.Max(0.8, Math.Min(1.4, opening)), b = Math.Max(0.8, Math.Min(1.5, bay));
      if (nan || o != opening || b != bay) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Factors kept in range: opening ×{o:F2}, bay ×{b:F2}.");
      var settings = new SwiftSettings { Exposure = (Exposure)exposure, Mode = (SwiftMode)mode, Opening = o, Bay = b, Seed = seed != 0 ? seed : 1 };
      var spec = new FlightSpec(settings, seed);
      DA.SetData(0, new MoyuGoo<FlightSpec>(spec));
      Message = $"{settings.Exposure}" + (settings.Mode == SwiftMode.Zen ? " · Zen" : "");

      // Preview: floor, ceiling and the first walls of this layout.
      var game = new SwiftGame(settings);
      var plane = AirflowStudyComponent.DefaultPlane;
      double size = AirflowStudyComponent.DefaultSize();
      Point3d At(double x, double y) => plane.Origin + (plane.XAxis * x + plane.YAxis * y) * size;
      var curves = new List<Curve>
      {
        new LineCurve(At(0, 0), At(50, 0)),
        new LineCurve(At(0, SwiftGame.Height), At(50, SwiftGame.Height)),
      };
      for (int i = 0; i < 4 && i < game.Walls.Count; i++)
      {
        var w = game.Walls[i];
        double lo = w.Centre - w.Gap / 2, hi = w.Centre + w.Gap / 2;
        curves.Add(new PolylineCurve(new[] { At(w.X, 0), At(w.Right, 0), At(w.Right, lo), At(w.X, lo), At(w.X, 0) }));
        curves.Add(new PolylineCurve(new[] { At(w.X, hi), At(w.Right, hi), At(w.Right, SwiftGame.Height), At(w.X, SwiftGame.Height), At(w.X, hi) }));
      }
      DA.SetDataList(1, curves);
    }

    public override Guid ComponentGuid => new Guid("14ceb1e1-aa5e-4512-8e8c-1fb4be8a1459");
  }
}
