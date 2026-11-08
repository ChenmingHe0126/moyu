using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Games.Common;
using Moyu.Games.Common.Core;
using Moyu.Games.Swift;
using Moyu.Games.Swift.Core;
using Rhino;
using Rhino.Geometry;

namespace Moyu.Components.Section
{
  /// <summary>
  /// The Swift game, presented as a section airflow study. Run (the button on the component) starts it in the active
  /// Rhino viewport; the outputs refresh when the game pauses, switches to boss mode, ends or stops.
  /// </summary>
  public class AirflowStudyComponent : MoyuComponent, IRunnable
  {
    FlightSpec _spec;
    Plane _plane;
    double _size;
    /// <summary>The Language input's choice; null follows the system.</summary>
    UiLanguage? _language;
    /// <summary>Set while the document closes or the component leaves it: the session's last change must not touch the canvas or schedule a solution.</summary>
    bool _closing;

    // Snapshot of the last session, shown on the outputs.
    string _state = "Ready";
    int _score;
    long _best;
    Curve _path;
    List<Curve> _section = new List<Curve>();
    List<string> _metrics = new List<string>();

    public AirflowStudyComponent()
      : base("Airflow Study", "Airflow",
        "Simulates cross and stack ventilation and daylight through a building section, and reports air changes and daylight factors. Press Run on the component to start.",
        "Section")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override void CreateAttributes() => m_attributes = new RunButtonAttributes(this);

    public bool IsRunning => GameSession.Current != null && ReferenceEquals(GameSession.Current.Owner, this);

    /// <summary>A section standing on the World XZ plane: X across, Z up.</summary>
    public static Plane DefaultPlane => new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis);

    /// <summary>0.75 m per course unit: the course is 9 m from floor to ceiling, a two-storey house.</summary>
    public static double DefaultSize()
    {
      var doc = RhinoDoc.ActiveDoc;
      return doc == null ? 0.75 : 0.75 * RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Grid", "G", "Course settings from Storey Grid. Default: normal exposure, classic.", GH_ParamAccess.item);
      pManager.AddPlaneParameter("Plane", "P", "The section plane (X across, Y up). Default: World XZ at the origin.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Size", "Sz", "Model units per course unit. Default: 0.75 m.", GH_ParamAccess.item);
      AddLanguageInput(pManager);
      pManager[0].Optional = true;
      pManager[1].Optional = true;
      pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("State", "St", "Study state.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Score", "S", "Walls passed in the last flight.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Best", "B", "Best score for these settings on this computer.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Path", "Pa", "The last stretch of the flight path on the section plane.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Section", "Sc", "Cut outlines of the climate section (walls, slabs, roof).", GH_ParamAccess.list);
      pManager.AddTextParameter("Metrics", "Mt", "Daylight factor, workplane share, rooflights, cross and stack air changes, air speeds.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One Airflow Study runs one game; extra input branches are ignored.");
        return;
      }
      IGH_Goo specGoo = null;
      var plane = DefaultPlane;
      double size = DefaultSize();
      DA.GetData(0, ref specGoo);
      DA.GetData(1, ref plane);
      DA.GetData(2, ref size);
      _language = LanguageInput.Read(this, DA, 3);
      if (IsRunning) LanguageInput.Apply(GameSession.Current, _language);

      var errors = new List<string>();
      var spec = FlightSpec.Default;
      if (specGoo != null && !MoyuGoo<FlightSpec>.TryGet(specGoo, out spec)) errors.Add($"Grid input received {specGoo.TypeName}; connect Storey Grid.");
      if (!(size > 0)) errors.Add("Size must be positive.");
      if (!plane.IsValid) errors.Add("Plane is not valid.");
      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      _spec = errors.Count == 0 ? spec : null;
      _plane = plane;
      _size = size;

      if (!IsRunning && _state == "Ready" && _spec != null) _best = GameScores.Best(SwiftSession.ScoreFile, SwiftSession.ScoreKeyFor(_spec.Settings));
      DA.SetData(0, _state);
      DA.SetData(1, _score);
      DA.SetData(2, (int)Math.Min(int.MaxValue, _best));
      DA.SetData(3, _path);
      DA.SetDataList(4, _section);
      DA.SetDataList(5, _metrics);
      if (errors.Count > 0) Message = "Error";
      else if (!IsRunning && _state == "Ready") Message = $"{spec.Settings.Exposure}" + (spec.Settings.Mode == SwiftMode.Zen ? " · Zen" : "");
    }

    /// <summary>Starts a flight, or stops the one this component is running.</summary>
    public void Run()
    {
      if (IsRunning)
      {
        GameSession.Current.Stop();
        return;
      }
      if (_spec == null)
      {
        RhinoApp.WriteLine("Airflow Study: fix the errors on the component first.");
        return;
      }
      var session = SwiftSession.Start(_spec.Settings.Copy(), _spec.Seed, _plane, _size, this, OnSessionChanged, out string error);
      if (session == null) RhinoApp.WriteLine($"Airflow Study: {error}");
      else LanguageInput.Apply(session, _language);
    }

    void OnSessionChanged(GameSession session)
    {
      var s = (SwiftSession)session;
      var g = s.Game;
      _score = g.Score;
      _best = Math.Max(s.Best, g.Score);
      var trail = g.Trail.Skip(Math.Max(0, g.Trail.Count - 200)).Select(q => s.AtCourse(q.X, q.Y)).ToList();
      _path = trail.Count >= 2 ? new PolylineCurve(trail) : null;
      var dz = s.Boss && s.Disguise != null ? s.Disguise : s.CurrentDisguise();
      _section = dz.CutCurves().ToList();
      var m = dz.Model.Metrics;
      _metrics = new List<string>
      {
        $"Mean daylight factor {m.MeanDf:F1} %", $"Workplane at 2 % DF or more {m.Sda * 100:F0} %", $"Rooflights {m.Rooflights} ({m.RooflightArea:F0} m²)",
        $"Cross ventilation {m.CrossAch:F1} ach", $"Stack ventilation {m.StackAch:F1} ach", $"Mean air speed {m.MeanSpeed:F2} m/s", $"Air speed at P1 {m.ProbeSpeed:F2} m/s",
      };
      if (s.Stopped) _state = g.IsOver ? "Study complete" : "Ready";
      else if (s.Boss) _state = "Section A-A";
      else if (g.IsOver) _state = "Study complete";
      else if (g.State == SwiftState.Paused) _state = "Study paused";
      else _state = "Study running";
      Message = s.Boss ? $"Section A-A · {m.CrossAch:F1} ach" : s.Stopped || g.IsOver ? (g.Score > 0 ? $"Score {g.Score}" : "Ready") : "Running";

      if (_closing) return;
      Instances.RedrawCanvas();
      // Refresh the outputs after this frame, so a heavy downstream definition never delays the boss key.
      OnPingDocument()?.ScheduleSolution(20, d => ExpireSolution(false));
    }

    public override void AddedToDocument(GH_Document document)
    {
      _closing = false;
      base.AddedToDocument(document);
    }

    public override void RemovedFromDocument(GH_Document document)
    {
      _closing = true;
      if (IsRunning) GameSession.Current.Stop();
      base.RemovedFromDocument(document);
    }

    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
      if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
      {
        _closing = true;
        if (IsRunning) GameSession.Current.Stop();
      }
      else if (context == GH_DocumentContext.Open || context == GH_DocumentContext.Loaded) _closing = false;
      base.DocumentContextChanged(document, context);
    }

    public override Guid ComponentGuid => new Guid("2a073a5f-8b4d-42aa-9b77-59a090251c1f");
  }
}
