using System;
using System.Diagnostics;
using Moyu.Games.Common.Core;

namespace Moyu.Platform.Mac
{
  /// <summary>
  /// The macOS counterpart of the Windows keyboard hook: an AppKit local event monitor that sees every key press and
  /// release in Rhino before the viewport, the command line or an input method does, reports it as a Windows virtual
  /// key (<see cref="MacKeys"/>) and lets the handler swallow it. Only Rhino's own key events arrive, so other apps
  /// are never affected. Rhino-free, so Moyu.MacTests drives it with real AppKit events.
  /// </summary>
  public sealed class MacKeyMonitor : IDisposable
  {
    /// <summary>
    /// <paramref name="act"/> is true exactly once per press and once per release; false for auto-repeat. Return true
    /// to swallow. Modifier keys (Shift, Control, Option, Command) are reported but always reach Rhino as well.
    /// </summary>
    public delegate bool Handler(int virtualKey, bool down, bool repeat, bool act);

    readonly Handler _handler;
    readonly KeyTransitions _keys = new KeyTransitions();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Func<IntPtr, IntPtr> _onEvent;
    IntPtr _monitor;

    public MacKeyMonitor(Handler handler)
    {
      _handler = handler;
      _onEvent = OnEvent;
      try { _monitor = MacInterop.AddKeyMonitor(_onEvent); }
      catch (Exception ex) { InstallError = ex.Message; }
      if (_monitor == IntPtr.Zero && InstallError == null) InstallError = "AppKit returned no event monitor.";
    }

    public bool Installed => _monitor != IntPtr.Zero;

    /// <summary>Why the monitor could not be installed; null when it was.</summary>
    public string InstallError { get; }

    /// <summary>Control, Option or Command held: a shortcut, which always goes through to Rhino.</summary>
    public static bool ShortcutModifierDown => (MacInterop.CurrentModifierFlags() & MacKeys.ShortcutFlags) != 0;

    /// <summary>Forgets which keys are down, e.g. after Rhino lost focus and missed their release.</summary>
    public void Reset() => _keys.Reset();

    IntPtr OnEvent(IntPtr nsEvent)
    {
      ulong type = MacInterop.EventType(nsEvent);
      int code = MacInterop.KeyCode(nsEvent);
      bool modifier = type == MacInterop.FlagsChanged;
      bool up, repeat = false;
      int vk;
      if (modifier)
      {
        bool? down = MacKeys.ModifierDown(code, MacInterop.ModifierFlags(nsEvent));
        if (down == null) return nsEvent;
        up = !down.Value;
        vk = MacKeys.ToVirtualKey(code, null);
      }
      else if (type == MacInterop.KeyDown || type == MacInterop.KeyUp)
      {
        up = type == MacInterop.KeyUp;
        if (!up) repeat = MacInterop.IsARepeat(nsEvent);
        vk = MacKeys.ToVirtualKey(code, MacInterop.CharactersIgnoringModifiers(nsEvent));
      }
      else return nsEvent;
      if (vk == 0) return nsEvent;
      bool fresh = _keys.Fresh(vk, up, repeat, _clock.ElapsedMilliseconds);
      bool swallow = _handler(vk, !up, !up && !fresh, fresh);
      // A modifier's own event always reaches Rhino, so its idea of which modifiers are held stays right.
      return swallow && !modifier ? IntPtr.Zero : nsEvent;
    }

    public void Dispose()
    {
      if (_monitor == IntPtr.Zero) return;
      MacInterop.RemoveKeyMonitor(_monitor, _onEvent);
      _monitor = IntPtr.Zero;
    }
  }
}
