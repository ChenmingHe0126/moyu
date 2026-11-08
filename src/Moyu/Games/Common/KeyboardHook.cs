using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Moyu.Games.Common.Core;

namespace Moyu.Games.Common
{
  /// <summary>
  /// Thread keyboard hook on Rhino's UI thread (Windows only). The handler sees every key press and release before
  /// Rhino, the viewport or the Grasshopper canvas do, and decides whether to swallow it: that is what stops the
  /// arrow keys panning the view and Space repeating the last command while a game runs. Only keys sent to this
  /// thread arrive, so other programs are never affected.
  /// </summary>
  public sealed class KeyboardHook : IDisposable
  {
    /// <summary>
    /// <paramref name="act"/> is true exactly once per press and once per release; it is false for auto-repeat and
    /// for a second notification of the same message. Decide about swallowing either way. Return true to swallow.
    /// </summary>
    public delegate bool Handler(Keys key, bool down, bool repeat, bool act);

    const int WH_KEYBOARD = 2, HC_ACTION = 0, VK_CONTROL = 0x11, VK_MENU = 0x12;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetKeyState(int vk);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("imm32.dll")]
    static extern IntPtr ImmGetContext(IntPtr hwnd);

    [DllImport("imm32.dll")]
    static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);

    [DllImport("imm32.dll")]
    static extern bool ImmGetOpenStatus(IntPtr himc);

    [DllImport("imm32.dll")]
    static extern bool ImmSetOpenStatus(IntPtr himc, bool open);

    [DllImport("imm32.dll")]
    static extern bool ImmNotifyIME(IntPtr himc, int action, int index, int value);

    const uint MAPVK_VSC_TO_VK_EX = 3, GA_ROOT = 2, BossScanCode = 0x29;
    const int NI_COMPOSITIONSTR = 0x15, CPS_CANCEL = 0x4;

    readonly Handler _handler;
    readonly List<IntPtr> _imeClosed = new List<IntPtr>();
    // Which keys are down, so a press or release is acted on once however many times it is reported.
    readonly KeyTransitions _keys = new KeyTransitions();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    // Held in a field so the garbage collector cannot free the delegate while Windows still calls it.
    readonly HookProc _proc;
    IntPtr _hook;

    public KeyboardHook(Handler handler)
    {
      _handler = handler;
      _proc = Proc;
      if (Environment.OSVersion.Platform == PlatformID.Win32NT)
      {
        _hook = SetWindowsHookEx(WH_KEYBOARD, _proc, IntPtr.Zero, GetCurrentThreadId());
        if (_hook == IntPtr.Zero) InstallError = Marshal.GetLastWin32Error();
      }
    }

    public bool Installed => _hook != IntPtr.Zero;

    /// <summary>The Win32 error when the hook could not be set on Windows; 0 otherwise.</summary>
    public int InstallError { get; }

    /// <summary>Ctrl or Alt held: shortcuts such as Ctrl+S and Alt+Tab always go through.</summary>
    public static bool CtrlOrAltDown => (GetKeyState(VK_CONTROL) & 0x8000) != 0 || (GetKeyState(VK_MENU) & 0x8000) != 0;

    /// <summary>The top-level window owning the focused control, or zero when nothing on this thread has focus.</summary>
    public static IntPtr FocusRoot()
    {
      var focus = GetFocus();
      return focus == IntPtr.Zero ? IntPtr.Zero : Root(focus);
    }

    /// <summary>The top-level window a control sits in (a floating viewport's own frame, not Rhino's).</summary>
    public static IntPtr Root(IntPtr hwnd) => hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT);

    IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
    {
      if (code >= 0)
      {
        long flags = lParam.ToInt64();
        bool up = (flags & 0x80000000L) != 0;
        bool wasDown = (flags & 0x40000000L) != 0;
        var key = (Keys)(int)(wParam.ToInt64() & 0xFFFF);
        bool extended = (flags & 0x01000000L) != 0;
        uint scanCode = (uint)((flags >> 16) & 0xFF);
        bool ime = key == Keys.ProcessKey;
        if (ime)
        {
          // A Chinese/Japanese input method took the key; recover which key it was from the scan code.
          uint scan = scanCode | (extended ? 0xE000u : 0u);
          var mapped = (Keys)MapVirtualKey(scan, MAPVK_VSC_TO_VK_EX);
          if (mapped != Keys.None) key = mapped;
        }
        // A scan code names the left or right Shift, Ctrl or Alt, a plain message the generic key: fold to the generic
        // one so a press pairs with its release and the games' Shift checks see it.
        key = (Keys)KeyTransitions.Generic((int)key);
        // The boss key is the key left of 1 on every layout (` on US and UK, ^ on German, ² on French), so it is matched
        // by position; a layout that gives VK_OEM_3 to another key (UK ' @, German Ö) does not get a second boss key.
        if (scanCode == BossScanCode && !extended) key = GameSession.BossKey;
        else if (key == GameSession.BossKey) key = Keys.None;
        // Both hook codes count (a message may be reported only as peeked); KeyTransitions acts on each press and
        // release once, and does not trust wasDown for a key whose release it saw (Microsoft Pinyin can set it on a
        // brand-new press).
        bool fresh = _keys.Fresh((int)key, up, wasDown, _clock.ElapsedMilliseconds);
        bool swallow = false;
        try { swallow = _handler(key, !up, !up && !fresh, fresh); }
        catch (Exception ex) { Rhino.RhinoApp.WriteLine($"Moyu keyboard: {ex.Message}"); }
        if (swallow && ime && fresh) CloseIme();
        if (swallow) return (IntPtr)1;
      }
      return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Forgets which keys are down, e.g. after the app lost focus and missed their release.</summary>
    public void Reset() => _keys.Reset();

    /// <summary>
    /// Drops any half-typed IME text and switches the focused window's IME to direct (English) input. Once the
    /// handler has stopped the game (Esc on the result screen) and Dispose has run, it only drops the text: nothing
    /// would switch the IME back on.
    /// </summary>
    void CloseIme()
    {
      var hwnd = GetFocus();
      if (hwnd == IntPtr.Zero) return;
      var himc = ImmGetContext(hwnd);
      if (himc == IntPtr.Zero) return;
      ImmNotifyIME(himc, NI_COMPOSITIONSTR, CPS_CANCEL, 0);
      if (_hook != IntPtr.Zero && ImmGetOpenStatus(himc))
      {
        ImmSetOpenStatus(himc, false);
        if (!_imeClosed.Contains(hwnd)) _imeClosed.Add(hwnd);
      }
      ImmReleaseContext(hwnd, himc);
    }

    /// <summary>Unhooks and gives back the input method state it switched off (unless the user switched it since).</summary>
    public void Dispose()
    {
      foreach (var hwnd in _imeClosed)
      {
        var himc = ImmGetContext(hwnd);
        if (himc == IntPtr.Zero) continue;
        if (!ImmGetOpenStatus(himc)) ImmSetOpenStatus(himc, true);
        ImmReleaseContext(hwnd, himc);
      }
      _imeClosed.Clear();
      if (_hook == IntPtr.Zero) return;
      if (!UnhookWindowsHookEx(_hook)) Rhino.RhinoApp.WriteLine($"Moyu keyboard: unhook failed (error {Marshal.GetLastWin32Error()})");
      _hook = IntPtr.Zero;
    }
  }
}
