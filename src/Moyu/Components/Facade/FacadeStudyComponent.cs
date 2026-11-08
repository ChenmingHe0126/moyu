using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Games.Breakout;
using Moyu.Games.Breakout.Core;
using Moyu.Games.Common;
using Moyu.Games.Common.Core;
using Rhino;
using Rhino.Geometry;

namespace Moyu.Components.Facade
{
  /// <summary>
  /// The Rebound game, presented as a facade elevation study. Run (the button on the component) starts a game in the
  /// active Rhino viewport; the outputs refresh when the game pauses, switches to boss mode or ends.
  /// </summary>
  public class FacadeStudyComponent : GameComponent
  {
    const string ScoreFile = "rebound";

    BreakoutSettings _pending;
    Plane _plane;
    double _size;

    // Snapshot of the last session, shown on the outputs.
    string _state = "Ready";
    long _score, _best;
    string _bestKey;
    int _level = 1;
    List<Curve> _panels = new List<Curve>(), _windows = new List<Curve>();
    List<string> _metrics = new List<string>();
    // The document is going away: a session stopped now must not schedule a solution on it.
    bool _closing;
    // Why the last Run could not start. A failed start stops itself, and the solution that schedules clears runtime
    // messages, so SolveInstance shows it again until the next Run.
    string _runError;

    public FacadeStudyComponent()
      : base("Facade Study", "Facade",
        "Studies the panel and window layout of a brick facade and reports window-to-wall ratio and envelope figures. Press Run on the component to start.",
        Panels.Facade)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    /// <summary>A facade standing on the World XZ plane: X along the wall, Z up.</summary>
    public static Plane DefaultPlane => new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Wall", "W", "Brick bond or custom wall from Brick Bond. Default: the six bonds in turn, one per level.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Mode", "M", "0 = Classic (levels, 3 lives), 1 = Blitz (3 minutes), 2 = Zen (no lives lost).", GH_ParamAccess.item, 0);
      pManager.AddIntegerParameter("Level", "Lv", "Starting level (1–15); higher is faster and harder.", GH_ParamAccess.item, 1);
      pManager.AddPlaneParameter("Plane", "P", "The facade plane (X along the wall, Y up). Default: World XZ at the origin.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Size", "S", "Model units per facade module. Default: 2.4 m.", GH_ParamAccess.item);
      pManager[0].Optional = true;
      pManager[3].Optional = true;
      pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("State", "St", "Study state.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Score", "Sc", "Score of the current or last game.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Level", "Lv", "Current level.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Best", "B", "Best score for this mode and wall on this computer.", GH_ParamAccess.item);
      pManager.AddCurveParameter("Panels", "Pn", "Facade units still standing (outlines on the facade plane).", GH_ParamAccess.list);
      pManager.AddCurveParameter("Windows", "Wn", "Units opened up as clear vision glass (outlines).", GH_ParamAccess.list);
      pManager.AddTextParameter("Metrics", "Mt", "Storeys, curtain-wall panels, window-to-wall ratio, vision glass area, facade U-value, solar factor and embodied carbon.", GH_ParamAccess.list);
    }

    // A required input with no data skips SolveInstance, so clear the settings here: Run must not start a game from
    // the inputs of an earlier solution.
    protected override void BeforeSolveInstance()
    {
      _pending = null;
      base.BeforeSolveInstance();
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One Facade Study runs one game; extra input branches are ignored.");
        return;
      }
      IGH_Goo wallGoo = null;
      int mode = 0, level = 1;
      var plane = DefaultPlane;
      double size = DefaultSize();
      DA.GetData(0, ref wallGoo);
      DA.GetData(1, ref mode);
      DA.GetData(2, ref level);
      DA.GetData(3, ref plane);
      DA.GetData(4, ref size);

      _pending = null;
      var errors = new List<string>();
      if (mode < 0 || mode > 2) errors.Add("Mode must be 0 (Classic), 1 (Blitz) or 2 (Zen).");
      if (!(size > 0) || double.IsInfinity(size)) errors.Add("Size must be a positive finite number.");
      if (!plane.IsValid) errors.Add("Plane is not valid.");
      level = Math.Max(1, Math.Min(15, level));
      WallSpec wall = null;
      if (wallGoo != null && !MoyuGoo<WallSpec>.TryGet(wallGoo, out wall)) errors.Add(WrongInput("Wall", wallGoo, "Brick Bond"));

      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      if (_runError != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _runError);
      if (errors.Count == 0)
      {
        _pending = new BreakoutSettings
        {
          Mode = (BreakoutMode)mode, StartLevel = level, Bond = wall?.Bond, Custom = wall?.Custom, Courses = wall?.Courses ?? 10,
        };
        _plane = plane;
        _size = size;
      }

      // Best follows the inputs; a stopped game's score counts only while the inputs still match it.
      if (!IsRunning)
      {
        string key = $"{(BreakoutMode)mode}-{(wall?.Custom != null ? "custom" : wall?.Bond?.ToString() ?? "bonds")}";
        _best = Math.Max(GameScores.Best(ScoreFile, key), key == _bestKey ? _best : 0);
        _bestKey = key;
      }
      DA.SetData(0, _state);
      DA.SetData(1, (int)Math.Min(int.MaxValue, _score));
      DA.SetData(2, _level);
      DA.SetData(3, (int)Math.Min(int.MaxValue, _best));
      DA.SetDataList(4, _panels);
      DA.SetDataList(5, _windows);
      DA.SetDataList(6, _metrics);
      if (errors.Count > 0) Message = "Error";
      else if (!IsRunning && _state == "Ready") Message = wall?.Custom != null ? "Custom wall" : wall?.Bond != null ? BondName(wall.Bond.Value) : "Ready";
    }

    /// <summary>The bond's name in the component's language ("顺砌 Stretcher bond" → 顺砌 or Stretcher bond).</summary>
    string BondName(BondType bond)
    {
      string both = Bonds.Names[(int)bond];
      int space = both.IndexOf(' ');
      return Language == UiLanguage.Chinese ? both.Substring(0, space) : both.Substring(space + 1);
    }

    /// <summary>The bond name under the component follows the language.</summary>
    protected override void OnLanguageChanged()
    {
      if (!IsRunning) ExpireSolution(true);
    }

    public static double DefaultSize()
    {
      var doc = RhinoDoc.ActiveDoc;
      return doc == null ? TowerFacade.FieldModule : TowerFacade.FieldModule * RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
    }

    /// <summary>Starts a game, or stops the one this component is running.</summary>
    public override void Run()
    {
      _runError = null;
      if (IsRunning)
      {
        GameSession.Current.Stop();
        return;
      }
      if (_pending == null)
      {
        CannotRun("Fix the errors or empty inputs on the component first.");
        return;
      }
      var settings = new BreakoutSettings
      {
        Mode = _pending.Mode, StartLevel = _pending.StartLevel, Bond = _pending.Bond, Custom = _pending.Custom, Courses = _pending.Courses,
        Seed = Environment.TickCount,
      };
      var session = BreakoutSession.Start(settings, _plane, _size, this, OnSessionChanged, out string error);
      if (session == null)
      {
        _runError = error;
        CannotRun(error);
      }
    }

    /// <summary>Shows why the Run button did nothing on the component, where the user is looking.</summary>
    void CannotRun(string why)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, why);
      Instances.RedrawCanvas();
    }

    void OnSessionChanged(GameSession session)
    {
      var s = (BreakoutSession)session;
      var g = s.Game;
      _score = g.Score;
      _level = g.Level;
      _best = Math.Max(s.Best, g.Score);
      _bestKey = s.ScoreKey;
      var t = (s.Boss && s.Disguise != null ? s.Disguise : s.CurrentDisguise()).Tower;
      _panels = s.BrickCurves(g.Bricks.Where(b => b.Alive)).ToList();
      _windows = s.BrickCurves(g.Broken).ToList();
      _metrics = new List<string>
      {
        $"Storeys {t.Storeys} ({t.Top:F1} m)", $"Curtain-wall panels {t.PanelCount}", $"Window-to-wall {t.Wwr * 100:F0} %", $"Vision glass {t.VisionArea:N0} m²",
        $"Facade U-value {t.UValue:F2} W/m²K", $"Solar factor g {t.GValue:F2}", $"Embodied carbon {t.CarbonTonnes:N0} t CO₂e",
      };
      if (s.Stopped) _state = g.IsOver ? "Study complete" : "Ready";
      else if (s.Boss) _state = "Elevation B";
      else if (g.IsOver) _state = "Study complete";
      else if (g.State == BreakoutState.Paused) _state = "Study paused";
      else _state = "Study running";
      // Stopped first: Stop leaves Boss set, and a stopped game must not keep the disguise's label.
      Message = s.Stopped ? (g.IsOver ? $"Score {g.Score:N0}" : "Ready") : s.Boss ? $"Elevation B · WWR {t.Wwr * 100:F0} %" : g.IsOver ? $"Score {g.Score:N0}" : "Running";

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
        if (IsRunning)
        {
          GameSession.Current.Stop();
          // Another tab took the canvas, so the stop refreshed nothing. Expire now (the document is disabled, so
          // nothing solves); the solution Grasshopper runs when the tab comes back recomputes the outputs.
          if (context == GH_DocumentContext.Unloaded) ExpireSolution(false);
        }
      }
      else if (context == GH_DocumentContext.Open || context == GH_DocumentContext.Loaded) _closing = false;
      base.DocumentContextChanged(document, context);
    }

    public override Guid ComponentGuid => new Guid("b232de6c-3642-427f-90d5-1e90050e09cc");
  }
}
