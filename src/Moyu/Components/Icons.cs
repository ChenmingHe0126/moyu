using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace Moyu.Components
{
  /// <summary>
  /// 24×24 PNG icons embedded from src/Moyu/Icons/&lt;Name&gt;.png. A missing file simply yields no icon,
  /// so components keep Grasshopper's default glyph until the artwork exists.
  /// </summary>
  public static class Icons
  {
    static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

    public static Bitmap Get(string name)
    {
      lock (Cache)
      {
        if (Cache.TryGetValue(name, out var bmp)) return bmp;
        using (var stream = typeof(Icons).Assembly.GetManifestResourceStream($"Moyu.Icons.{name}.png"))
        {
          // GDI+ reads a stream-backed Bitmap lazily, so copy it before the stream goes away.
          if (stream != null)
            using (var decoded = new Bitmap(stream))
              bmp = new Bitmap(decoded);
        }
        Cache[name] = bmp;
        return bmp;
      }
    }
  }

  /// <summary>Gives the Moyu ribbon tab its icon and a one-letter short name.</summary>
  public class MoyuPriority : GH_AssemblyPriority
  {
    public override GH_LoadingInstruction PriorityLoad()
    {
      var icon = Icons.Get("Moyu");
      if (icon != null) Instances.ComponentServer.AddCategoryIcon(MoyuComponent.Tab, icon);
      Instances.ComponentServer.AddCategorySymbolName(MoyuComponent.Tab, 'M');
      return GH_LoadingInstruction.Proceed;
    }
  }
}
