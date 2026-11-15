using System;
using System.Runtime.InteropServices;
using Moyu.Platform.Mac;

namespace Moyu.MacTests
{
  /// <summary>Makes real NSEvents through Quartz (CGEvent), the way a keyboard's events reach an app, and dispatches them.</summary>
  static class Synth
  {
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const int AutorepeatField = 8; // kCGKeyboardEventAutorepeat
    const uint FlagsChangedType = 12; // kCGEventFlagsChanged

    [DllImport(CoreGraphics)] static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, [MarshalAs(UnmanagedType.I1)] bool keyDown);
    [DllImport(CoreGraphics)] static extern void CGEventKeyboardSetUnicodeString(IntPtr ev, UIntPtr length, [In] ushort[] text);
    [DllImport(CoreGraphics)] static extern void CGEventSetIntegerValueField(IntPtr ev, int field, long value);
    [DllImport(CoreGraphics)] static extern void CGEventSetFlags(IntPtr ev, ulong flags);
    [DllImport(CoreGraphics)] static extern void CGEventSetType(IntPtr ev, uint type);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr cf);

    /// <summary>An autoreleased NSEvent for a key going down or up.</summary>
    public static IntPtr Key(int keyCode, bool down, string text = null, bool repeat = false, ulong flags = 0)
    {
      IntPtr cg = CGEventCreateKeyboardEvent(IntPtr.Zero, (ushort)keyCode, down);
      if (cg == IntPtr.Zero) throw new InvalidOperationException("CGEventCreateKeyboardEvent returned nothing");
      try
      {
        if (text != null)
        {
          var units = new ushort[text.Length];
          for (int i = 0; i < text.Length; i++) units[i] = text[i];
          CGEventKeyboardSetUnicodeString(cg, (UIntPtr)units.Length, units);
        }
        if (repeat) CGEventSetIntegerValueField(cg, AutorepeatField, 1);
        CGEventSetFlags(cg, flags);
        return ToNSEvent(cg);
      }
      finally { CFRelease(cg); }
    }

    /// <summary>An autoreleased flags-changed NSEvent: modifier key <paramref name="keyCode"/>, with the flags after it.</summary>
    public static IntPtr Modifier(int keyCode, ulong flagsAfter)
    {
      IntPtr cg = CGEventCreateKeyboardEvent(IntPtr.Zero, (ushort)keyCode, true);
      if (cg == IntPtr.Zero) throw new InvalidOperationException("CGEventCreateKeyboardEvent returned nothing");
      try
      {
        CGEventSetType(cg, FlagsChangedType);
        CGEventSetFlags(cg, flagsAfter);
        return ToNSEvent(cg);
      }
      finally { CFRelease(cg); }
    }

    static IntPtr ToNSEvent(IntPtr cg)
    {
      IntPtr ns = MacInterop.Call(MacInterop.Class("NSEvent"), "eventWithCGEvent:", cg);
      if (ns == IntPtr.Zero) throw new InvalidOperationException("NSEvent eventWithCGEvent: returned nil");
      return ns;
    }

    /// <summary>Hands the event to NSApplication as its run loop would ([NSApp sendEvent:]).</summary>
    public static void Send(IntPtr nsEvent) => MacInterop.Call(MacInterop.SharedApplication, "sendEvent:", nsEvent);
  }
}
