using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.Games.Common;
using Moyu.Games.Common.Core;
using Moyu.Games.Stack;
using Moyu.Games.Stack.Core;
using Rhino;
using Rhino.Geometry;

namespace Moyu.Components.Massing
{
  /// <summary>
  /// The VoxStack game, presented as a massing study generator. Run (the button on the component) starts a game in
  /// the active Rhino viewport; the outputs refresh when the game pauses, switches to boss mode or ends.
  /// </summary>
  public class MassingStudyComponent : MoyuComponent, IRunnable
  {
    const string ScoreFile = "voxstack";

    StackSettings _pending;
    Plane _plane = Plane.WorldXY;
    double _size = 7.2;
    /// <summary>The Language input's choice; null follows the system.</summary>
    UiLanguage? _language;

    // Snapshot of the last session, shown on the outputs.
    string _state = "Ready";
    long _score, _best;
    string _bestKey;
    int _layers, _level = 1;
    Mesh _mass;
    List<string> _metrics = new List<string>();
    // The document is going away: a session stopped now must not schedule a solution on it.
    bool _closing;

    public MassingStudyComponent()
      : base("Massing Study", "Mass",
        "Stacks voxel units inside a site well and reports planning metrics. Press Run on the component to start.",
        "Massing")
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override void CreateAttributes() => m_attributes = new RunButtonAttributes(this);

    public bool IsRunning => GameSession.Current != null && ReferenceEquals(GameSession.Current.Owner, this);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddGenericParameter("Library", "L", "Units to stack, from Unit Library. Default: the standard set.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Width", "X", "Well width in voxels (4–10).", GH_ParamAccess.item, 5);
      pManager.AddIntegerParameter("Depth", "Y", "Well depth in voxels (4–10).", GH_ParamAccess.item, 5);
      pManager.AddIntegerParameter("Height", "Z", "Well height in voxels (8–20).", GH_ParamAccess.item, 12);
      pManager.AddPlaneParameter("Plane", "P", "Where the well stands. Default: World XY at the origin.", GH_ParamAccess.item);
      pManager.AddNumberParameter("Size", "S", "Voxel edge in model units. Default: one 7.2 m structural bay.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Mode", "M", "0 = Marathon (endless), 1 = Blitz (3 minutes), 2 = Zen (no gravity, no game over).", GH_ParamAccess.item, 0);
      pManager.AddIntegerParameter("Level", "Lv", "Starting level (1–15); higher falls faster.", GH_ParamAccess.item, 1);
      AddLanguageInput(pManager);
      pManager[0].Optional = true;
      pManager[4].Optional = true;
      pManager[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddTextParameter("State", "St", "Study state.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Score", "Sc", "Score of the current or last game.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Layers", "Ly", "Layers cleared.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Level", "Lv", "Current level.", GH_ParamAccess.item);
      pManager.AddIntegerParameter("Best", "B", "Best score for this mode and well size on this computer.", GH_ParamAccess.item);
      pManager.AddMeshParameter("Mass", "Ms", "The stack as a massing mesh (one voxel = one bay, three storeys).", GH_ParamAccess.item);
      pManager.AddTextParameter("Metrics", "Mt", "Site area, GFA, FAR, floors, coverage and height of the massing.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (DA.Iteration > 0)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One Massing Study runs one game; extra input branches are ignored.");
        return;
      }
      IGH_Goo libGoo = null;
      int nx = 5, ny = 5, nz = 12, mode = 0, level = 1;
      var plane = Plane.WorldXY;
      double size = DefaultSize();
      DA.GetData(0, ref libGoo);
      DA.GetData(1, ref nx);
      DA.GetData(2, ref ny);
      DA.GetData(3, ref nz);
      DA.GetData(4, ref plane);
      DA.GetData(5, ref size);
      DA.GetData(6, ref mode);
      DA.GetData(7, ref level);
      _language = LanguageInput.Read(this, DA, 8);
      if (IsRunning) LanguageInput.Apply(GameSession.Current, _language);

      _pending = null;
      var errors = new List<string>();
      if (nx < 4 || nx > 10 || ny < 4 || ny > 10) errors.Add("Width and Depth must be between 4 and 10 voxels.");
      if (nz < 8 || nz > 20) errors.Add("Height must be between 8 and 20 voxels.");
      if (mode < 0 || mode > 2) errors.Add("Mode must be 0 (Marathon), 1 (Blitz) or 2 (Zen).");
      if (!(size > 0)) errors.Add("Size must be positive.");
      if (!plane.IsValid) errors.Add("Plane is not valid.");
      level = Math.Max(1, Math.Min(15, level));

      IReadOnlyList<PieceDef> pieces = PieceSets.Get(PieceSets.Standard);
      if (libGoo != null)
      {
        if (MoyuGoo<UnitLibrary>.TryGet(libGoo, out var lib)) pieces = lib.Pieces;
        else if (MoyuGoo<StackUnit>.TryGet(libGoo, out var unit)) pieces = new[] { unit.Piece };
        else errors.Add(WrongInput("Library", libGoo, "Unit Library (or a single Voxel Unit)"));
      }
      if (errors.Count == 0)
        foreach (var p in pieces)
        {
          string why = PieceDef.Validate(p.Cells, nx, ny, nz);
          if (why != null) errors.Add($"{p.Name}: {why}");
        }

      foreach (var e in errors) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
      if (errors.Count == 0)
      {
        _pending = new StackSettings { Nx = nx, Ny = ny, Nz = nz, Mode = (StackMode)mode, StartLevel = level, Pieces = pieces };
        _plane = plane;
        _size = size;
      }

      // Best follows the inputs; a stopped game's score counts only while the inputs still match it.
      if (!IsRunning)
      {
        string key = $"{(StackMode)mode}-{nx}x{ny}x{nz}";
        _best = Math.Max(GameScores.Best(ScoreFile, key), key == _bestKey ? _best : 0);
      }
      DA.SetData(0, _state);
      DA.SetData(1, (int)Math.Min(int.MaxValue, _score));
      DA.SetData(2, _layers);
      DA.SetData(3, _level);
      DA.SetData(4, (int)Math.Min(int.MaxValue, _best));
      DA.SetData(5, _mass);
      DA.SetDataList(6, _metrics);
      if (errors.Count > 0) Message = "Error";
      else if (!IsRunning && _state == "Ready") Message = "Ready";
    }

    static double DefaultSize()
    {
      var doc = RhinoDoc.ActiveDoc;
      return doc == null ? MassingMetrics.Bay : MassingMetrics.Bay * RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
    }

    /// <summary>Starts a game, or stops the one this component is running.</summary>
    public void Run()
    {
      if (IsRunning)
      {
        GameSession.Current.Stop();
        return;
      }
      if (_pending == null)
      {
        CannotRun("Fix the errors on the component first.");
        return;
      }
      var settings = new StackSettings
      {
        Nx = _pending.Nx, Ny = _pending.Ny, Nz = _pending.Nz, Mode = _pending.Mode, StartLevel = _pending.StartLevel,
        Pieces = _pending.Pieces, Seed = Environment.TickCount,
      };
      var session = StackSession.Start(settings, _plane, _size, this, OnSessionChanged, out string error);
      if (session == null) CannotRun(error);
      else LanguageInput.Apply(session, _language);
    }

    /// <summary>Shows why the Run button did nothing on the component, where the user is looking.</summary>
    void CannotRun(string why)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, why);
      Instances.RedrawCanvas();
    }

    void OnSessionChanged(GameSession session)
    {
      var s = (StackSession)session;
      var g = s.Game;
      _score = g.Score;
      _layers = g.Layers;
      _level = g.Level;
      _best = Math.Max(s.Best, g.Score);
      _bestKey = s.ScoreKey;
      var disguise = s.Boss && s.Disguise != null ? s.Disguise
        : new MassingDisguise(g.Well.Cells().Select(c => c.cell), s.Settings.Nx, s.Settings.Ny, s.Settings.Nz, s.Plane, s.VoxelSize, s.Settings.Seed);
      _mass = disguise.WorldMass();
      var m = disguise.Metrics;
      _metrics = new List<string>
      {
        $"Site area {m.SiteArea:N0} m²", $"GFA {m.Gfa:N0} m²", $"FAR {m.Far:F2}", $"Floors {m.Floors}",
        $"Coverage {m.Coverage * 100:F0} %", $"Height {m.Height:F1} m",
      };
      if (s.Stopped) _state = g.IsOver ? "Study complete" : "Ready";
      else if (s.Boss) _state = "Option B";
      else if (g.IsOver) _state = "Study complete";
      else if (g.State == StackState.Paused) _state = "Study paused";
      else _state = "Study running";
      Message = s.Boss ? $"Option B · FAR {m.Far:F2}" : s.Stopped ? (g.IsOver ? $"Score {g.Score:N0}" : "Ready") : g.IsOver ? $"Score {g.Score:N0}" : "Running";

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

    public override Guid ComponentGuid => new Guid("6f1d7a52-3c84-4e0b-9a51-2d7c9e4b8f10");
  }
}
