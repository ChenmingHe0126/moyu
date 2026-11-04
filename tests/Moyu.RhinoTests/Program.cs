using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Moyu.RhinoTests
{
  static class Program
  {
    static Program()
    {
      // Must run before any RhinoCommon/Grasshopper type is touched.
      RhinoInside.Resolver.Initialize();
    }

    [STAThread]
    static int Main(string[] args)
    {
      try { return Run(args); }
      catch (Exception ex) { Console.WriteLine("FATAL " + ex); return 2; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
      string root = FindRepoRoot();
      string gha = args.FirstOrDefault(a => a.EndsWith(".gha", StringComparison.OrdinalIgnoreCase))
        ?? Path.Combine(root, "src", "Moyu", "bin", "Release", "net7.0-windows", "Moyu.gha");
      int img = Array.IndexOf(args, "--images");
      string images = img >= 0 && img + 1 < args.Length ? Path.GetFullPath(args[img + 1]) : Path.Combine(root, "tests", "Moyu.RhinoTests", "out");
      if (!File.Exists(gha)) { Console.WriteLine($"Plugin not found: {gha} (build src/Moyu in Release first)"); return 2; }

      // Workaround for broken third-party plugins in the user's Grasshopper library folders: native DLLs listed
      // here (semicolon-separated full paths) are loaded first, so a plugin that cannot find its own native
      // library does not kill the test process from a finalizer.
      foreach (var lib in (Environment.GetEnvironmentVariable("MOYU_TEST_PRELOAD") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        Console.WriteLine($"preloaded {lib}: {System.Runtime.InteropServices.NativeLibrary.TryLoad(lib, out _)}");

      string ghFolder = Path.Combine(root, "tests", "gh");
      // --examples <folder>: write the six files for the release package there (example names), never into tests/gh.
      int ex = Array.IndexOf(args, "--examples");
      string examples = ex >= 0 && ex + 1 < args.Length ? Path.GetFullPath(args[ex + 1]) : null;

      using (new Rhino.Runtime.InProcess.RhinoCore(new[] { "/netcore" }, Rhino.Runtime.InProcess.WindowStyle.NoWindow))
      {
        Console.WriteLine($"Rhino {Rhino.RhinoApp.Version} (headless, .NET {Environment.Version})");
        if (args.Contains("--icons")) return Scenarios.Icons(gha);
        if (examples != null)
        {
          Directory.CreateDirectory(examples);
          return GhFile.Run(gha, Path.Combine(examples, "Topo2D.gh")) | GhFile3D.Run(gha, Path.Combine(examples, "Topo3D.gh"))
            | GhFileStack.Run(gha, Path.Combine(examples, "VoxStack.gh")) | GhFileSnake.Run(gha, Path.Combine(examples, "Serpentine.gh"))
            | GhFileBreakout.Run(gha, Path.Combine(examples, "Rebound.gh")) | GhFileSwift.Run(gha, Path.Combine(examples, "Swift.gh"));
        }
        if (args.Contains("--probe-gc")) return Probe.Run(gha, args.Contains("--with-moyu"), Path.Combine(ghFolder, "Moyu2D-Test.gh"));
        if (args.Contains("--write-gh-stack")) return GhFileStack.Run(gha, Path.Combine(ghFolder, "MoyuStack-Test.gh"));
        if (args.Contains("--write-gh-snake")) return GhFileSnake.Run(gha, Path.Combine(ghFolder, "MoyuSnake-Test.gh"));
        if (args.Contains("--write-gh-breakout")) return GhFileBreakout.Run(gha, Path.Combine(ghFolder, "MoyuBreakout-Test.gh"));
        if (args.Contains("--write-gh-swift")) return GhFileSwift.Run(gha, Path.Combine(ghFolder, "MoyuSwift-Test.gh"));
        if (args.Contains("--write-gh"))
          return GhFile.Run(gha, Path.Combine(ghFolder, "Moyu2D-Test.gh")) | GhFile3D.Run(gha, Path.Combine(ghFolder, "Moyu3D-Test.gh"))
            | GhFileStack.Run(gha, Path.Combine(ghFolder, "MoyuStack-Test.gh")) | GhFileSnake.Run(gha, Path.Combine(ghFolder, "MoyuSnake-Test.gh"))
            | GhFileBreakout.Run(gha, Path.Combine(ghFolder, "MoyuBreakout-Test.gh")) | GhFileSwift.Run(gha, Path.Combine(ghFolder, "MoyuSwift-Test.gh"));
        return Scenarios.Run(gha, images);
      }
    }

    static string FindRepoRoot()
    {
      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Moyu"))) dir = dir.Parent;
      return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
  }
}
