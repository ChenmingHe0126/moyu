using System;
using System.Runtime.InteropServices;

namespace Moyu.Platform
{
  /// <summary>Which operating system Rhino runs on, for the few places that call into it directly.</summary>
  public static class OS
  {
    public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static readonly bool IsMac = !IsWindows && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
  }
}
