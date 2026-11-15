using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moyu.Platform;
using Moyu.Platform.Mac;

namespace Moyu.Games.Common
{
  /// <summary>
  /// Frame timer on the UI thread (~60 Hz), handing the real elapsed seconds to the game. Runs outside Grasshopper's
  /// solution loop, so a frame costs a viewport redraw and nothing else. A WinForms timer on Windows, an Eto timer
  /// (an NSTimer on Rhino's run loop) on the Mac.
  /// </summary>
  public sealed class GameClock : IDisposable
  {
    readonly IFrameTimer _timer;
    readonly Stopwatch _watch = Stopwatch.StartNew();
    readonly Action<double> _tick;
    double _last;

    public GameClock(Action<double> tick, int intervalMs = 15)
    {
      _tick = tick;
      _timer = OS.IsWindows ? WinFormsTimer(intervalMs, OnTick) : EtoTimer(intervalMs, OnTick);
    }

    void OnTick()
    {
      double now = _watch.Elapsed.TotalSeconds;
      // A long stall (a modal dialog, a heavy Grasshopper solve) must not fast-forward the game.
      double dt = Math.Min(0.1, now - _last);
      _last = now;
      try { _tick(dt); }
      catch (Exception ex)
      {
        // Nothing above a UI timer catches for us; a frame that throws must not reach Rhino's message pump.
        _timer.Stop();
        Rhino.RhinoApp.WriteLine($"Moyu clock stopped: {ex.GetType().Name}: {ex.Message}");
      }
    }

    public void Dispose() => _timer.Dispose();

    interface IFrameTimer : IDisposable
    {
      void Stop();
    }

    // Each timer is made in a method of its own, so the JIT never looks at the other platform's timer type.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static IFrameTimer WinFormsTimer(int intervalMs, Action tick) => new WinForms(intervalMs, tick);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static IFrameTimer EtoTimer(int intervalMs, Action tick) => new Eto(intervalMs, tick);

    sealed class WinForms : IFrameTimer
    {
      readonly System.Windows.Forms.Timer _timer;
      readonly Action _tick;

      public WinForms(int intervalMs, Action tick)
      {
        _tick = tick;
        _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _timer.Tick += OnTick;
        _timer.Start();
      }

      void OnTick(object sender, EventArgs e) => _tick();

      public void Stop() => _timer.Stop();

      public void Dispose()
      {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer.Dispose();
      }
    }

    sealed class Eto : IFrameTimer
    {
      readonly global::Eto.Forms.UITimer _timer;
      readonly Action _tick;

      public Eto(int intervalMs, Action tick)
      {
        _tick = tick;
        _timer = new global::Eto.Forms.UITimer { Interval = intervalMs / 1000.0 };
        _timer.Elapsed += OnElapsed;
        _timer.Start();
      }

      void OnElapsed(object sender, EventArgs e) => _tick();

      public void Stop() => _timer.Stop();

      public void Dispose()
      {
        _timer.Stop();
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
      }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    static bool s_macActiveFailed;

    /// <summary>
    /// A window of this process (Rhino, Grasshopper, a Rhino dialog) is in front; on the Mac, Rhino is the active app.
    /// True where that cannot be told.
    /// </summary>
    public static bool AppIsForeground()
    {
      if (OS.IsMac)
      {
        if (s_macActiveFailed) return true;
        try { return MacInterop.AppIsActive(); }
        catch (Exception)
        {
          s_macActiveFailed = true;
          return true;
        }
      }
      if (!OS.IsWindows) return true;
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
