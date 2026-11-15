using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Moyu.Platform.Mac
{
  /// <summary>
  /// The few AppKit calls Moyu needs on Rhino for Mac, made through the Objective-C runtime (libobjc) so the plug-in
  /// needs no Mac-only assembly and the same .gha builds on Windows. Rhino-free: Moyu.MacTests runs this code on a
  /// plain macOS machine. Only call it on macOS, on the main (UI) thread.
  /// </summary>
  public static class MacInterop
  {
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string LibSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary>NSEventType values the keyboard monitor handles.</summary>
    public const ulong KeyDown = 10, KeyUp = 11, FlagsChanged = 12;

    static readonly IntPtr RtldDefault = new IntPtr(-2);

    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong a, IntPtr b);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern ulong SendULong(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern ushort SendUShort(IntPtr receiver, IntPtr selector);
    // BOOL is a signed char on Intel and a bool on Apple silicon: one byte either way.
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern byte SendBool(IntPtr receiver, IntPtr selector);
    [DllImport(LibSystem)] static extern IntPtr dlsym(IntPtr handle, string symbol);

    /// <summary>The class object of an Objective-C class (zero when it does not exist).</summary>
    public static IntPtr Class(string name) => objc_getClass(name);

    /// <summary>A selector.</summary>
    public static IntPtr Selector(string name) => sel_registerName(name);

    /// <summary>[receiver selector] for an object result.</summary>
    public static IntPtr Call(IntPtr receiver, string selector) => Send(receiver, Selector(selector));

    /// <summary>[receiver selector:argument] for an object result.</summary>
    public static IntPtr Call(IntPtr receiver, string selector, IntPtr argument) => Send(receiver, Selector(selector), argument);

    public static IntPtr PushAutoreleasePool() => objc_autoreleasePoolPush();

    public static void PopAutoreleasePool(IntPtr pool) => objc_autoreleasePoolPop(pool);

    /// <summary>NSApp, created if this process has none yet (only the tests run without one).</summary>
    public static IntPtr SharedApplication => Call(Class("NSApplication"), "sharedApplication");

    /// <summary>Rhino is the active app (it has the keyboard): false once the user switched to another program.</summary>
    public static bool AppIsActive() => SendBool(SharedApplication, Selector("isActive")) != 0;

    /// <summary>The modifier keys held right now (NSEvent.modifierFlags), as NSEventModifierFlags bits.</summary>
    public static ulong CurrentModifierFlags() => SendULong(Class("NSEvent"), Selector("modifierFlags"));

    /// <summary>The first of the user's preferred languages in System Settings, such as "zh-Hans-CN"; null when none.</summary>
    public static string PreferredLanguage()
    {
      var languages = Call(Class("NSLocale"), "preferredLanguages");
      return languages == IntPtr.Zero ? null : ToManaged(Call(languages, "firstObject"));
    }

    /// <summary>A font family of that name (such as "PingFang SC") is installed on this Mac.</summary>
    public static bool FontFamilyExists(string family)
    {
      var ns = NSString(family);
      if (ns == IntPtr.Zero) return false;
      var members = Call(Call(Class("NSFontManager"), "sharedFontManager"), "availableMembersOfFontFamily:", ns);
      return members != IntPtr.Zero && SendULong(members, Selector("count")) > 0;
    }

    // ---------------------------------------------------------------- NSEvent

    public static ulong EventType(IntPtr nsEvent) => SendULong(nsEvent, Selector("type"));

    public static int KeyCode(IntPtr nsEvent) => SendUShort(nsEvent, Selector("keyCode"));

    public static ulong ModifierFlags(IntPtr nsEvent) => SendULong(nsEvent, Selector("modifierFlags"));

    /// <summary>Only for key-down and key-up events: AppKit raises an exception for any other type.</summary>
    public static bool IsARepeat(IntPtr nsEvent) => SendBool(nsEvent, Selector("isARepeat")) != 0;

    /// <summary>Only for key-down and key-up events: AppKit raises an exception for any other type.</summary>
    public static string CharactersIgnoringModifiers(IntPtr nsEvent) => ToManaged(Call(nsEvent, "charactersIgnoringModifiers"));

    // ---------------------------------------------------------------- strings

    /// <summary>An autoreleased NSString with the text.</summary>
    public static IntPtr NSString(string text)
    {
      if (text == null) return IntPtr.Zero;
      byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
      IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
      try
      {
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return Send(Class("NSString"), Selector("stringWithUTF8String:"), buffer);
      }
      finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>The text of an NSString; null for nil.</summary>
    public static string ToManaged(IntPtr nsString)
    {
      if (nsString == IntPtr.Zero) return null;
      IntPtr utf8 = Send(nsString, Selector("UTF8String"));
      if (utf8 == IntPtr.Zero) return null;
      int length = 0;
      while (Marshal.ReadByte(utf8, length) != 0) length++;
      var bytes = new byte[length];
      Marshal.Copy(utf8, bytes, 0, length);
      return Encoding.UTF8.GetString(bytes);
    }

    // ---------------------------------------------------------------- the local event monitor

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr MonitorBlock(IntPtr block, IntPtr nsEvent);

    // The block AppKit calls is a global block made once and never freed: copying a global block returns the block
    // itself, so nothing AppKit keeps can outlive it. Its function forwards to whichever monitor is installed.
    static readonly MonitorBlock BlockFunction = InvokeBlock;
    static IntPtr s_block;
    static Func<IntPtr, IntPtr> s_handler;

    static IntPtr InvokeBlock(IntPtr block, IntPtr nsEvent)
    {
      var handler = s_handler;
      if (handler == null || nsEvent == IntPtr.Zero) return nsEvent;
      // An exception must never travel back into AppKit: it would take Rhino down.
      try { return handler(nsEvent); }
      catch (Exception) { return nsEvent; }
    }

    static IntPtr GlobalBlock()
    {
      if (s_block != IntPtr.Zero) return s_block;
      IntPtr isa = dlsym(RtldDefault, "_NSConcreteGlobalBlock");
      if (isa == IntPtr.Zero) throw new InvalidOperationException("The Objective-C block runtime was not found.");
      // struct Block_descriptor { unsigned long reserved; unsigned long size; }
      IntPtr descriptor = Marshal.AllocHGlobal(16);
      Marshal.WriteInt64(descriptor, 0, 0);
      Marshal.WriteInt64(descriptor, 8, 32);
      // struct Block_literal { void *isa; int flags; int reserved; void *invoke; struct Block_descriptor *descriptor; }
      IntPtr block = Marshal.AllocHGlobal(32);
      Marshal.WriteIntPtr(block, 0, isa);
      Marshal.WriteInt32(block, 8, 1 << 28); // BLOCK_IS_GLOBAL
      Marshal.WriteInt32(block, 12, 0);
      Marshal.WriteIntPtr(block, 16, Marshal.GetFunctionPointerForDelegate(BlockFunction));
      Marshal.WriteIntPtr(block, 24, descriptor);
      s_block = block;
      return block;
    }

    /// <summary>
    /// Installs a local event monitor for key down, key up and flags changed: AppKit hands every such event of this
    /// app to <paramref name="handler"/> before any window, view or input method sees it. The handler returns the
    /// event to let it through or zero to swallow it. One monitor at a time; returns the monitor token.
    /// </summary>
    public static IntPtr AddKeyMonitor(Func<IntPtr, IntPtr> handler)
    {
      ulong mask = (1UL << (int)KeyDown) | (1UL << (int)KeyUp) | (1UL << (int)FlagsChanged);
      IntPtr block = GlobalBlock();
      s_handler = handler;
      IntPtr monitor = Send(Class("NSEvent"), Selector("addLocalMonitorForEventsMatchingMask:handler:"), mask, block);
      if (monitor == IntPtr.Zero)
      {
        s_handler = null;
        return IntPtr.Zero;
      }
      return Call(monitor, "retain");
    }

    /// <summary>Removes a monitor <see cref="AddKeyMonitor"/> returned; its handler is not called again.</summary>
    public static void RemoveKeyMonitor(IntPtr monitor, Func<IntPtr, IntPtr> handler)
    {
      if (monitor == IntPtr.Zero) return;
      if (s_handler == handler) s_handler = null;
      Call(Class("NSEvent"), "removeMonitor:", monitor);
      Call(monitor, "release");
    }
  }
}
