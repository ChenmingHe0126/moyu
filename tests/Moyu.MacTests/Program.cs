using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Moyu.Games.Common.Core;
using Moyu.Platform;
using Moyu.Platform.Mac;

namespace Moyu.MacTests
{
  /// <summary>
  /// Drives the code Moyu runs on Rhino for Mac (the AppKit key monitor, the active-app, language and modifier queries,
  /// the font families the games ask for) on a real macOS. Rhino itself is not needed: the monitor is AppKit's.
  /// </summary>
  static class Program
  {
    static int _failures;

    // Windows virtual keys the games use, and macOS key codes.
    const int A = 0x41, S = 0x53, Space = 0x20, Shift = 0x10, LeftWin = 0x5B;
    const int KA = 0x00, KS = 0x01, KSpace = 0x31, KGrave = 0x32, KShift = 0x38, KCommand = 0x37, KCapsLock = 0x39;

    sealed class Call
    {
      public int Key;
      public bool Down, Repeat, Act;
      public override string ToString() => $"key 0x{Key:X2} {(Down ? "down" : "up")}{(Repeat ? " repeat" : "")}{(Act ? " act" : "")}";
    }

    static int Main()
    {
      if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        Console.WriteLine("Moyu.MacTests runs on macOS only: skipped.");
        return 0;
      }
      Console.WriteLine($"macOS {Environment.OSVersion.Version}, {RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}");
      Check(OS.IsMac && !OS.IsWindows, "OS says Mac");
      // Rhino has AppKit loaded; this console process has to load it itself.
      Check(MacInterop.LoadAppKit(), "AppKit loads");

      IntPtr pool = MacInterop.PushAutoreleasePool();
      try
      {
        IntPtr app = MacInterop.SharedApplication;
        Check(app != IntPtr.Zero, "NSApplication exists");
        MacInterop.Call(app, "finishLaunching");
        Queries();
        Monitor();
        SecondMonitor();
      }
      catch (Exception ex)
      {
        _failures++;
        Console.WriteLine($"  [FAIL] {ex.GetType().Name}: {ex.Message}");
      }
      finally { MacInterop.PopAutoreleasePool(pool); }

      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    static void Queries()
    {
      Console.WriteLine("AppKit queries");
      bool active = MacInterop.AppIsActive();
      Console.WriteLine($"  NSApp isActive = {active}");
      ulong flags = MacInterop.CurrentModifierFlags();
      Console.WriteLine($"  NSEvent modifierFlags = 0x{flags:X}");
      Check((flags & MacKeys.ShortcutFlags) == 0 && !MacKeyMonitor.ShortcutModifierDown, "no shortcut modifier is held on the runner");
      string language = MacInterop.PreferredLanguage();
      Console.WriteLine($"  preferred language = {language ?? "(none)"}");
      Check(!string.IsNullOrEmpty(language), "the preferred language is read");
      Check(UiLanguages.FromCulture("zh-Hans-CN") == UiLanguage.Chinese && UiLanguages.FromCulture("en-US") == UiLanguage.English,
        "macOS language codes map to the games' languages");
      // The families GameConduit names on the Mac.
      foreach (var family in new[] { "Avenir Next", "Helvetica Neue", "PingFang SC" })
        Check(MacInterop.FontFamilyExists(family), $"font family {family} is installed");
      Check(!MacInterop.FontFamilyExists("No Such Family Moyu"), "a missing family is reported missing");
      Check(MacInterop.ToManaged(MacInterop.NSString("摸鱼 Moyu ✓")) == "摸鱼 Moyu ✓", "NSString round-trips UTF-8");
    }

    static void Monitor()
    {
      Console.WriteLine("Key monitor");
      var calls = new List<Call>();
      bool swallow = true, throwNext = false;
      var monitor = new MacKeyMonitor((vk, down, repeat, act) =>
      {
        if (throwNext)
        {
          throwNext = false;
          throw new InvalidOperationException("a handler that throws");
        }
        calls.Add(new Call { Key = vk, Down = down, Repeat = repeat, Act = act });
        return swallow;
      });
      Check(monitor.Installed, $"the monitor installs{(monitor.InstallError != null ? ": " + monitor.InstallError : "")}");
      if (!monitor.Installed) return;

      Call Only(string what)
      {
        bool one = calls.Count == 1;
        Check(one, $"{what}: one call (got {calls.Count}{(calls.Count > 0 ? ": " + string.Join(", ", calls) : "")})");
        var c = one ? calls[0] : null;
        calls.Clear();
        return c;
      }

      var ev = Synth.Key(KA, true, "a");
      Check(MacInterop.EventType(ev) == MacInterop.KeyDown && MacInterop.KeyCode(ev) == KA, "a synthetic key-down is a key-down");
      Synth.Send(ev);
      var c1 = Only("A pressed");
      Check(c1 != null && c1.Key == A && c1.Down && !c1.Repeat && c1.Act, "A pressed: key A, down, acted on");

      Synth.Send(Synth.Key(KA, true, "a", repeat: true));
      var c2 = Only("A auto-repeat");
      Check(c2 != null && c2.Key == A && c2.Down && c2.Repeat && !c2.Act, "auto-repeat is reported, not acted on");

      Synth.Send(Synth.Key(KA, false, "a"));
      var c3 = Only("A released");
      Check(c3 != null && c3.Key == A && !c3.Down && c3.Act, "A released: up, acted on");

      swallow = false;
      Synth.Send(Synth.Key(KSpace, true, " "));
      var c4 = Only("Space let through");
      Check(c4 != null && c4.Key == Space && c4.Down, "Space reported when the handler lets it through");
      Synth.Send(Synth.Key(KSpace, false, " "));
      calls.Clear();
      swallow = true;

      Synth.Send(Synth.Key(KGrave, true, "`"));
      var c5 = Only("boss key");
      Check(c5 != null && c5.Key == MacKeys.BossKey, "the key left of 1 is the boss key");
      Synth.Send(Synth.Key(KGrave, false, "`"));
      calls.Clear();

      Synth.Send(Synth.Modifier(KShift, MacKeys.ShiftFlag | 0x2));
      var c6 = Only("Shift down (flags changed)");
      Check(c6 != null && c6.Key == Shift && c6.Down && c6.Act, "Shift going down is a press");
      Synth.Send(Synth.Modifier(KShift, 0));
      var c7 = Only("Shift up (flags changed)");
      Check(c7 != null && c7.Key == Shift && !c7.Down && c7.Act, "Shift coming up is a release");

      Synth.Send(Synth.Modifier(KCapsLock, 1UL << 16));
      Check(calls.Count == 0, "Caps Lock is left alone");
      calls.Clear();

      Synth.Send(Synth.Modifier(KCommand, MacKeys.CommandFlag | 0x8));
      var c8 = Only("Command down");
      Check(c8 != null && c8.Key == LeftWin && c8.Down, "Command is reported as the Windows key, which the games pass on");
      Synth.Send(Synth.Key(KS, true, "s", flags: MacKeys.CommandFlag | 0x8));
      var c9 = Only("⌘S");
      Check(c9 != null && c9.Key == S && c9.Down, "⌘S reaches the handler as S (GameSession passes it on to Rhino)");
      Synth.Send(Synth.Key(KS, false, "s", flags: MacKeys.CommandFlag | 0x8));
      Synth.Send(Synth.Modifier(KCommand, 0));
      calls.Clear();

      throwNext = true;
      Synth.Send(Synth.Key(KA, true, "a"));
      Check(calls.Count == 0, "a handler that throws does not take the app down");
      Synth.Send(Synth.Key(KA, false, "a"));
      Check(calls.Count == 1 && !calls[0].Down, "the monitor keeps working after a throw");
      calls.Clear();

      monitor.Reset();
      monitor.Dispose();
      Synth.Send(Synth.Key(KA, true, "a"));
      Synth.Send(Synth.Key(KA, false, "a"));
      Check(calls.Count == 0, "after Dispose no key reaches the handler");
      monitor.Dispose();
      Check(true, "a second Dispose is harmless");
    }

    /// <summary>Stopping one game and starting the next installs a new monitor.</summary>
    static void SecondMonitor()
    {
      Console.WriteLine("Second session");
      int count = 0;
      using (var monitor = new MacKeyMonitor((vk, down, repeat, act) => { count++; return true; }))
      {
        Check(monitor.Installed, "a second monitor installs");
        Synth.Send(Synth.Key(KA, true, "a"));
        Synth.Send(Synth.Key(KA, false, "a"));
        Check(count == 2, $"it receives the press and the release (got {count})");
      }
      Synth.Send(Synth.Key(KA, true, "a"));
      Check(count == 2, "and nothing after it is disposed");
    }

    static void Check(bool ok, string what)
    {
      if (!ok) _failures++;
      Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
    }
  }
}
