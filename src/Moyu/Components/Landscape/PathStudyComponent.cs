using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Games.Common;
using Moyu.Games.Snake;
using Moyu.Games.Snake.Core;
using Rhino.Geometry;

namespace Moyu.Components.Landscape
{
  /// <summary>
  /// The Serpentine game, presented as a circulation path study over a site surface. Run (the button on the
  /// component) starts a game in the active Rhino viewport; the outputs refresh when the game pauses, switches to
  /// boss mode or ends.
  /// </summary>
  public class PathStudyComponent : MoyuComponent, IRunnable
  {
    const string ScoreFile = "serpentine";

    SurfaceField _field;
    SnakeSettings _pending;
    // What the field was built from (the input goo, or the built-in terrain), so a slider nudge elsewhere does not resample the surface.
    object _fieldSource;
    int _fieldCells;
    // The built-in terrain, rebuilt only when the document's unit size changes.
    Brep _defaultBrep;
    double _defaultSize;

    // Snapshot of the last session, shown on the outputs.
    string _state = "Ready";
    long _score, _best;
    string _bestKey;
    int _length;
    Curve _route;
    List<string> _metrics = new List<string>();
    // The document is going away: a session stopped now must not schedule a solution on it.
    bool _closing;

    public PathStudyComponent()
      : base("Path Study", "Path",
        "Traces a circulation route over a site surface and reports its length and slopes. Press Run on the component to start.",
        "Landscape")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override void CreateAttributes() => m_attributes = new RunButtonAttributes(this);

    public bool IsRunning => GameSession.Current != null && ReferenceEquals(GameSession.Current.Owner, this);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddSurfaceParameter("Surface", "S", "Site surface to walk on (a trimmed face works; closed directions wrap round). Default: rolling terrain from Site Surface.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Cells", "N", "Grid cells along the surface's longer direction (8–80).", GH_ParamAccess.item, 28);
      pManager.AddIntegerParameter("Mode", "M", "0 = Classic (endless), 1 = Blitz (3 minutes), 2 = Zen (no walls to die on, no bombs).", GH_ParamAccess.item, 0);
      pManager.AddIntegerParameter("Level", "Lv", "Starting level (1–15); higher is faster.", GH_ParamAccess.item, 1);
      pManager.AddBooleanParameter("Slope", "Sl", "Downhill is faster and uphill slower.", GH_ParamAccess.item, true);
      pManager[0].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("State", "St", "Study state.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Score", "Sc", "Score of the current or last game.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Length", "Ln", "Length of the route in cells.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Best", "B", "Best score for this mode and grid on this computer.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Route", "R", "The route as a smooth curve over the surface.", GH_ParamAccess.item);
      pManager.AddTextParameter("Metrics", "Mt", "Route length, elevation gain, average and maximum slope, accessible share and nodes.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One Path Study runs one game; extra input branches are ignored.");
        return;
      }
      // The cast hands out a new Brep on every solve, so the cache keys on the goo, which arrives by reference until upstream changes.
      IGH_Goo goo = null;
      Brep brep = null;
      int cells = 28, mode = 0, level = 1;
      bool slope = true;
      DA.GetData(0, ref goo);
      if (goo != null) DA.GetData(0, ref brep);
      DA.GetData(1, ref cells);
      DA.GetData(2, ref mode);
      DA.GetData(3, ref level);
      DA.GetData(4, ref slope);

      _pending = null;
      var errors = new List<string>();
      if (cells < 8 || cells > 80) errors.Add("Cells must be between 8 and 80.");
      if (mode < 0 || mode > 2) errors.Add("Mode must be 0 (Classic), 1 (Blitz) or 2 (Zen).");
      level = Math.Max(1, Math.Min(15, level));
      if (brep == null) brep = DefaultBrep();
      if (brep == null || brep.Faces.Count != 1) errors.Add("Surface must be a single surface or trimmed face.");
      object source = goo ?? (object)brep;

      if (errors.Count == 0 && !IsRunning && (_field == null || !ReferenceEquals(source, _fieldSource) || cells != _fieldCells))
      {
        _field = SurfaceField.Build(brep.Faces[0], cells, out string why);
        _fieldSource = source;
        _fieldCells = cells;
        if (_field == null) errors.Add(why);
      }
      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      if (errors.Count == 0) _pending = new SnakeSettings { Mode = (SnakeMode)mode, StartLevel = level, SlopeAffectsSpeed = slope };

      // Best follows the inputs; a stopped game's score counts only while the inputs still match it.
      if (!IsRunning && _field != null)
      {
        string key = $"{(SnakeMode)mode}-{_field.Grid.Nu}x{_field.Grid.Nv}";
        _best = Math.Max(GameScores.Best(ScoreFile, key), key == _bestKey ? _best : 0);
      }
      DA.SetData(0, _state);
      DA.SetData(1, (int)Math.Min(int.MaxValue, _score));
      DA.SetData(2, _length);
      DA.SetData(3, (int)Math.Min(int.MaxValue, _best));
      DA.SetData(4, _route);
      DA.SetDataList(5, _metrics);
      if (errors.Count > 0) Message = "Error";
      else if (!IsRunning && _state == "Ready") Message = _field.Describe();
    }

    Brep DefaultBrep()
    {
      double size = SiteSurfaceComponent.DefaultSize();
      if (_defaultBrep == null || size != _defaultSize)
      {
        _defaultBrep = SiteSurfaces.Create(0, size, 1);
        _defaultSize = size;
      }
      return _defaultBrep;
    }

    /// <summary>Starts a game, or stops the one this component is running.</summary>
    public void Run()
    {
      if (IsRunning)
      {
        GameSession.Current.Stop();
        return;
      }
      if (_pending == null || _field == null)
      {
        CannotRun("Fix the errors on the component first.");
        return;
      }
      var settings = new SnakeSettings
      {
        Mode = _pending.Mode, StartLevel = _pending.StartLevel, SlopeAffectsSpeed = _pending.SlopeAffectsSpeed, Seed = Environment.TickCount,
      };
      var session = SnakeSession.Start(_field, settings, this, OnSessionChanged, out string error);
      if (session == null) CannotRun(error);
    }

    /// <summary>Shows why the Run button did nothing on the component, where the user is looking.</summary>
    void CannotRun(string why)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, why);
      Instances.RedrawCanvas();
    }

    void OnSessionChanged(GameSession session)
    {
      var s = (SnakeSession)session;
      var g = s.Game;
      _score = g.Score;
      _length = g.Length;
      _best = Math.Max(s.Best, g.Score);
      _bestKey = s.ScoreKey;
      var dz = s.CurrentDisguise();
      _route = CirculationDisguise.RouteCurve(s.Field, g.Body.Reverse().ToList());
      var m = dz.Metrics;
      _metrics = new List<string>
      {
        $"Route length {m.Length:F1} m", $"Elevation gain {m.Gain:F1} m", $"Average slope {m.AverageSlope * 100:F1} %",
        $"Max slope {m.MaxSlope * 100:F1} %", $"≤ 5 % slope {m.Accessible * 100:F0} %", $"Nodes {dz.Nodes.Length}",
      };
      if (s.Stopped) _state = g.IsOver ? "Study complete" : "Ready";
      else if (s.Boss) _state = "Route A";
      else if (g.IsOver) _state = "Study complete";
      else if (g.State == SnakeState.Paused) _state = "Study paused";
      else _state = "Study running";
      Message = s.Boss ? $"Route A · {m.Length:F0} m" : s.Stopped ? (g.IsOver ? $"Score {g.Score:N0}" : s.Field.Describe()) : g.IsOver ? $"Score {g.Score:N0}" : "Running";

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

    public override Guid ComponentGuid => new Guid("6a7a47ea-88fd-4baf-9269-f62e90906e5f");
  }
}
