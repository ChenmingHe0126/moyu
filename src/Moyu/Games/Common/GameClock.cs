using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Moyu.Games.Common
{
  /// <summary>
  /// Frame timer on the UI thread (WinForms timer, ~60 Hz), handing the real elapsed seconds to the game. Runs
  /// outside Grasshopper's solution loop, so a frame costs a viewport redraw and nothing else.
  /// </summary>
  public sealed class GameClock : IDisposable
  {
    readonly System.Windows.Forms.Timer _timer;
    readonly Stopwatch _watch = Stopwatch.StartNew();
    readonly Action<double> _tick;
    double _last;

    public GameClock(Action<double> tick, int intervalMs = 15)
    {
      _tick = tick;
      _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
      _timer.Tick += OnTick;
      _timer.Start();
    }

    void OnTick(object sender, EventArgs e)
    {
      double now = _watch.Elapsed.TotalSeconds;
      // A long stall (a modal dialog, a heavy Grasshopper solve) must not fast-forward the game.
      double dt = Math.Min(0.1, now - _last);
      _last = now;
      try { _tick(dt); }
      catch (Exception ex)
      {
        // Nothing above a WinForms timer catches for us; a frame that throws must not reach Rhino's message pump.
        _timer.Stop();
        Rhino.RhinoApp.WriteLine($"Moyu clock stopped: {ex.GetType().Name}: {ex.Message}");
      }
    }

    public void Dispose()
    {
      _timer.Stop();
      _timer.Tick -= OnTick;
      _timer.Dispose();
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    /// <summary>A window of this process (Rhino, Grasshopper, a Rhino dialog) is in front. True off Windows.</summary>
    public static bool AppIsForeground()
    {
      if (Environment.OSVersion.Platform != PlatformID.Win32NT) return true;
      var hwnd = GetForegroundWindow();
      if (hwnd == IntPtr.Zero) return false;
      GetWindowThreadProcessId(hwnd, out uint pid);
      return pid == ProcessId;
    }

    static readonly uint ProcessId;

    static GameClock()
    {
      using (var p = Process.GetCurrentProcess()) ProcessId = (uint)p.Id;
    }
  }
}
