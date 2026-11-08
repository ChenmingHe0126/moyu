using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Moyu.Games.Common.Core;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.UI;

namespace Moyu.Games.Common
{
  /// <summary>
  /// What every Moyu game shares: one game running in a Rhino viewport, the keyboard hook and frame clock, the
  /// boss key (also triggered when Rhino loses focus), the 3-2-1 countdown, pause / restart / quit (the rules also
  /// pause while another Rhino window or a Rhino command has the keys), the mouse orbit, the camera reset, and
  /// putting the user's view back afterwards. A game supplies its rules, drawing and disguise through the abstract
  /// members. Only one game session runs at a time; starting another stops the first.
  /// </summary>
  public abstract class GameSession
  {
    public static GameSession Current { get; private set; }

    /// <summary>The key left of 1 (` ~ on a US keyboard); the hook reports that key as this on every layout.</summary>
    public const Keys BossKey = Keys.Oemtilde;
    protected const double CountdownSeconds = 3;
    const double MinElevation = 8 * Math.PI / 180, MaxElevation = 89 * Math.PI / 180;

    /// <summary>The component that started the session.</summary>
    public readonly object Owner;
    public readonly Guid ViewportId;
    public bool Boss { get; private set; }
    public double BossTime { get; private set; }
    /// <summary>Seconds left in the 3-2-1; 0 when not counting.</summary>
    public double Countdown { get; private set; }
    /// <summary>True when the running countdown starts a new game rather than resuming one.</summary>
    public bool CountdownIsStart { get; private set; }
    /// <summary>Real seconds since the session started (drives HUD animation).</summary>
    public double Time { get; private set; }
    /// <summary>Seconds since the game ended.</summary>
    public double OverTime { get; private set; }
    /// <summary>Seconds of play since the current round's start countdown ended (drives the key-hint fade).</summary>
    public double PlayTime { get; private set; }
    public bool AutoBoss { get; set; } = true;
    public bool Stopped { get; private set; }

    /// <summary>
    /// Language of the text the game draws (the boss-mode disguises stay English). It starts as the owner's choice
    /// (<see cref="ILanguageOwner"/>), else the system's; the component switches it while the game runs.
    /// </summary>
    public UiLanguage Language
    {
      get => _language;
      set
      {
        if (_language == value) return;
        _language = value;
        if (!Stopped) Redraw();
      }
    }

    UiLanguage _language;

    [DllImport("kernel32.dll")]
    static extern ushort GetUserDefaultUILanguage();

    /// <summary>The Windows display language (Settings › Time &amp; language), else the UI culture: Chinese for any Chinese, English otherwise.</summary>
    public static UiLanguage SystemLanguage()
    {
      if (System.Environment.OSVersion.Platform == PlatformID.Win32NT)
      {
        try { return UiLanguages.FromCulture(new CultureInfo(GetUserDefaultUILanguage()).Name); }
        catch (Exception) { /* an unknown language id: fall back to the UI culture */ }
      }
      return UiLanguages.FromCulture(CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>A Language input's choice, with auto (null) resolved to the system language.</summary>
    public static UiLanguage Resolve(UiLanguage? choice) => choice ?? SystemLanguage();

    protected readonly RhinoDoc Doc;
    protected readonly RhinoView View;
    /// <summary>Keys currently held down, as seen by the hook.</summary>
    protected readonly HashSet<Keys> Held = new HashSet<Keys>();

    readonly ViewportInfo _userView;
    readonly bool _userMaximized, _userGrid, _userAxes, _userWorldAxes;
    /// <summary>The view that was active at the start (a layout page when the game was started from one).</summary>
    readonly Guid _userActiveView;
    readonly Action<GameSession> _changed;
    GameConduit _conduit;
    KeyboardHook _hook;
    GameClock _clock;
    OrbitMouse _mouse;
    ViewportInfo _gameCamera;
    bool _pausedByBoss;
    // The round's game-over step has run (it can end in a frame or in a key handler).
    bool _overNoted;

    protected GameSession(object owner, Action<GameSession> changed, RhinoDoc doc, RhinoView view)
    {
      Owner = owner;
      _language = owner is ILanguageOwner lo ? lo.Language : SystemLanguage();
      _changed = changed;
      Doc = doc;
      View = view;
      ViewportId = view.ActiveViewportID;
      var vp = view.ActiveViewport;
      _userView = new ViewportInfo(vp);
      _userMaximized = view.Maximized;
      _userGrid = vp.ConstructionGridVisible;
      _userAxes = vp.ConstructionAxesVisible;
      _userWorldAxes = vp.WorldAxesVisible;
      _userActiveView = doc.Views.ActiveView?.MainViewport.Id ?? Guid.Empty;
    }

    /// <summary>A standard view to play in: the active one, or the first perspective view when a layout is active.</summary>
    public static RhinoView PickView(out RhinoDoc doc, out string error)
    {
      error = null;
      doc = RhinoDoc.ActiveDoc;
      var view = doc?.Views.ActiveView;
      if (view is RhinoPageView)
        view = doc.Views.GetStandardRhinoViews().FirstOrDefault(v => v.ActiveViewport.IsPerspectiveProjection) ?? doc.Views.GetStandardRhinoViews().FirstOrDefault();
      if (view == null) error = "There is no Rhino viewport to play in.";
      return view;
    }

    public static void StopCurrent() => Current?.Stop();

    /// <summary>
    /// Takes over the view and starts the countdown; call at the end of the game's constructor. False (with the
    /// session already stopped, so the hook never outlives a failed start) when keyboard capture is unavailable or
    /// the take-over throws.
    /// </summary>
    protected bool Begin(GameConduit conduit, out string error)
    {
      error = null;
      Current?.Stop();
      Current = this;
      _conduit = conduit;
      _conduit.Enabled = true;
      _hook = new KeyboardHook(OnKey);
      if (!_hook.Installed)
      {
        int code = _hook.InstallError;
        Stop();
        error = code != 0
          ? $"Keyboard capture could not be installed (Win32 error {code})."
          : "Keyboard capture is not available here (Moyu games need Rhino for Windows).";
        return false;
      }
      try
      {
        _mouse = new OrbitMouse(this) { Enabled = true };
        RhinoDoc.CloseDocument += OnCloseDocument;
        RhinoApp.Closing += OnRhinoClosing;

        var vp = View.ActiveViewport;
        if (vp.IsParallelProjection) vp.ChangeToPerspectiveProjection(true, 28);
        View.Maximized = true;
        vp = View.ActiveViewport;
        vp.ConstructionGridVisible = false;
        vp.ConstructionAxesVisible = false;
        vp.WorldAxesVisible = false;
        SetStartCamera(vp);
        StartCountdown(true);
        _clock = new GameClock(Tick);
        // The Run button lives in the Grasshopper window; the keys belong to the game from here on.
        RhinoApp.SetFocusToMainWindow();
        View.Redraw();
        Changed();
        return true;
      }
      catch (Exception ex)
      {
        Stop();
        error = $"{GameName} could not start: {ex.Message}";
        return false;
      }
    }

    // ---------------------------------------------------------------- what a game supplies

    /// <summary>The game has ended and shows its result screen.</summary>
    protected abstract bool IsOver { get; }
    /// <summary>The rules are paused (by the player, the countdown or boss mode).</summary>
    protected abstract bool IsGamePaused { get; }
    /// <summary>The rules are running, so boss mode has to pause them and count back in afterwards.</summary>
    protected abstract bool IsGameActive { get; }
    protected abstract void PauseGame();
    protected abstract void ResumeGame();
    protected abstract void TickGame(double dt);
    /// <summary>A key during play (Esc / P pause and the boss key are handled before this).</summary>
    protected abstract void GameKey(Keys key, bool down, bool repeat);
    /// <summary>Fresh rules for another round.</summary>
    protected abstract void NewGame();
    protected abstract void SetStartCamera(RhinoViewport vp);
    /// <summary>Prepares the disguise and its camera; returns the line to print on Rhino's command line.</summary>
    protected abstract string EnterDisguise(RhinoViewport vp);
    protected virtual void OnGameOver() { }
    /// <summary>Forget held moves and boosts (pause, boss mode, game over).</summary>
    protected virtual void ClearInput() { }
    /// <summary>Per-frame work that runs even while the rules are paused (camera animation).</summary>
    protected virtual void TickAlways(double dt) { }
    protected virtual bool NeedsRedraw => !IsGamePaused && !(IsOver && OverTime > 2);
    /// <summary>"Up" for the mouse orbit; elevation is clamped against it when <see cref="ClampElevation"/>.</summary>
    protected virtual Vector3d Up => Vector3d.ZAxis;
    protected virtual bool ClampElevation => true;
    protected virtual string GameName => "Moyu";

    // ---------------------------------------------------------------- shared state

    /// <summary>Paused by the player (not counting down, not over): show the pause screen.</summary>
    public bool IsPaused => IsGamePaused && Countdown <= 0 && !IsOver && !Boss;
    public bool ShowsResult => IsOver;

    protected RhinoViewport Viewport => View.ActiveViewport;
    protected void Changed() => _changed?.Invoke(this);
    protected void Redraw() => View.Redraw();
    bool ViewAlive() => Doc.Views.Find(ViewportId) != null;

    void StartCountdown(bool fresh)
    {
      PauseGame();
      Countdown = CountdownSeconds;
      CountdownIsStart = fresh;
      if (fresh) PlayTime = 0;
    }

    void Tick(double dt)
    {
      if (Stopped) return;
      try
      {
        if (!ViewAlive())
        {
          Stop();
          return;
        }
        Time += dt;
        if (AutoBoss && !Boss && !GameClock.AppIsForeground()) EnterBoss();
        if (Boss)
        {
          double before = BossTime;
          BossTime += dt;
          // The disguise's "analysing…" counter ticks over every 1.2 s.
          if ((int)(BossTime / 1.2) != (int)(before / 1.2)) Redraw();
          return;
        }
        bool command = InRhinoCommand;
        if (!IsOver && (command || !OwnsFocus()))
        {
          // Another Rhino window (Save As, "Save changes?", the Grasshopper editor) or a running command has the keys:
          // the countdown waits, and the rules pause until the player is back in the viewport and presses Esc.
          if (Countdown > 0)
          {
            Redraw();
            return;
          }
          if (IsGameActive) Pause();
        }
        // A named view or the viewport's title menu (Top, Parallel) would leave the game edge-on or without depth.
        if (!command && Viewport.IsParallelProjection) ResetCamera();
        TickAlways(dt);
        if (Countdown > 0)
        {
          Countdown -= dt;
          if (Countdown <= 0)
          {
            Countdown = 0;
            ResumeGame();
            // The owner last heard "paused" as the countdown began.
            Changed();
          }
          Redraw();
          return;
        }
        if (!IsGamePaused) PlayTime += dt;
        TickGame(dt);
        NoteGameOver();
        if (IsOver) OverTime += dt;
        if (NeedsRedraw) Redraw();
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine($"{GameName} stopped: {ex.GetType().Name}: {ex.Message}");
        Stop();
      }
    }

    /// <summary>
    /// Keys belong to the game only while Rhino's main window or the game view's own frame (when it floats) has the
    /// focus; the Grasshopper editor, dialogs and floating panels keep their own.
    /// </summary>
    bool OwnsFocus()
    {
      var root = KeyboardHook.FocusRoot();
      if (root == IntPtr.Zero) return false;
      return root == RhinoApp.MainWindowHandle() || root == KeyboardHook.Root(View.Handle);
    }

    /// <summary>A Rhino command is running (a prompt, a point pick): its keys and clicks are Rhino's.</summary>
    static bool InRhinoCommand => Rhino.Commands.Command.InCommand();

    // ---------------------------------------------------------------- keyboard

    bool OnKey(Keys key, bool down, bool repeat, bool act)
    {
      if (Stopped) return false;
      if (act)
      {
        if (down) Held.Add(key);
        else Held.Remove(key);
      }
      // Shortcuts go to Rhino, but a key let go under Ctrl or Alt still ends its steer, sprint or soft drop.
      if (key == Keys.LWin || key == Keys.RWin || IsCtrlOrAlt(key) || KeyboardHook.CtrlOrAltDown) return PassOn(key, down, act);
      if (!OwnsFocus()) return PassOn(key, down, act);
      bool press = act && down && !repeat;
      if (key == BossKey)
      {
        if (press) ToggleBoss();
        return true;
      }
      if (Boss) return false;
      // Esc cancels the command and its prompt gets what is typed; the game pauses on the next tick.
      if (InRhinoCommand) return PassOn(key, down, act);
      if (IsOver)
      {
        if (key == Keys.Enter || key == Keys.Space)
        {
          // A moment's grace, so a key mashed at the end does not skip the result screen.
          if (press && OverTime > 0.8) Restart();
          return true;
        }
        if (key == Keys.Escape)
        {
          if (press) Stop();
          return true;
        }
        return false;
      }
      if (Countdown > 0) return true;
      if (IsGamePaused)
      {
        // Paused, the keyboard is Rhino's: only Esc (resume) and Shift+Esc (quit) are taken.
        if (key == Keys.Escape)
        {
          if (press)
          {
            if (Held.Contains(Keys.ShiftKey)) Stop();
            else Resume();
          }
          return true;
        }
        return false;
      }
      // Playing: every plain key belongs to the game, so nothing leaks into Rhino's command line.
      if (key == Keys.Escape || key == Keys.P)
      {
        if (press) Pause();
        return true;
      }
      if (key == Keys.Home)
      {
        if (press) ResetCamera();
        return true;
      }
      if (act)
      {
        GameKey(key, down, repeat);
        // A VoxStack hard drop or Hold can top out right here, before the next frame.
        NoteGameOver();
      }
      return true;
    }

    /// <summary>
    /// Leaves a key to whoever has the keyboard, but lets a game in play see the release of a key it saw pressed, so
    /// a steer, sprint or soft drop does not stay on.
    /// </summary>
    bool PassOn(Keys key, bool down, bool act)
    {
      if (act && !down && !Boss && !IsOver && Countdown <= 0 && !IsGamePaused) GameKey(key, false, false);
      return false;
    }

    static bool IsCtrlOrAlt(Keys key) =>
      key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
      key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;

    // ---------------------------------------------------------------- states

    /// <summary>Scores the round and tells the owner, once per round, whether it ended in a frame or on a key.</summary>
    void NoteGameOver()
    {
      if (!IsOver || _overNoted) return;
      _overNoted = true;
      OverTime = 0;
      ClearInput();
      OnGameOver();
      Changed();
    }

    void Pause()
    {
      PauseGame();
      ClearInput();
      // A Shift held from play (VoxStack soft drop, Serpentine sprint) must not turn the Esc that resumes into the
      // Shift+Esc that quits: only a Shift pressed while paused counts.
      Held.Remove(Keys.ShiftKey);
      Changed();
      Redraw();
    }

    void Resume()
    {
      ResumeGame();
      Changed();
      Redraw();
    }

    public void Restart()
    {
      NewGame();
      _overNoted = false;
      // After NewGame, so a camera that follows the game (Serpentine's first person) starts from the new round.
      ResetCamera();
      StartCountdown(true);
      OverTime = 0;
      ClearInput();
      RhinoApp.SetFocusToMainWindow();
      Changed();
      Redraw();
    }

    /// <summary>The boss key.</summary>
    public void ToggleBoss()
    {
      if (Boss) ExitBoss();
      else EnterBoss(false);
    }

    /// <summary>Everything the boss could see changes in this one call; the next frame is the disguise.</summary>
    public void EnterBoss() => EnterBoss(true);

    /// <param name="keysLost">Rhino lost the focus, so key releases may have gone to another program.</param>
    void EnterBoss(bool keysLost)
    {
      if (Boss || Stopped) return;
      _pausedByBoss = Countdown > 0 || IsGameActive;
      PauseGame();
      Countdown = 0;
      ClearInput();
      Held.Clear();
      // Only a focus loss hides releases from the hook. From the boss key it has seen them all, and forgetting the
      // boss key would make its own auto-repeat count as a new press that switches boss mode straight back off.
      if (keysLost) _hook.Reset();
      var vp = Viewport;
      _gameCamera = new ViewportInfo(vp);
      string line = EnterDisguise(vp);
      Boss = true;
      BossTime = 0;
      Redraw();
      if (!string.IsNullOrEmpty(line)) RhinoApp.WriteLine(line);
      Changed();
    }

    public void ExitBoss()
    {
      if (!Boss) return;
      if (!ViewAlive())
      {
        Stop();
        return;
      }
      Boss = false;
      if (_gameCamera != null) Viewport.SetViewProjection(_gameCamera, true);
      if (_pausedByBoss && !IsOver) StartCountdown(false);
      Redraw();
      Changed();
    }

    public void Stop()
    {
      if (Stopped) return;
      Stopped = true;
      _clock?.Dispose();
      _hook?.Dispose();
      if (_mouse != null) _mouse.Enabled = false;
      if (_conduit != null)
      {
        _conduit.Enabled = false;
        _conduit.Release();
      }
      RhinoDoc.CloseDocument -= OnCloseDocument;
      RhinoApp.Closing -= OnRhinoClosing;
      try
      {
        if (ViewAlive())
        {
          var vp = Viewport;
          vp.SetViewProjection(_userView, true);
          vp.ConstructionGridVisible = _userGrid;
          vp.ConstructionAxesVisible = _userAxes;
          vp.WorldAxesVisible = _userWorldAxes;
          View.Maximized = _userMaximized;
          // Started from a layout page: go back to it.
          if (_userActiveView != Guid.Empty && _userActiveView != View.MainViewport.Id)
          {
            var before = Doc.Views.Find(_userActiveView);
            if (before != null) Doc.Views.ActiveView = before;
          }
        }
      }
      catch (Exception) { /* the view may be closing with the document */ }
      if (Current == this) Current = null;
      // Stop also runs from a timer tick or the hook, where nothing else would catch a failing callback.
      try
      {
        Changed();
        Doc.Views.Redraw();
      }
      catch (Exception ex) { RhinoApp.WriteLine($"{GameName}: {ex.GetType().Name}: {ex.Message}"); }
    }

    void OnCloseDocument(object sender, DocumentEventArgs e)
    {
      if (e.Document == Doc) Stop();
    }

    void OnRhinoClosing(object sender, EventArgs e) => Stop();

    // ---------------------------------------------------------------- camera

    protected double Elevation(Vector3d v)
    {
      double len = v.Length;
      return len > 0 ? Math.Asin(Math.Max(-1, Math.Min(1, v * Up / len))) : 0;
    }

    /// <summary>
    /// Back to the game's opening camera, in perspective (Home, a restart, or a named or parallel view chosen from
    /// Rhino). Virtual so a game can also drop camera state of its own.
    /// </summary>
    protected virtual void ResetCamera()
    {
      var vp = Viewport;
      if (vp.IsParallelProjection) vp.ChangeToPerspectiveProjection(true, 28);
      SetStartCamera(vp);
      Redraw();
    }

    public bool CanOrbit(RhinoView view) => !Stopped && !Boss && view != null && view.ActiveViewportID == ViewportId;

    /// <summary>What the mouse is doing in the game viewport.</summary>
    public enum MouseAction { Hover, Down, Drag, Up }

    /// <summary>
    /// The mouse in the game viewport (not in boss mode). Return true to take it: a Down taken this way also takes
    /// the following Drags and the Up, and the mouse orbit stays off for that stroke.
    /// </summary>
    protected virtual bool GameMouse(MouseAction action, System.Drawing.Point client, MouseButton button) => false;

    internal bool HandleMouse(RhinoView view, MouseAction action, System.Drawing.Point client, MouseButton button)
    {
      if (!CanOrbit(view)) return false;
      bool taken = GameMouse(action, client, button);
      NoteGameOver();
      return taken;
    }

    /// <summary>Where a viewport pixel lands on <paramref name="plane"/>; false when the view runs parallel to it.</summary>
    protected bool ClientToPlane(System.Drawing.Point client, Plane plane, out Point3d point)
    {
      point = Point3d.Unset;
      var ray = Viewport.ClientToWorld(new Point2d(client.X, client.Y));
      if (!Rhino.Geometry.Intersect.Intersection.LinePlane(ray, plane, out double t)) return false;
      point = ray.PointAt(t);
      return point.IsValid;
    }

    /// <summary>Left- or right-drag orbit around the camera target.</summary>
    public virtual void Orbit(int dx, int dy)
    {
      var vp = Viewport;
      var up = Up;
      var target = vp.CameraTarget;
      var v = vp.CameraLocation - target;
      v.Transform(Transform.Rotation(-dx * 0.008, up, Point3d.Origin));
      var axis = Vector3d.CrossProduct(v, up);
      if (axis.Unitize())
      {
        var pitched = v;
        pitched.Transform(Transform.Rotation(dy * 0.008, axis, Point3d.Origin));
        double e = Elevation(pitched);
        if (!ClampElevation || (e >= MinElevation && e <= MaxElevation)) v = pitched;
      }
      vp.SetCameraLocations(target, target + v);
      vp.CameraUp = up;
      Redraw();
    }

    /// <summary>Pushes the camera back above the horizon after something other than <see cref="Orbit"/> dipped it below.</summary>
    protected void KeepCameraAbove()
    {
      var vp = Viewport;
      var up = Up;
      var v = vp.CameraLocation - vp.CameraTarget;
      if (Elevation(v) >= MinElevation) return;
      var flat = v - (v * up) * up;
      if (!flat.Unitize()) return;
      var fixedDir = flat * Math.Cos(MinElevation) + up * Math.Sin(MinElevation);
      vp.SetCameraLocations(vp.CameraTarget, vp.CameraTarget + fixedDir * v.Length);
      vp.CameraUp = up;
    }

    /// <summary>
    /// The mouse in the game viewport: left- and right-drag orbit (no selection window, and no right click for Rhino
    /// to take as Enter and repeat the last command), the middle button does nothing (no popup toolbar over the
    /// game). While a Rhino command runs, every new press is Rhino's.
    /// </summary>
    sealed class OrbitMouse : MouseCallback
    {
      readonly GameSession _session;
      // Buttons whose press was kept from Rhino, so their release is kept from it too.
      readonly HashSet<MouseButton> _taken = new HashSet<MouseButton>();
      System.Drawing.Point _last;
      MouseButton _orbit = MouseButton.None, _gameDrag = MouseButton.None;

      public OrbitMouse(GameSession session) => _session = session;

      void Take(MouseCallbackEventArgs e)
      {
        _taken.Add(e.MouseButton);
        e.Cancel = true;
      }

      protected override void OnMouseDown(MouseCallbackEventArgs e)
      {
        // A new press ends whatever stroke of this button lost its release.
        _taken.Remove(e.MouseButton);
        if (_orbit == e.MouseButton) _orbit = MouseButton.None;
        if (InRhinoCommand || !_session.CanOrbit(e.View)) return;
        if (_session.HandleMouse(e.View, MouseAction.Down, e.ViewportPoint, e.MouseButton))
        {
          _gameDrag = e.MouseButton;
          Take(e);
          return;
        }
        bool orbits = e.MouseButton == MouseButton.Left || e.MouseButton == MouseButton.Right;
        if (!orbits && e.MouseButton != MouseButton.Middle) return;
        if (orbits && _orbit == MouseButton.None)
        {
          _orbit = e.MouseButton;
          _last = e.ViewportPoint;
        }
        Take(e);
      }

      // A quick second press comes as a double click, which Rhino sends here and never to OnMouseDown: without this
      // every other fast click (a Swift flap) is lost and Rhino runs its own double-click action under the game.
      protected override void OnMouseDoubleClick(MouseCallbackEventArgs e) => OnMouseDown(e);

      protected override void OnMouseMove(MouseCallbackEventArgs e)
      {
        if (_gameDrag != MouseButton.None)
        {
          _session.HandleMouse(e.View, MouseAction.Drag, e.ViewportPoint, _gameDrag);
          e.Cancel = true;
          return;
        }
        if (_orbit == MouseButton.None)
        {
          _session.HandleMouse(e.View, MouseAction.Hover, e.ViewportPoint, MouseButton.None);
          return;
        }
        var p = e.ViewportPoint;
        if (_session.CanOrbit(e.View)) _session.Orbit(p.X - _last.X, p.Y - _last.Y);
        _last = p;
        e.Cancel = true;
      }

      protected override void OnMouseUp(MouseCallbackEventArgs e)
      {
        if (_gameDrag != MouseButton.None && e.MouseButton == _gameDrag)
        {
          _gameDrag = MouseButton.None;
          _session.HandleMouse(e.View, MouseAction.Up, e.ViewportPoint, e.MouseButton);
        }
        else if (e.MouseButton == _orbit) _orbit = MouseButton.None;
        if (_taken.Remove(e.MouseButton)) e.Cancel = true;
      }
    }
  }
}
