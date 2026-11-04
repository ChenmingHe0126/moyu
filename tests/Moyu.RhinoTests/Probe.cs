using System;
using System.IO;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Crash triage: starts Grasshopper with the user's own library folders, optionally runs the Moyu
  /// test file, then forces a full garbage collection so that every pending finalizer runs.
  /// A broken third-party finalizer kills the process here the same way it kills Rhino.
  /// </summary>
  static class Probe
  {
    public static int Run(string gha, bool withMoyu, string path)
    {
      AppDomain.CurrentDomain.AssemblyLoad += (s, e) =>
      {
        var name = e.LoadedAssembly.GetName().Name;
        if (name.IndexOf("millipede", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("DeadFish", StringComparison.OrdinalIgnoreCase) >= 0)
          Console.WriteLine($"  load {name,-24} from {(string.IsNullOrEmpty(e.LoadedAssembly.Location) ? "(memory)" : e.LoadedAssembly.Location)}");
      };
      var gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper") as Grasshopper.Plugin.GH_RhinoScriptInterface;
      gh.RunHeadless();
      GH_Document.EnableSolutions = true;

      foreach (var a in AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name.IndexOf("millipede", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.GetName().Name.IndexOf("DeadFish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.GetName().Name.IndexOf("Moyu", StringComparison.OrdinalIgnoreCase) >= 0))
        Console.WriteLine($"  loaded {a.GetName().Name,-24} from {(string.IsNullOrEmpty(a.Location) ? "(memory)" : a.Location)}");

      if (withMoyu)
      {
        if (!Instances.ComponentServer.ObjectProxies.Any(p => p.Desc.Category == "Moyu")) LoadPlugin(gha);
        var io = new GH_DocumentIO();
        Console.WriteLine($"  open {path}: {io.Open(path)}");
        var doc = io.Document;
        doc.Enabled = true;
        foreach (var t in doc.Objects.OfType<GH_BooleanToggle>()) t.Value = true;
        doc.NewSolution(true);
        foreach (var s in doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == Solver))
          for (int k = 0; k < 20; k++) Tick(doc, s);
        Console.WriteLine("  Moyu test file solved: " + string.Join(", ", doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == Solver).Select(s => s.Message)));
      }

      Console.WriteLine("  forcing full GC + finalizers ...");
      Console.Out.Flush();
      for (int k = 0; k < 3; k++)
      {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
      }
      Console.WriteLine("  survived GC");
      return 0;
    }
  }
}
