using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  static class Scenarios3D
  {
    static readonly Color Steel = Color.FromArgb(70, 110, 190);
    static readonly Color Shell = Color.FromArgb(205, 205, 205);
    static Action<bool, string> Check;

    public static void Run(Action<bool, string> check)
    {
      Check = check;
      Cantilever();
      Canopy();
      Bracket();
      Errors();
    }

    static Brep BoxBrep(double x0, double y0, double z0, double x1, double y1, double z1) =>
      new Box(Plane.WorldXY, new Interval(x0, x1), new Interval(y0, y1), new Interval(z0, z1)).ToBrep();

    static Brep Planar(Rectangle3d r) => Brep.CreatePlanarBreps(r.ToNurbsCurve(), 1e-6)[0];

    static (List<(Point3d[], Vector3d)>, List<Point3d[]>) Marks(object resultObject)
    {
      dynamic result = resultObject;
      var loads = new List<(Point3d[], Vector3d)>();
      foreach (var l in result.Loads) loads.Add(((Point3d[])l.Nodes, (Vector3d)l.Force));
      var supports = new List<Point3d[]>();
      foreach (var s in result.Supports) supports.Add((Point3d[])s.Nodes);
      return (loads, supports);
    }

    static void Snapshot(GH_Component solver, GH_Component vis, string file, bool surface = true)
    {
      dynamic r = Result(solver);
      var (loads, supports) = Marks((object)r);
      var mesh = ((GH_Mesh)Data(vis, surface ? 1 : 0).First()).Value;
      Render3D.Mesh(mesh, (BoundingBox)r.Domain.Bounds, loads, supports, Steel, file);
    }

    static void DomainShot(GH_Component solver, string file)
    {
      dynamic r = Result(solver);
      var (loads, supports) = Marks((object)r);
      Render3D.Mesh((Mesh)r.Domain.Shell, (BoundingBox)r.Domain.Bounds, loads, supports, Shell, file);
    }

    /// <summary>Every edge of a closed (possibly pinched) surface touches an even number of faces.</summary>
    /// <summary>Ticks like a Timer until the solver converges or stops at its iteration budget.</summary>
    static void RunToEnd(GH_Document doc, GH_Component solver, int maxTicks = 400)
    {
      for (int k = 0; k < maxTicks && !Bool(solver, 4) && !solver.Message.StartsWith("Max iter") && !solver.Message.StartsWith("Unstable"); k++)
        Tick(doc, solver);
    }

    static bool Closed(Mesh m)
    {
      var t = m.TopologyEdges;
      for (int e = 0; e < t.Count; e++)
        if (t.GetConnectedFaces(e).Length % 2 != 0) return false;
      return m.Faces.Count > 0;
    }

    static void Cantilever()
    {
      Console.WriteLine("3D cantilever: fixed end face, line load along the bottom of the free end");
      var doc = NewDocument();
      var bnd = Add(doc, Boundary3D);
      Set(bnd, 0, new GH_Brep(BoxBrep(0, 0, 0, 40, 20, 20)));
      Set(bnd, 1, new GH_Integer(32));
      var sup = Add(doc, Support3D);
      Set(sup, 0, new GH_Brep(Planar(new Rectangle3d(Plane.WorldYZ, 20, 20))));
      var load = Add(doc, Load3D);
      Set(load, 0, new GH_Curve(new LineCurve(new Point3d(40, 0, 0), new Point3d(40, 20, 0))));
      var solver = Add(doc, Solver3D);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(sup, 0, solver, 2);
      var vis = Add(doc, Visualize3D);
      Wire(solver, 0, vis, 0);

      doc.NewSolution(false);
      PrintMessages(bnd);
      Check(bnd.Message == "32 × 16 × 16", $"voxel grid {bnd.Message}");
      Check(sup.Message == "Fixed" && load.Message == "curve", $"support {sup.Message}, load on a {load.Message}");
      Check(bnd.IsPreviewCapable && bnd.ClippingBox.IsValid && sup.ClippingBox.IsValid && load.ClippingBox.IsValid, "conditions preview themselves");
      Check(Int(solver, 1) == 0 && solver.Message == "Ready", $"Run off → idle ({solver.Message})");
      dynamic r0 = Result(solver);
      int fixedNodes = ((Point3d[])r0.Supports[0].Nodes).Length;
      Check(fixedNodes == 17 * 17, $"end face fixes its {fixedNodes} nodes (17 × 17)");
      DomainShot(solver, "c3d_iter000.png");

      Set(solver, 10, new GH_Boolean(true));
      var sw = Stopwatch.StartNew();
      doc.NewSolution(false);
      for (int k = 0; k < 20 && Int(solver, 1) < 10; k++) Tick(doc, solver);
      Snapshot(solver, vis, "c3d_iter010.png");
      for (int k = 0; k < 40 && Int(solver, 1) < 30; k++) Tick(doc, solver);
      Snapshot(solver, vis, "c3d_iter030.png");
      RunToEnd(doc, solver);
      PrintMessages(solver);
      Check(Bool(solver, 4), $"converged at iteration {Int(solver, 1)} in {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / Math.Max(1, Int(solver, 1))} ms/iteration incl. display)");
      Check(Math.Abs(Num(solver, 3) - 0.2) < 0.002, $"volume {Num(solver, 3):F4}");
      var history = Nums(solver, 5);
      Check(history.Last() < history.First() * 0.5, $"compliance {history.First():F1} → {history.Last():F2}");
      Render.Csv("cantilever3d_history.csv", history);

      var surface = ((GH_Mesh)Data(vis, 1).First()).Value;
      Check(Closed(surface), $"iso-surface is closed ({surface.Faces.Count} faces)");
      Snapshot(solver, vis, "c3d_final.png");
      Set(vis, 1, new GH_Integer(0));
      doc.NewSolution(false);
      var voxels = ((GH_Mesh)Data(vis, 0).First()).Value;
      Check(voxels.Faces.Count > 0 && vis.Message == "Voxels", $"voxel mode: {voxels.Faces.Count} faces");
      Snapshot(solver, vis, "c3d_voxels.png", surface: false);
      Set(vis, 1, new GH_Integer(1));

      int before = Int(solver, 1);
      Set(load, 0, new GH_Curve(new LineCurve(new Point3d(40, 0, 20), new Point3d(40, 20, 20))));
      doc.NewSolution(false);
      RunToEnd(doc, solver);
      Check(Bool(solver, 4) && Int(solver, 1) > before, $"moved load to the top edge: continued {before} → {Int(solver, 1)} and re-converged");
      Snapshot(solver, vis, "c3d_live.png");
    }

    static void Canopy()
    {
      Console.WriteLine("3D canopy: surface load on top, four corner pins, solid roof slab, void passage");
      var doc = NewDocument();
      var bnd = Add(doc, Boundary3D);
      Set(bnd, 0, new GH_Brep(BoxBrep(0, 0, 0, 40, 40, 16)));
      Set(bnd, 1, new GH_Integer(32));
      var load = Add(doc, Load3D);
      Set(load, 0, new GH_Brep(Planar(new Rectangle3d(new Plane(new Point3d(0, 0, 16), Vector3d.ZAxis), 40, 40))));
      var pins = Add(doc, Support3D);
      Set(pins, 0, new[] { (0, 0), (40, 0), (40, 40), (0, 40) }.Select(p => (IGH_Goo)new GH_Point(new Point3d(p.Item1, p.Item2, 0))).ToArray());
      var slab = Add(doc, Area3D);
      Set(slab, 0, new GH_Brep(BoxBrep(0, 0, 14.5, 40, 40, 16)));
      Set(slab, 1, new GH_Boolean(true));
      var passage = Add(doc, Area3D);
      Set(passage, 0, new GH_Brep(BoxBrep(13, -1, 0, 27, 41, 7)));
      var solver = Add(doc, Solver3D);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(pins, 0, solver, 2);
      Wire(slab, 0, solver, 3);
      Wire(passage, 0, solver, 3, append: true);
      Set(solver, 5, new GH_Number(0.2));
      var vis = Add(doc, Visualize3D);
      Wire(solver, 0, vis, 0);

      doc.NewSolution(false);
      Check(bnd.Message == "32 × 32 × 16", $"voxel grid {bnd.Message}");
      Check(load.Message == "surface" && Data(pins, 0).Count() == 4, "surface load and four point supports");
      DomainShot(solver, "canopy_domain.png");

      Set(solver, 10, new GH_Boolean(true));
      var sw = Stopwatch.StartNew();
      doc.NewSolution(false);
      RunToEnd(doc, solver);
      PrintMessages(solver);
      Check(Bool(solver, 4) && !HasMessage(solver, GH_RuntimeMessageLevel.Error), $"converged at iteration {Int(solver, 1)} in {sw.ElapsedMilliseconds} ms");

      dynamic r = Result(solver);
      var density = (double[])r.Density;
      dynamic dom = r.Domain;
      double At(double x, double y, double z)
      {
        Point3d g = dom.ToGrid(new Point3d(x, y, z));
        return density[((int)g.Z * (int)dom.Grid.Ny + (int)g.Y) * (int)dom.Grid.Nx + (int)g.X];
      }
      Check(At(20, 20, 15.4) == 1.0 && At(3, 37, 15.4) == 1.0, "roof slab stays solid");
      Check(At(20, 20, 2) == 0.0 && At(20, 5, 5) == 0.0, "passage stays empty");
      Snapshot(solver, vis, "canopy_result.png");
    }

    static void Bracket()
    {
      Console.WriteLine("3D bracket: extruded L with a bolt hole (nested solid)");
      var doc = NewDocument();
      var outline = new Polyline(new[]
      {
        new Point3d(0, 0, 0), new Point3d(40, 0, 0), new Point3d(40, 40, 0), new Point3d(100, 40, 0),
        new Point3d(100, 100, 0), new Point3d(0, 100, 0), new Point3d(0, 0, 0),
      });
      var plate = Extrusion.Create(outline.ToNurbsCurve(), 12, true).ToBrep();
      var bolt = new Cylinder(new Circle(new Plane(new Point3d(20, 75, 0), Vector3d.ZAxis), 6), 12).ToBrep(true, true);
      var bnd = Add(doc, Boundary3D);
      Set(bnd, 0, new GH_Brep(plate), new GH_Brep(bolt));
      Set(bnd, 1, new GH_Integer(40));
      var sup = Add(doc, Support3D);
      Set(sup, 0, new GH_Brep(Planar(new Rectangle3d(new Plane(new Point3d(0, 100, 0), Vector3d.XAxis, Vector3d.ZAxis), 40, 12))));
      var load = Add(doc, Load3D);
      Set(load, 0, new GH_Point(new Point3d(100, 45, 6)));
      Set(load, 1, new GH_Vector(new Vector3d(0, -5, 0)));
      var solver = Add(doc, Solver3D);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(sup, 0, solver, 2);
      Set(solver, 5, new GH_Number(0.3));
      var vis = Add(doc, Visualize3D);
      Wire(solver, 0, vis, 0);

      doc.NewSolution(false);
      PrintMessages(bnd);
      dynamic r = Result(solver);
      int inside = (int)r.Domain.InsideCount;
      // 7600 - π·6² in plan, voxel 2.5 → ~1198 voxels per layer, 5 layers inside the 12 thickness.
      Check(Math.Abs(inside - 5990) < 180, $"L plate minus bolt hole voxelises to {inside} voxels (expected ≈ 5990)");
      Check(!HasMessage(bnd, GH_RuntimeMessageLevel.Warning), "breps are closed");
      DomainShot(solver, "bracket3d_domain.png");

      Set(solver, 10, new GH_Boolean(true));
      var sw = Stopwatch.StartNew();
      doc.NewSolution(false);
      RunToEnd(doc, solver);
      PrintMessages(solver);
      Check(Bool(solver, 4) && !HasMessage(solver, GH_RuntimeMessageLevel.Error), $"converged at iteration {Int(solver, 1)} in {sw.ElapsedMilliseconds} ms");
      Snapshot(solver, vis, "bracket3d_result.png");
    }

    static void Errors()
    {
      Console.WriteLine("3D error handling");
      var doc = NewDocument();
      var bnd = Add(doc, Boundary3D);
      Set(bnd, 0, new GH_Brep(BoxBrep(0, 0, 0, 20, 10, 10)));
      Set(bnd, 1, new GH_Integer(16));
      var load = Add(doc, Load3D);
      Set(load, 0, new GH_Point(new Point3d(20, 5, 5)));
      var pin = Add(doc, Support3D);
      Set(pin, 0, new GH_Point(new Point3d(0, 5, 5)));
      var solver = Add(doc, Solver3D);
      Wire(bnd, 0, solver, 0);
      Wire(load, 0, solver, 1);
      Wire(pin, 0, solver, 2);
      Set(solver, 10, new GH_Boolean(true));
      var vis = Add(doc, Visualize3D);
      Wire(solver, 0, vis, 0);
      doc.NewSolution(false);
      PrintMessages(solver);
      Check(solver.Message == "Unstable" && HasMessage(solver, GH_RuntimeMessageLevel.Error), "a single pinned node (free to rotate) is reported as unstable");
      Check(!Data(vis, 0).Any(), "visualize shows nothing while the solver fails");

      var open = Add(doc, Boundary3D);
      Set(open, 0, new GH_Brep(Planar(new Rectangle3d(Plane.WorldXY, 10, 10))));
      doc.NewSolution(false);
      PrintMessages(open);
      Check(HasMessage(open, GH_RuntimeMessageLevel.Warning, "not closed") || HasMessage(open, GH_RuntimeMessageLevel.Error),
        "an open surface as boundary is flagged");
    }
  }
}
