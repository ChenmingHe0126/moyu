using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  static class Scenarios
  {
    static int _failures;

    static void Check(bool ok, string what)
    {
      Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
      if (!ok) _failures++;
    }

    /// <summary>Quick check that every component and the ribbon tab picked up their embedded icons.</summary>
    public static int Icons(string gha)
    {
      LoadPlugin(gha);
      var proxies = Instances.ComponentServer.ObjectProxies.Where(p => p.Desc.Category == "Moyu").OrderBy(p => p.Desc.SubCategory).ThenBy(p => p.Exposure).ToList();
      var get = System.Reflection.Assembly.LoadFrom(gha).GetType("Moyu.Components.Icons").GetMethod("Get");
      foreach (var p in proxies)
      {
        var bmp = get.Invoke(null, new object[] { p.Type.Name }) as System.Drawing.Bitmap;
        Check(bmp != null && bmp.Width == 24 && bmp.Height == 24, $"{p.Desc.SubCategory} {p.Desc.Name}: {p.Type.Name}.png {(bmp == null ? "missing" : $"{bmp.Width}×{bmp.Height}")}");
      }
      Check(proxies.Count == 24, $"{proxies.Count} components (7 × 2D, 7 × 3D, 3 × Massing, 2 × Landscape, 2 × Facade, 2 × Section, 1 × Examples)");
      // Grasshopper sorts panels by name: the numbers put Examples first, then the parts in the order they were built.
      var panels = proxies.Select(p => p.Desc.SubCategory).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
      var expected = new[] { "0 | Examples", "1 | Topo2D", "2 | Topo3D", "3 | Massing", "4 | Landscape", "5 | Facade", "6 | Section" };
      Check(panels.SequenceEqual(expected), $"panels in ribbon order: {string.Join(", ", panels)}");
      Check(Instances.ComponentServer.GetCategoryIcon("Moyu") != null, "Moyu tab has its icon");
      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    public static int Run(string gha, string images)
    {
      Render.Folder = images;
      LoadPlugin(gha);

      var proxies = Instances.ComponentServer.ObjectProxies.Where(p => p.Desc.Category == "Moyu").ToList();
      foreach (var p in proxies.OrderBy(p => p.Exposure).ThenBy(p => p.Desc.Name))
        Console.WriteLine($"  ribbon: {p.Desc.Category} > {p.Desc.SubCategory} [{p.Exposure}] {p.Desc.Name} ({p.Desc.NickName})");
      Check(proxies.Count(p => p.Desc.SubCategory == "1 | Topo2D") == 7 && proxies.Count(p => p.Desc.SubCategory == "2 | Topo3D") == 7,
        "7 components each in Moyu > 1 | Topo2D and Moyu > 2 | Topo3D");
      var icons = System.Reflection.Assembly.LoadFrom(gha).GetType("Moyu.Components.Icons").GetMethod("Get");
      var withIcon = proxies.Where(p => icons.Invoke(null, new object[] { p.Type.Name }) != null).Select(p => p.Desc.NickName).ToList();
      Console.WriteLine($"  info: embedded icons for {withIcon.Count} of {proxies.Count} components [{string.Join(", ", withIcon)}]");

      Examples(gha);
      Cantilever();
      Bridge();
      Bracket();
      Errors();
      Scenarios3D.Run(Check);
      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The Examples component's six definitions, embedded in the plugin: each reads back as an unnamed document holding
    /// its main component, and matches packaging/examples (a stale build would open old examples).
    /// </summary>
    static void Examples(string gha)
    {
      Console.WriteLine("Examples component: the six embedded example definitions");
      var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == new Guid("a106111d-3409-475f-99ce-8a0d65cdfc47"));
      Check(proxy != null && proxy.Desc.Category == "Moyu" && proxy.Desc.SubCategory == "0 | Examples", "Examples component in Moyu > 0 | Examples");
      var asm = System.Reflection.Assembly.LoadFrom(gha);
      var files = asm.GetType("Moyu.Components.Examples.ExampleFiles");
      var all = ((Array)files.GetField("All").GetValue(null)).Cast<object>().ToList();
      Check(all.Count == 6, $"{all.Count} examples");
      var main = new Dictionary<string, Guid>
      {
        ["Topo2D"] = Solver, ["Topo3D"] = Solver3D, ["VoxStack"] = GhFileStack.Study,
        ["Serpentine"] = GhFileSnake.Study, ["Rebound"] = GhFileBreakout.Study, ["Swift"] = GhFileSwift.Study,
      };
      // The repo's packaging/examples, found upwards from the plugin (a build folder or an unpacked package in dist/).
      var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(gha)));
      while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "packaging", "examples"))) dir = dir.Parent;
      string folder = dir == null ? "" : Path.Combine(dir.FullName, "packaging", "examples");
      foreach (var example in all)
      {
        string file = (string)example.GetType().GetField("File").GetValue(example);
        var doc = (GH_Document)files.GetMethod("Load").Invoke(null, new[] { example });
        int count = doc.Objects.OfType<GH_Component>().Count(c => main.TryGetValue(file, out var id) && c.ComponentGuid == id);
        Check(count > 0 && string.IsNullOrEmpty(doc.FilePath), $"{file}: opens unnamed, {doc.ObjectCount} objects, {count} main component(s)");
        string onDisk = Path.Combine(folder, file + ".gh");
        using (var stream = asm.GetManifestResourceStream($"Moyu.Examples.{file}.gh"))
        using (var memory = new MemoryStream())
        {
          stream.CopyTo(memory);
          Check(File.Exists(onDisk) && memory.ToArray().SequenceEqual(File.ReadAllBytes(onDisk)), $"{file}: embedded copy matches packaging/examples (rebuild after regenerating examples)");
        }
      }
    }

    /// <summary>Cantilever in World XY driven tick by tick like a Timer, then edited live.</summary>
    static void Cantilever()
    {
      Console.WriteLine("Cantilever (World XY): trigger stepping, live edit, reset, display modes");
      var doc = NewDocument();
      var bnd = Add(doc, Boundary);
      Set(bnd, 0, new GH_Curve(new Rectangle3d(Plane.WorldXY, 60, 30).ToNurbsCurve()));
      Set(bnd, 1, new GH_Integer(90));
      var sup = Add(doc, Support);
      Set(sup, 0, new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(0, 30, 0))));
      var load = Add(doc, Load);
      Set(load, 0, new GH_Point(new Point3d(60, 15, 0)));
      var solver = Add(doc, Solver);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(sup, 0, solver, 2);
      Set(solver, 9, new GH_Integer(5));
      var vis = Add(doc, Visualize);
      Wire(solver, 0, vis, 0);

      doc.NewSolution(false);
      Check(bnd.Message == "90 × 45", $"boundary grid {bnd.Message}");
      Check(Int(solver, 1) == 0 && solver.Message == "Ready", $"Run off → no iterations ({solver.Message})");
      Render.Result(vis, solver, "solver_iter000.png", contours: false);

      Set(solver, 10, new GH_Boolean(true));
      doc.NewSolution(false);
      Check(Int(solver, 1) == 5, $"one solution runs Steps = 5 iterations ({Int(solver, 1)})");
      Check(HasMessage(solver, GH_RuntimeMessageLevel.Remark, "Timer"), "remark suggests attaching a Timer");
      Render.Result(vis, solver, "solver_iter005.png", contours: false);

      for (int k = 0; k < 3; k++) Tick(doc, solver);
      Check(Int(solver, 1) == 20, $"three timer ticks → 20 iterations ({Int(solver, 1)})");
      Render.Result(vis, solver, "solver_iter020.png", contours: false);

      var sw = Stopwatch.StartNew();
      Set(solver, 9, new GH_Integer(50));
      doc.NewSolution(false);
      Converge(doc, solver);
      Check(Bool(solver, 4), $"converged at iteration {Int(solver, 1)}, {sw.ElapsedMilliseconds} ms ({solver.Message})");
      Check(Math.Abs(Num(solver, 3) - 0.4) < 0.002, $"volume {Num(solver, 3):F4}");
      var history = Nums(solver, 5);
      Check(history.Last() < history.First() * 0.5, $"compliance {history.First():F1} → {history.Last():F1}");
      Render.Csv("cantilever_history.csv", history);

      var mesh = MeshOut(vis);
      var contours = CurvesOut(vis);
      Check(mesh.Faces.Count == 90 * 45 && mesh.VertexColors.Count == mesh.Vertices.Count, $"pixel mesh: {mesh.Faces.Count} coloured faces");
      Check(contours.Count >= 2 && contours.All(c => c.IsClosed), $"{contours.Count} closed contour curves");
      Render.Result(vis, solver, "vis_pixels.png");
      Set(vis, 1, new GH_Integer(1));
      doc.NewSolution(false);
      Check(MeshOut(vis).Faces.Count < 90 * 45 * 0.6, $"solid mode keeps {MeshOut(vis).Faces.Count} faces above the threshold");
      Render.Result(vis, solver, "vis_solid.png");
      Set(vis, 1, new GH_Integer(2));
      doc.NewSolution(false);
      Check(MeshOut(vis).Vertices.Count == 91 * 46, $"smooth mode shares vertices ({MeshOut(vis).Vertices.Count})");
      Render.Result(vis, solver, "vis_smooth.png");
      Set(vis, 1, new GH_Integer(0));

      // Live edit while running: densities carry over, convergence and the iteration budget reset.
      int before = Int(solver, 1);
      Set(load, 0, new GH_Point(new Point3d(60, 30, 0)));
      Set(solver, 9, new GH_Integer(3));
      doc.NewSolution(false);
      Check(Int(solver, 1) == before + 3 && !Bool(solver, 4), $"moved load continues {before} → {Int(solver, 1)}");
      Set(solver, 9, new GH_Integer(50));
      doc.NewSolution(false);
      Converge(doc, solver);
      Check(Bool(solver, 4), $"re-converged at iteration {Int(solver, 1)}");
      Render.Result(vis, solver, "live_moved_load.png");

      Set(solver, 11, new GH_Boolean(true));
      doc.NewSolution(false);
      Check(Int(solver, 1) == 0 && solver.Message == "Ready", $"reset → iteration 0 ({solver.Message})");
    }

    /// <summary>Bridge elevation in World XZ: curve load, pin + roller, solid deck, void opening, real-unit material.</summary>
    static void Bridge()
    {
      Console.WriteLine("Bridge (World XZ): curve load, areas, material");
      var doc = NewDocument();
      var xz = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis);
      var bnd = Add(doc, Boundary);
      Set(bnd, 0, new GH_Curve(new Rectangle3d(xz, 100, 40).ToNurbsCurve()));
      Set(bnd, 1, new GH_Integer(120));
      var load = Add(doc, Load);
      Set(load, 0, new GH_Curve(new LineCurve(new Point3d(0, 0, 40), new Point3d(100, 0, 40))));
      Set(load, 1, new GH_Vector(new Vector3d(0, 0, -10)));
      var pin = Add(doc, Support);
      Set(pin, 0, new GH_Point(new Point3d(0, 0, 0)));
      var roller = Add(doc, Support);
      Set(roller, 0, new GH_Point(new Point3d(100, 0, 0)));
      Set(roller, 1, new GH_Boolean(false));
      var deck = Add(doc, Area);
      Set(deck, 0, new GH_Curve(new Rectangle3d(new Plane(new Point3d(0, 0, 37), Vector3d.XAxis, Vector3d.ZAxis), 100, 3).ToNurbsCurve()));
      Set(deck, 1, new GH_Boolean(true));
      var opening = Add(doc, Area);
      Set(opening, 0, new GH_Curve(new Circle(new Plane(new Point3d(50, 0, 14), Vector3d.XAxis, Vector3d.ZAxis), 9).ToNurbsCurve()));
      var mat = Add(doc, Material);
      Set(mat, 0, new GH_Number(210000));
      var solver = Add(doc, Solver);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(pin, 0, solver, 2);
      Wire(roller, 0, solver, 2, append: true);
      Wire(deck, 0, solver, 3);
      Wire(opening, 0, solver, 3, append: true);
      Wire(mat, 0, solver, 4);
      Set(solver, 5, new GH_Number(0.3));
      Set(solver, 7, new GH_Number(2.5));
      Set(solver, 9, new GH_Integer(50));
      var vis = Add(doc, Visualize);
      Wire(solver, 0, vis, 0);

      doc.NewSolution(false);
      Check(bnd.Message == "120 × 48", $"XZ boundary grid {bnd.Message}");
      Check(roller.Message == "Roller Y", $"roller support ({roller.Message})");
      foreach (var c in new[] { bnd, load, pin, roller, deck, opening })
        Check(c.IsPreviewCapable && c.ClippingBox.IsValid, $"{c.NickName} previews itself in the viewport");
      Render.Result(vis, solver, "conditions_bridge.png", contours: false);

      Set(solver, 10, new GH_Boolean(true));
      var sw = Stopwatch.StartNew();
      doc.NewSolution(false);
      PrintMessages(solver);
      Converge(doc, solver);
      Check(Bool(solver, 4) && !HasMessage(solver, GH_RuntimeMessageLevel.Error), $"converged at iteration {Int(solver, 1)}, {sw.ElapsedMilliseconds} ms");
      Check(Math.Abs(Num(solver, 3) - 0.3) < 0.003, $"volume {Num(solver, 3):F4}");

      dynamic r = Result(solver);
      Plane plane = r.Domain.Plane;
      double size = r.Domain.PixelSize;
      var density = (double[])r.Density;
      double At(Point3d p)
      {
        plane.ClosestParameter(p, out double s, out double t);
        return density[(int)(t / size) * 120 + (int)(s / size)];
      }
      Check(Math.Abs(plane.ZAxis.Y) > 0.999, "grid plane follows the XZ elevation");
      Check(At(new Point3d(50, 0, 14)) == 0.0, "void opening stays empty");
      Check(At(new Point3d(20, 0, 38.5)) == 1.0, "solid deck stays full");
      Render.Csv("bridge_history.csv", Nums(solver, 5));
      Render.Result(vis, solver, "bridge_result.png");
    }

    /// <summary>L-shaped bracket with a bolt hole given as a nested boundary curve.</summary>
    static void Bracket()
    {
      Console.WriteLine("Bracket (World XY): non-rectangular boundary with a hole");
      var doc = NewDocument();
      var outline = new Polyline(new[]
      {
        new Point3d(0, 0, 0), new Point3d(40, 0, 0), new Point3d(40, 40, 0), new Point3d(100, 40, 0),
        new Point3d(100, 100, 0), new Point3d(0, 100, 0), new Point3d(0, 0, 0),
      });
      var bnd = Add(doc, Boundary);
      Set(bnd, 0, new GH_Curve(outline.ToNurbsCurve()), new GH_Curve(new Circle(new Point3d(20, 75, 0), 6).ToNurbsCurve()));
      Set(bnd, 1, new GH_Integer(60));
      var sup = Add(doc, Support);
      Set(sup, 0, new GH_Curve(new LineCurve(new Point3d(0, 100, 0), new Point3d(40, 100, 0))));
      var load = Add(doc, Load);
      Set(load, 0, new GH_Point(new Point3d(100, 45, 0)));
      Set(load, 1, new GH_Vector(new Vector3d(0, -5, 0)));
      var solver = Add(doc, Solver);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(sup, 0, solver, 2);
      Set(solver, 5, new GH_Number(0.35));
      Set(solver, 9, new GH_Integer(50));
      var vis = Add(doc, Visualize);
      Wire(solver, 0, vis, 0);
      doc.NewSolution(false);
      Render.Domain(solver, "boundary_bracket_pixels.png");

      Set(bnd, 1, new GH_Integer(100));
      Set(solver, 10, new GH_Boolean(true));
      Set(vis, 1, new GH_Integer(2));
      var sw = Stopwatch.StartNew();
      doc.NewSolution(false);
      dynamic r = Result(solver);
      int outside = ((Array)r.Kinds).Cast<object>().Count(k => k.ToString() == "Outside");
      Check(Math.Abs(outside - (2400 + 113)) < 40, $"L cut-out (2400 px) + hole (~113 px) are outside the domain ({outside})");
      Converge(doc, solver);
      Check(Bool(solver, 4) && !HasMessage(solver, GH_RuntimeMessageLevel.Error), $"converged at iteration {Int(solver, 1)}, {sw.ElapsedMilliseconds} ms");
      Render.Result(vis, solver, "bracket_result.png");
    }

    static void Errors()
    {
      Console.WriteLine("Error handling");
      var doc = NewDocument();
      var bnd = Add(doc, Boundary);
      Set(bnd, 0, new GH_Curve(new Rectangle3d(Plane.WorldXY, 20, 10).ToNurbsCurve()));
      Set(bnd, 1, new GH_Integer(20));
      var load = Add(doc, Load);
      Set(load, 0, new GH_Point(new Point3d(20, 5, 0)));
      var roller = Add(doc, Support);
      Set(roller, 0, new GH_Point(new Point3d(0, 0, 0)));
      Set(roller, 1, new GH_Boolean(false));
      var solver = Add(doc, Solver);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(roller, 0, solver, 2);
      Set(solver, 10, new GH_Boolean(true));
      var vis = Add(doc, Visualize);
      Wire(solver, 0, vis, 0);
      doc.NewSolution(false);
      PrintMessages(solver);
      Check(solver.Message == "Unstable" && HasMessage(solver, GH_RuntimeMessageLevel.Error), "a single roller is reported as unstable");
      Check(!Data(vis, 0).Any() && !vis.ClippingBox.IsValid, "visualize shows nothing while the solver fails");

      var perpendicular = Add(doc, Load);
      Set(perpendicular, 0, new GH_Point(new Point3d(10, 5, 0)));
      Set(perpendicular, 1, new GH_Vector(new Vector3d(0, 0, -1)));
      Wire(perpendicular, 0, solver, 1);
      doc.NewSolution(false);
      PrintMessages(solver);
      Check(HasMessage(solver, GH_RuntimeMessageLevel.Error, "No usable load"), "a load normal to the plane is rejected");

      var open = Add(doc, Boundary);
      Set(open, 0, new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(1, 1, 0))));
      doc.NewSolution(false);
      PrintMessages(open);
      Check(HasMessage(open, GH_RuntimeMessageLevel.Error, "closed"), "an open boundary curve is rejected");
    }

    /// <summary>
    /// Re-solves the way a Timer would until the solver converges: without a Timer one solution runs at most 50
    /// iterations (Steps is clamped to 1–50).
    /// </summary>
    static void Converge(GH_Document doc, GH_Component solver)
    {
      for (int k = 0; k < 40 && !Bool(solver, 4) && !HasMessage(solver, GH_RuntimeMessageLevel.Error); k++) Tick(doc, solver);
    }
  }
}
