using System;
using Moyu.Games.Common.Core;

namespace Moyu.Core.Tests
{
  /// <summary>How the Mac keyboard monitor names keys: macOS key codes in, the Windows virtual keys the games use out.</summary>
  static class TestsMacKeys
  {
    // Windows virtual keys (WinForms Keys values).
    const int A = 0x41, C = 0x43, D = 0x44, E = 0x45, P = 0x50, Q = 0x51, S = 0x53, V = 0x56, W = 0x57, X = 0x58, Z = 0x5A,
      Space = 0x20, Enter = 0x0D, Escape = 0x1B, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
      Shift = 0x10, Control = 0x11, Alt = 0x12, LeftWin = 0x5B, RightWin = 0x5C, One = 0x31;

    // macOS key codes (kVK_*).
    const int KA = 0x00, KS = 0x01, KD = 0x02, KZ = 0x06, KX = 0x07, KC = 0x08, KV = 0x09, KQ = 0x0C, KW = 0x0D, KE = 0x0E,
      KP = 0x23, K1 = 0x12, KReturn = 0x24, KKeypadEnter = 0x4C, KSpace = 0x31, KEscape = 0x35, KGrave = 0x32,
      KIsoSection = 0x0A, KLeft = 0x7B, KRight = 0x7C, KDown = 0x7D, KUp = 0x7E, KHome = 0x73, KShift = 0x38,
      KRightShift = 0x3C, KControl = 0x3B, KOption = 0x3A, KCommand = 0x37, KRightCommand = 0x36, KCapsLock = 0x39,
      KFn = 0x3F, KJisYen = 0x5D;

    public static void Run(Action<bool, string> check)
    {
      Console.WriteLine("Mac keys");

      // Every key a game listens to, on a US layout.
      check(MacKeys.ToVirtualKey(KW, "w") == W && MacKeys.ToVirtualKey(KA, "a") == A && MacKeys.ToVirtualKey(KS, "s") == S &&
        MacKeys.ToVirtualKey(KD, "d") == D, "W A S D");
      check(MacKeys.ToVirtualKey(KQ, "q") == Q && MacKeys.ToVirtualKey(KE, "e") == E && MacKeys.ToVirtualKey(KZ, "z") == Z &&
        MacKeys.ToVirtualKey(KX, "x") == X && MacKeys.ToVirtualKey(KC, "c") == C && MacKeys.ToVirtualKey(KV, "v") == V &&
        MacKeys.ToVirtualKey(KP, "p") == P, "Q E Z X C V P");
      check(MacKeys.ToVirtualKey(KLeft, "") == Left && MacKeys.ToVirtualKey(KRight, "") == Right &&
        MacKeys.ToVirtualKey(KUp, "") == Up && MacKeys.ToVirtualKey(KDown, "") == Down,
        "the arrows, whose characters are private-use codes");
      check(MacKeys.ToVirtualKey(KSpace, " ") == Space && MacKeys.ToVirtualKey(KEscape, "\u001b") == Escape &&
        MacKeys.ToVirtualKey(KHome, "") == Home, "Space, Esc, Home");
      check(MacKeys.ToVirtualKey(KReturn, "\r") == Enter && MacKeys.ToVirtualKey(KKeypadEnter, "\u0003") == Enter,
        "Return and the keypad's Enter are both Enter");
      check(MacKeys.ToVirtualKey(KW, "W") == W, "Shift+W (upper case) is still W");

      // Letters follow the layout, as Windows names them; non-letters go by place.
      check(MacKeys.ToVirtualKey(KQ, "a") == A && MacKeys.ToVirtualKey(KA, "q") == Q, "AZERTY: the key typing A is A");
      check(MacKeys.ToVirtualKey(KW, "ц") == W, "a Cyrillic layout falls back to the key's place");
      check(MacKeys.ToVirtualKey(KW, null) == W && MacKeys.ToVirtualKey(KW, "") == W, "no characters: by place");
      check(MacKeys.ToVirtualKey(K1, "&") == One, "the digit row by place (AZERTY types & on 1)");

      // The boss key is the key left of 1 on ANSI and ISO keyboards, whatever it types.
      check(MacKeys.ToVirtualKey(KGrave, "`") == MacKeys.BossKey && MacKeys.ToVirtualKey(KIsoSection, "§") == MacKeys.BossKey,
        "` (ANSI) and § (ISO) are the boss key");
      check(MacKeys.ToVirtualKey(KGrave, "a") == MacKeys.BossKey, "the boss key stays the boss key on any layout");
      check(MacKeys.BossKey == 0xC0, "the boss key is VK_OEM_3, Keys.Oemtilde");

      // Modifiers fold to the generic Windows keys; Command is the Windows key, so the games pass it to Rhino.
      check(MacKeys.ToVirtualKey(KShift, null) == Shift && MacKeys.ToVirtualKey(KRightShift, null) == Shift, "both Shifts are Shift");
      check(MacKeys.ToVirtualKey(KControl, null) == Control && MacKeys.ToVirtualKey(KOption, null) == Alt, "Control and Option");
      check(MacKeys.ToVirtualKey(KCommand, null) == LeftWin && MacKeys.ToVirtualKey(KRightCommand, null) == RightWin,
        "Command is the Windows key");
      check(MacKeys.ToVirtualKey(KCapsLock, null) == 0 && MacKeys.ToVirtualKey(KFn, null) == 0, "Caps Lock and fn are left alone");
      check(MacKeys.ToVirtualKey(KJisYen, "¥") == MacKeys.Other, "a key Windows has no name for is still taken in play");

      // Flags-changed events: which modifier went down or up.
      const ulong LeftShiftBit = 0x2, RightShiftBit = 0x4, LeftCommandBit = 0x8;
      check(MacKeys.ModifierDown(KShift, MacKeys.ShiftFlag | LeftShiftBit) == true, "left Shift down");
      check(MacKeys.ModifierDown(KShift, 0) == false, "left Shift up");
      check(MacKeys.ModifierDown(KShift, MacKeys.ShiftFlag | RightShiftBit) == false,
        "left Shift up while right Shift is still held");
      check(MacKeys.ModifierDown(KRightShift, MacKeys.ShiftFlag | RightShiftBit) == true, "right Shift down");
      check(MacKeys.ModifierDown(KShift, MacKeys.ShiftFlag) == true && MacKeys.ModifierDown(KShift, 0x100) == false,
        "an event with only the generic bit (a synthetic one) is read from that");
      check(MacKeys.ModifierDown(KCommand, MacKeys.CommandFlag | LeftCommandBit) == true, "Command down");
      check(MacKeys.ModifierDown(KCapsLock, 1UL << 16) == null && MacKeys.ModifierDown(KA, MacKeys.ShiftFlag) == null,
        "Caps Lock and plain keys are not tracked modifiers");
      check((MacKeys.ShortcutFlags & MacKeys.ShiftFlag) == 0 && (MacKeys.ShortcutFlags & MacKeys.CommandFlag) != 0,
        "Command, Control and Option make a shortcut; Shift does not");
    }
  }
}
