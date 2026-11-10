// Offline frame-by-frame recorder for Moyu's viewport games and solvers.
//
// The library is built outside Rhino and loaded into a running Rhino 8 with Assembly.Load(bytes) from a short
// run_csharp script. A shot (IShot) sets a scene up in a floating portrait viewport; the recorder then advances the
// scene by a fixed 1/60 s per frame, captures the view at 1080x1920 and pipes the raw frames to ffmpeg. Nothing runs
// in real time, so the footage is smooth however long a frame takes to draw.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Moyu.Games.Common;
using Rhino;
using Rhino.Display;

namespace MoyuRec
{
  public interface IShot
  {
    /// <summary>Frames to record at <see cref="Rec.Fps"/>.</summary>
    int Frames { get; }
    /// <summary>Builds the scene (start the game session, set the camera).</summary>
    void Setup(Rec rec);
    /// <summary>Advances the scene to frame <paramref name="i"/> (bot input + one fixed step). Runs before the capture.</summary>
    void Frame(Rec rec, int i);
    void Teardown(Rec rec);
  }

  /// <summary>
  /// A shot that captures its own frames (for example whole Rhino and Grasshopper windows) instead of the portrait view.
  /// The recorder then makes no floating view, and <see cref="Rec.View"/> is the active view.
  /// </summary>
  public interface ICustomCapture
  {
    Size FrameSize { get; }
    /// <summary>The frame, exactly <see cref="FrameSize"/>; the recorder disposes it.</summary>
    Bitmap Capture(Rec rec);
  }

  /// <summary>What a shot works with: the document, the portrait view, and offline control of a game session.</summary>
  public sealed class Rec
  {
    public const string ViewName = "MoyuCam";
    public RhinoDoc Doc;
    public RhinoView View;
    /// <summary>Size of the video; the view is captured at CaptureWidth x CaptureHeight and scaled down to it.</summary>
    public int Width = 1080, Height = 1920, Fps = 60;
    public int CaptureWidth = 1080, CaptureHeight = 1920;
    /// <summary>On-screen size of the floating view (pixels). Same 9:16 shape as the video.</summary>
    public int ViewWidth = 540, ViewHeight = 960;
    public double Dt => 1.0 / Fps;
    public Dictionary<string, string> Args = new Dictionary<string, string>();
    public GameSession Session;
    public readonly StringBuilder Log = new StringBuilder();

    public string Arg(string key, string fallback = null) => Args.TryGetValue(key, out var v) ? v : fallback;
    public double Arg(string key, double fallback) => Args.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : fallback;
    public void Note(string line) => Log.AppendLine(line);

    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Base = typeof(GameSession);

    static object Get(object o, Type t, string name)
    {
      var p = t.GetProperty(name, Inst);
      if (p != null) return p.GetValue(o);
      var f = t.GetField(name, Inst) ?? throw new MissingMemberException(t.Name, name);
      return f.GetValue(o);
    }

    static void Set(object o, Type t, string name, object value)
    {
      var p = t.GetProperty(name, Inst);
      if (p != null) { p.SetValue(o, value); return; }
      var f = t.GetField(name, Inst) ?? throw new MissingMemberException(t.Name, name);
      f.SetValue(o, value);
    }

    static object Call(object o, Type t, string name, params object[] args)
    {
      for (var type = t; type != null; type = type.BaseType)
      {
        var m = type.GetMethods(Inst | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
        if (m != null) return m.Invoke(o, args);
      }
      throw new MissingMethodException(t.Name, name);
    }

    /// <summary>Private or protected members of the running session (looked up on its own type, then the base).</summary>
    public object SGet(string name)
    {
      var t = Session.GetType();
      return (t.GetProperty(name, Inst) != null || t.GetField(name, Inst) != null) ? Get(Session, t, name) : Get(Session, Base, name);
    }
    public void SSet(string name, object value)
    {
      var t = Session.GetType();
      if (t.GetProperty(name, Inst)?.CanWrite == true || t.GetField(name, Inst) != null) Set(Session, t, name, value);
      else Set(Session, Base, name, value);
    }
    public object SCall(string name, params object[] args) => Call(Session, Session.GetType(), name, args);

    /// <summary>
    /// Takes a freshly started session off real time: no clock, no auto boss mode on focus loss, no mouse steering.
    /// From here on only <see cref="Step"/> moves it.
    /// </summary>
    public void Adopt(GameSession session)
    {
      Session = session ?? throw new ArgumentNullException(nameof(session));
      session.AutoBoss = false;
      (Get(session, Base, "_clock") as IDisposable)?.Dispose();
      var mouse = Get(session, Base, "_mouse");
      mouse?.GetType().GetProperty("Enabled")?.SetValue(mouse, false);
      // GameSession.Begin maximises the view, which blows a floating view up to the whole screen: back to 9:16.
      if (View != null && View.Floating && View.ActiveViewport.Name == ViewName)
      {
        if (View.Maximized) View.Maximized = false;
        Views.Portrait(Doc, ViewWidth, ViewHeight);
      }
    }

    /// <summary>GameSession.Tick without the focus checks: one fixed step of the countdown, the rules and the camera.</summary>
    public void Step(double dt)
    {
      var s = Session;
      if (s == null || s.Stopped) return;
      Set(s, Base, "Time", s.Time + dt);
      if (s.Boss)
      {
        Set(s, Base, "BossTime", s.BossTime + dt);
        return;
      }
      SCall("TickAlways", dt);
      if (s.Countdown > 0)
      {
        double left = s.Countdown - dt;
        Set(s, Base, "Countdown", Math.Max(0, left));
        if (left <= 0)
        {
          SCall("ResumeGame");
          SCall("Changed");
        }
        return;
      }
      if (!(bool)SGet("IsGamePaused")) Set(s, Base, "PlayTime", s.PlayTime + dt);
      SCall("TickGame", dt);
      SCall("NoteGameOver");
      if ((bool)SGet("IsOver")) Set(s, Base, "OverTime", s.OverTime + dt);
    }

    public void Step() => Step(Dt);

    /// <summary>Skips the 3-2-1 countdown (or shortens it to <paramref name="left"/> seconds).</summary>
    public void SetCountdown(double left)
    {
      Set(Session, Base, "Countdown", left);
      if (left <= 0)
      {
        SCall("ResumeGame");
        SCall("Changed");
      }
    }

    /// <summary>A key as the game sees it while playing (the hook's focus and pause handling are bypassed).</summary>
    public void Key(Keys key, bool down, bool repeat = false)
    {
      var held = (HashSet<Keys>)Get(Session, Base, "Held");
      if (down) held.Add(key);
      else held.Remove(key);
      SCall("GameKey", key, down, repeat);
      SCall("NoteGameOver");
    }

    public void Tap(Keys key)
    {
      Key(key, true);
      Key(key, false);
    }
  }

  /// <summary>Entry points called from the run_csharp loader by reflection.</summary>
  public static class Entry
  {
    static readonly Dictionary<string, Func<IShot>> Shots = new Dictionary<string, Func<IShot>>(StringComparer.OrdinalIgnoreCase);
    static Job _job;

    public static void Register(string name, Func<IShot> make) => Shots[name] = make;

    static Entry()
    {
      // Every IShot with a parameterless constructor in this assembly registers under its class name.
      foreach (var t in typeof(Entry).Assembly.GetTypes())
        if (typeof(IShot).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
        {
          var type = t;
          Shots[t.Name] = () => (IShot)Activator.CreateInstance(type);
        }
    }

    /// <summary>
    /// Starts recording a shot in the background of Rhino's UI thread and returns at once.
    /// args: "key=value;key=value" (frames=…, scale=0.5 for a quick half-size preview, ss=1.5 to capture larger and
    /// scale down (smaller HUD text, smoother edges), plus the shot's own keys).
    /// </summary>
    public static string Run(RhinoDoc doc, string shot, string outPath, string args)
    {
      if (_job != null && !_job.Done) return "busy: " + _job.Describe();
      if (!Shots.TryGetValue(shot, out var make)) return "unknown shot '" + shot + "'. Known: " + string.Join(", ", Shots.Keys);
      var rec = new Rec { Doc = doc };
      foreach (var pair in (args ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
      {
        int eq = pair.IndexOf('=');
        if (eq > 0) rec.Args[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
      }
      double scale = rec.Arg("scale", 1.0);
      rec.Width = (int)Math.Round(1080 * scale / 2) * 2;
      rec.Height = (int)Math.Round(1920 * scale / 2) * 2;
      double ss = Math.Max(1, rec.Arg("ss", 1.0));
      rec.CaptureWidth = (int)Math.Round(rec.Width * ss / 2) * 2;
      rec.CaptureHeight = (int)Math.Round(rec.Height * ss / 2) * 2;
      _job = new Job(rec, make(), shot, outPath);
      string error = null;
      RhinoApp.InvokeOnUiThread(new Action(() =>
      {
        try { _job.Start(); }
        catch (Exception ex) { error = ex.ToString(); _job.Fail(ex); }
      }));
      return error ?? "started: " + _job.Describe();
    }

    public static string Status() => _job == null ? "idle" : _job.Describe();

    public static string Cancel()
    {
      if (_job == null) return "idle";
      RhinoApp.InvokeOnUiThread(new Action(() => _job.Finish("cancelled")));
      return "cancelling";
    }

    public static string List() => string.Join(", ", Shots.Keys.OrderBy(k => k));
  }

  sealed class Job
  {
    readonly Rec _rec;
    readonly IShot _shot;
    ICustomCapture _custom;
    readonly string _name, _out;
    System.Windows.Forms.Timer _timer;
    Process _ffmpeg;
    Stream _pipe;
    byte[] _buffer;
    int _frame, _total;
    string _state = "new";
    readonly Stopwatch _wall = new Stopwatch();
    public bool Done { get; private set; }

    public Job(Rec rec, IShot shot, string name, string outPath)
    {
      _rec = rec;
      _shot = shot;
      _name = name;
      _out = outPath;
    }

    public string Describe() =>
      $"{_name} {_state} frame {_frame}/{_total} {_rec.Width}x{_rec.Height} (capture {_rec.CaptureWidth}x{_rec.CaptureHeight}) elapsed {_wall.Elapsed.TotalSeconds:F1}s -> {_out}" +
      (_rec.Log.Length > 0 ? "\n" + _rec.Log : "");

    public void Start()
    {
      _wall.Start();
      _state = "setup";
      _custom = _shot as ICustomCapture;
      if (_custom != null)
      {
        _rec.Width = _rec.CaptureWidth = _custom.FrameSize.Width;
        _rec.Height = _rec.CaptureHeight = _custom.FrameSize.Height;
        _rec.View = _rec.Doc.Views.ActiveView;
      }
      else
      {
        _rec.ViewWidth = (int)_rec.Arg("vw", 540);
        _rec.ViewHeight = _rec.ViewWidth * 16 / 9;
        _rec.View = Views.Portrait(_rec.Doc, _rec.ViewWidth, _rec.ViewHeight);
        _rec.Doc.Views.ActiveView = _rec.View;
      }
      _shot.Setup(_rec);
      _total = (int)_rec.Arg("frames", _shot.Frames);
      Directory.CreateDirectory(Path.GetDirectoryName(_out));
      string ffmpeg = _rec.Arg("ffmpeg", "ffmpeg");  // pass ffmpeg=<full path> in the args if it is not on PATH
      var info = new ProcessStartInfo(ffmpeg,
        $"-y -loglevel error -f rawvideo -pix_fmt bgra -s {_rec.CaptureWidth}x{_rec.CaptureHeight} -r {_rec.Fps} -i - " +
        (_rec.CaptureWidth != _rec.Width ? $"-vf scale={_rec.Width}:{_rec.Height}:flags=lanczos " : "") +
        $"-c:v libx264 -preset veryfast -crf 10 -g 15 -pix_fmt yuv420p -r {_rec.Fps} \"{_out}\"")
      {
        UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true,
      };
      _ffmpeg = Process.Start(info);
      _pipe = _ffmpeg.StandardInput.BaseStream;
      _buffer = new byte[_rec.CaptureWidth * _rec.CaptureHeight * 4];
      _state = "recording";
      _timer = new System.Windows.Forms.Timer { Interval = 1 };
      _timer.Tick += (s, e) => Pump();
      _timer.Start();
    }

    void Pump()
    {
      if (Done) return;
      var slice = Stopwatch.StartNew();
      try
      {
        // A few frames per timer tick, so Rhino's message loop (and the status calls) keep running.
        while (_frame < _total && slice.ElapsedMilliseconds < 150)
        {
          _shot.Frame(_rec, _frame);
          Capture();
          _frame++;
        }
        if (_frame >= _total) Finish("done");
      }
      catch (Exception ex) { Fail(ex); }
    }

    void Capture()
    {
      int cw = _rec.CaptureWidth, ch = _rec.CaptureHeight;
      using (var bmp = _custom != null ? _custom.Capture(_rec) : _rec.View.CaptureToBitmap(new Size(cw, ch)))
      {
        if (bmp == null) throw new InvalidOperationException("CaptureToBitmap returned null");
        if (bmp.Width != cw || bmp.Height != ch)
          throw new InvalidOperationException($"capture is {bmp.Width}x{bmp.Height}, expected {cw}x{ch}");
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
          int row = cw * 4;
          for (int y = 0; y < bmp.Height; y++)
            Marshal.Copy(data.Scan0 + y * data.Stride, _buffer, y * row, row);
        }
        finally { bmp.UnlockBits(data); }
        _pipe.Write(_buffer, 0, _buffer.Length);
      }
      // Let a person watching the Rhino window see where the recording is.
      if (_custom == null && _frame % 20 == 0) _rec.View.Redraw();
    }

    public void Fail(Exception ex)
    {
      _rec.Note("ERROR " + ex);
      Finish("failed");
    }

    public void Finish(string state)
    {
      if (Done) return;
      Done = true;
      _timer?.Stop();
      _timer?.Dispose();
      try { _pipe?.Flush(); _pipe?.Close(); } catch (Exception ex) { _rec.Note("pipe: " + ex.Message); }
      try { if (_ffmpeg != null && !_ffmpeg.WaitForExit(60000)) _rec.Note("ffmpeg did not exit"); else if (_ffmpeg != null) _rec.Note("ffmpeg exit " + _ffmpeg.ExitCode); }
      catch (Exception ex) { _rec.Note("ffmpeg: " + ex.Message); }
      try { _shot.Teardown(_rec); } catch (Exception ex) { _rec.Note("teardown: " + ex.Message); }
      _wall.Stop();
      _state = state;
      try { File.WriteAllText(_out + ".status.txt", Describe()); } catch (Exception) { }
    }
  }

  /// <summary>The floating 9:16 view every shot records in.</summary>
  public static class Views
  {
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    public static RhinoView Portrait(RhinoDoc doc, int width = 540, int height = 960)
    {
      var view = doc.Views.FirstOrDefault(v => v.ActiveViewport.Name == Rec.ViewName);
      if (view == null)
        view = doc.Views.Add(Rec.ViewName, DefinedViewportProjection.Perspective, new Rectangle(120, 40, width + 16, height + 40), true);
      // The frame's borders are not known in advance: nudge the window until the viewport itself is width x height.
      var root = GetAncestor(view.Handle, 2);
      for (int i = 0; i < 4; i++)
      {
        var size = view.ActiveViewport.Size;
        if (size.Width == width && size.Height == height) break;
        if (!GetWindowRect(root, out var r)) break;
        SetWindowPos(root, IntPtr.Zero, 0, 0, r.Right - r.Left + width - size.Width, r.Bottom - r.Top + height - size.Height, 0x0002 | 0x0004 | 0x0010);
        view.Redraw();
        Application.DoEvents();
      }
      return view;
    }
  }
}
