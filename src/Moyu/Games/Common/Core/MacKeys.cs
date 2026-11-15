using System.Collections.Generic;

namespace Moyu.Games.Common.Core
{
  /// <summary>
  /// Turns a macOS key event (its hardware key code, its characters, its modifier flags) into the Windows virtual-key
  /// code the games already speak (the values of WinForms' <c>Keys</c>), so the Mac keyboard monitor feeds the same
  /// game code as the Windows hook. Rhino-free, so the rules are tested in Moyu.Core.Tests on any machine.
  /// </summary>
  public static class MacKeys
  {
    /// <summary>VK_OEM_3, which the games use as the boss key (<c>Keys.Oemtilde</c>).</summary>
    public const int BossKey = 0xC0;

    /// <summary>
    /// What a key with no Windows name is reported as (VK_OEM_CLEAR). The games ignore it, but in play they still take
    /// it, so it does not end up on Rhino's command line.
    /// </summary>
    public const int Other = 0xFE;

    /// <summary>The device-independent modifier bits of an NSEvent (NSEventModifierFlagShift and so on).</summary>
    public const ulong ShiftFlag = 1UL << 17, ControlFlag = 1UL << 18, OptionFlag = 1UL << 19, CommandFlag = 1UL << 20;

    /// <summary>Control, Option or Command: a key pressed with one of these is a shortcut, which belongs to Rhino.</summary>
    public const ulong ShortcutFlags = ControlFlag | OptionFlag | CommandFlag;

    // The device-dependent bits (IOLLEvent.h) tell the left key from the right one in a flags-changed event.
    const ulong LeftControl = 0x1, LeftShift = 0x2, RightShift = 0x4, LeftCommand = 0x8, RightCommand = 0x10,
      LeftOption = 0x20, RightOption = 0x40, RightControl = 0x2000;
    const ulong DeviceBits = LeftControl | LeftShift | RightShift | LeftCommand | RightCommand | LeftOption | RightOption | RightControl;

    // kVK_* codes from Carbon's Events.h: they name a key by its place on the keyboard, not by what it types.
    static readonly Dictionary<int, int> Table = new Dictionary<int, int>
    {
      // Letters, by their place on a US keyboard (used when the layout does not type a Latin letter there).
      [0x00] = 'A', [0x0B] = 'B', [0x08] = 'C', [0x02] = 'D', [0x0E] = 'E', [0x03] = 'F', [0x05] = 'G', [0x04] = 'H',
      [0x22] = 'I', [0x26] = 'J', [0x28] = 'K', [0x25] = 'L', [0x2E] = 'M', [0x2D] = 'N', [0x1F] = 'O', [0x23] = 'P',
      [0x0C] = 'Q', [0x0F] = 'R', [0x01] = 'S', [0x11] = 'T', [0x20] = 'U', [0x09] = 'V', [0x0D] = 'W', [0x07] = 'X',
      [0x10] = 'Y', [0x06] = 'Z',
      // The digit row, by place, as Windows names it on every layout.
      [0x1D] = '0', [0x12] = '1', [0x13] = '2', [0x14] = '3', [0x15] = '4', [0x17] = '5', [0x16] = '6', [0x1A] = '7',
      [0x1C] = '8', [0x19] = '9',
      // Punctuation (VK_OEM_*).
      [0x18] = 0xBB, [0x1B] = 0xBD, [0x21] = 0xDB, [0x1E] = 0xDD, [0x2A] = 0xDC, [0x29] = 0xBA, [0x27] = 0xDE,
      [0x2B] = 0xBC, [0x2F] = 0xBE, [0x2C] = 0xBF,
      // The key left of 1: ` on an ANSI Mac keyboard (kVK_ANSI_Grave), § on an ISO one (kVK_ISO_Section). Both are the
      // boss key; on ISO keyboards macOS reports the key next to left Shift as Grave, which the games do not use.
      [0x32] = BossKey, [0x0A] = BossKey,
      // Editing and navigation.
      [0x24] = 0x0D, [0x4C] = 0x0D, [0x30] = 0x09, [0x31] = 0x20, [0x33] = 0x08, [0x35] = 0x1B, [0x75] = 0x2E,
      [0x72] = 0x2D, [0x73] = 0x24, [0x77] = 0x23, [0x74] = 0x21, [0x79] = 0x22,
      [0x7B] = 0x25, [0x7E] = 0x26, [0x7C] = 0x27, [0x7D] = 0x28,
      // Modifiers, folded to the generic Windows keys as the hook folds left and right.
      [0x38] = 0x10, [0x3C] = 0x10, [0x3B] = 0x11, [0x3E] = 0x11, [0x3A] = 0x12, [0x3D] = 0x12,
      [0x37] = 0x5B, [0x36] = 0x5C,
      // Function keys.
      [0x7A] = 0x70, [0x78] = 0x71, [0x63] = 0x72, [0x76] = 0x73, [0x60] = 0x74, [0x61] = 0x75, [0x62] = 0x76,
      [0x64] = 0x77, [0x65] = 0x78, [0x6D] = 0x79, [0x67] = 0x7A, [0x6F] = 0x7B,
      // Keypad.
      [0x52] = 0x60, [0x53] = 0x61, [0x54] = 0x62, [0x55] = 0x63, [0x56] = 0x64, [0x57] = 0x65, [0x58] = 0x66,
      [0x59] = 0x67, [0x5B] = 0x68, [0x5C] = 0x69, [0x43] = 0x6A, [0x45] = 0x6B, [0x4E] = 0x6D, [0x41] = 0x6E,
      [0x4B] = 0x6F,
    };

    /// <summary>Caps Lock and fn: keys whose flags-changed events say nothing a game wants.</summary>
    static bool Ignored(int keyCode) => keyCode == 0x39 || keyCode == 0x3F;

    /// <summary>
    /// The Windows virtual key for a Mac key event, or 0 when the event is to be left alone (Caps Lock, fn).
    /// <paramref name="characters"/> is the event's charactersIgnoringModifiers (null for a flags-changed event): when
    /// the layout types a Latin letter, that letter wins, as Windows names letter keys after the layout (AZERTY's A is
    /// where QWERTY has Q). Everything else, the boss key first, goes by the key's place.
    /// </summary>
    public static int ToVirtualKey(int keyCode, string characters)
    {
      if (Ignored(keyCode)) return 0;
      if (!Table.TryGetValue(keyCode, out int vk)) return Other;
      if (vk == BossKey || vk < 'A' || vk > 'Z') return vk;
      if (characters != null && characters.Length == 1)
      {
        char c = char.ToUpperInvariant(characters[0]);
        if (c >= 'A' && c <= 'Z') return c;
      }
      return vk;
    }

    /// <summary>A modifier key (Shift, Control, Option, Command on either side).</summary>
    public static bool IsModifier(int keyCode) => DeviceBit(keyCode) != 0;

    /// <summary>
    /// For a flags-changed event of <paramref name="keyCode"/>: true when that key went down, false when it came up,
    /// null when the key is not a modifier the games track. Real keyboards set the left/right bits; an event that
    /// carries only the generic bit (a synthetic one) is read from that.
    /// </summary>
    public static bool? ModifierDown(int keyCode, ulong flags)
    {
      ulong bit = DeviceBit(keyCode);
      if (bit == 0) return null;
      if ((flags & DeviceBits) != 0) return (flags & bit) != 0;
      return (flags & GenericBit(keyCode)) != 0;
    }

    static ulong DeviceBit(int keyCode)
    {
      switch (keyCode)
      {
        case 0x38: return LeftShift;
        case 0x3C: return RightShift;
        case 0x3B: return LeftControl;
        case 0x3E: return RightControl;
        case 0x3A: return LeftOption;
        case 0x3D: return RightOption;
        case 0x37: return LeftCommand;
        case 0x36: return RightCommand;
        default: return 0;
      }
    }

    static ulong GenericBit(int keyCode)
    {
      switch (keyCode)
      {
        case 0x38: case 0x3C: return ShiftFlag;
        case 0x3B: case 0x3E: return ControlFlag;
        case 0x3A: case 0x3D: return OptionFlag;
        case 0x37: case 0x36: return CommandFlag;
        default: return 0;
      }
    }
  }
}
