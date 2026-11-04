using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Moyu.RhinoTests
{
  /// <summary>Builds and drives Grasshopper documents in code, the way a user would on the canvas.</summary>
  static class Canvas
  {
    public static readonly Guid Boundary = new Guid("bea6c6f3-581f-47c7-859b-ffadd46d11b1");
    public static readonly Guid Load = new Guid("715dd06c-3618-405d-9f17-95d32d9f1652");
    public static readonly Guid Support = new Guid("179c263a-c613-4447-84b7-2cd55a730e17");
    public static readonly Guid Area = new Guid("efb05a6a-ac41-409d-8d6a-2a5cf3ae194c");
    public static readonly Guid Material = new Guid("697b1cf8-5420-47b6-a99e-920f75dd6e15");
    public static readonly Guid Solver = new Guid("0ccc0783-dd2c-45aa-ade8-96778cbd4815");
    public static readonly Guid Visualize = new Guid("2a76e4f3-1e42-46e8-94e3-d00c400409f7");

    public static readonly Guid Boundary3D = new Guid("c53783f5-76d5-4b4d-a59b-663ac770091e");
    public static readonly Guid Load3D = new Guid("c14e2661-33d8-4273-bf95-c0159fc6118e");
    public static readonly Guid Support3D = new Guid("f28a4501-8822-452d-a95c-f8514dad6ece");
    public static readonly Guid Area3D = new Guid("5b06ab3e-bc65-422f-be54-23933b2a5fa8");
    public static readonly Guid Material3D = new Guid("41f6f74f-2c53-4bc3-8dc4-2f1d6072ce91");
    public static readonly Guid Solver3D = new Guid("ebbb8167-2279-4eaf-a62d-75e2226fb21b");
    public static readonly Guid Visualize3D = new Guid("b3991b95-9c98-460d-8cbc-456b09dd6122");

    /// <summary>Starts Grasshopper headless and registers the plugin assembly.</summary>
    public static void LoadPlugin(string gha)
    {
      var gh = Rhino.RhinoApp.GetPlugInObject("Grasshopper") as Grasshopper.Plugin.GH_RhinoScriptInterface;
      gh.RunHeadless();
      GH_Document.EnableSolutions = true;

      // Grasshopper may already have loaded Moyu from one of the user's library folders.
      var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Moyu");
      if (loaded != null && Instances.ComponentServer.ObjectProxies.Any(p => p.Desc.Category == "Moyu"))
      {
        if (!string.Equals(System.IO.Path.GetFullPath(loaded.Location), System.IO.Path.GetFullPath(gha), StringComparison.OrdinalIgnoreCase))
          throw new InvalidOperationException($"Grasshopper already loaded Moyu from {loaded.Location}, not the build under test ({gha}).");
        Console.WriteLine($"Moyu already loaded by Grasshopper from {loaded.Location}");
        return;
      }
      // GH_ComponentServer.ParseGHA is what the loader calls for every library; it is not public.
      var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
        null, new[] { typeof(Assembly), typeof(string) }, null);
      if (!(bool)parse.Invoke(Instances.ComponentServer, new object[] { Assembly.LoadFrom(gha), gha }))
        throw new InvalidOperationException("Grasshopper refused the plugin assembly.");
    }

    public static GH_Document NewDocument() => new GH_Document { Enabled = true };

    public static GH_Component Add(GH_Document doc, Guid id)
    {
      var c = (GH_Component)Instances.ComponentServer.EmitObject(id);
      c.CreateAttributes();
      doc.AddObject(c, false);
      return c;
    }

    /// <summary>Replaces the persistent (right-click "Set …") data of an input, like editing it on the canvas.</summary>
    public static void Set(GH_Component c, int input, params IGH_Goo[] values)
    {
      dynamic p = c.Params.Input[input];
      p.PersistentData.Clear();
      foreach (dynamic v in values) p.PersistentData.Append(v);
      c.Params.Input[input].ExpireSolution(false);
    }

    public static void Wire(GH_Component src, int output, GH_Component dst, int input, bool append = false)
    {
      if (!append) dst.Params.Input[input].RemoveAllSources();
      dst.Params.Input[input].AddSource(src.Params.Output[output]);
      dst.Params.Input[input].ExpireSolution(false);
    }

    /// <summary>What a Grasshopper Timer does on every tick: expire the target and recompute.</summary>
    public static void Tick(GH_Document doc, GH_Component target)
    {
      target.ExpireSolution(false);
      doc.NewSolution(false);
    }

    public static IEnumerable<IGH_Goo> Data(GH_Component c, int output) => c.Params.Output[output].VolatileData.AllData(true);
    public static int Int(GH_Component c, int o) => ((GH_Integer)Data(c, o).First()).Value;
    public static double Num(GH_Component c, int o) => ((GH_Number)Data(c, o).First()).Value;
    public static bool Bool(GH_Component c, int o) => ((GH_Boolean)Data(c, o).First()).Value;
    public static List<double> Nums(GH_Component c, int o) => Data(c, o).Cast<GH_Number>().Select(n => n.Value).ToList();
    public static Mesh MeshOut(GH_Component c) => ((GH_Mesh)Data(c, 0).First()).Value;
    public static List<Curve> CurvesOut(GH_Component c) => Data(c, 1).Cast<GH_Curve>().Select(g => g.Value).ToList();
    /// <summary>The solver's Result2D; read through <c>dynamic</c> since the plugin is loaded at run time.</summary>
    public static dynamic Result(GH_Component solver) => Data(solver, 0).First().ScriptVariable();

    public static bool HasMessage(GH_Component c, GH_RuntimeMessageLevel level, string fragment = "") =>
      c.RuntimeMessages(level).Any(m => m.Contains(fragment));

    public static void PrintMessages(GH_Component c)
    {
      foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
        foreach (var m in c.RuntimeMessages(level))
          Console.WriteLine($"    {c.NickName} {level}: {m}");
    }
  }
}
