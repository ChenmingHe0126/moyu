using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Writes tests/gh/Moyu3D-Test.gh: three ready-to-run 3D examples built from native Grasshopper geometry
  /// and sliders, wired with Run toggles, Reset buttons and Timers; then reopens it and checks it.
  /// </summary>
  static class GhFile3D
  {
    static int _failures;

    static void Check(bool ok, string what)
    {
      Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
      if (!ok) _failures++;
    }

    // Canvas columns.
    const float C1 = 240, C2 = 380, C3 = 520, C4 = 680, M = 860, S = 1080, V = 1360;

    public static int Run(string gha, string path)
    {
      LoadPlugin(gha);
      Directory.CreateDirectory(Path.GetDirectoryName(path));

      var doc = NewDocument();
      var b = new GhBuilder(doc, Solver3D, Visualize3D);
      b.Title(0, -150,
        "Moyu 3D 拓扑优化示例 · 3D topology optimisation example\n" +
        "把某组的 Run 开关拨到 True 开始（Timer 已接好），Reset 回到均匀密度；Visualize 的 Mode：0 体素，1 光滑表面。\n" +
        "Set a group's Run toggle to True to start, Reset starts over; Visualize Mode 0 = voxels, 1 = smooth surface.");
      var checks = new List<(GH_Component c, string what, Func<BoundingBox, bool> ok)>();
      Cantilever(b, 0, 0, checks);
      Canopy(b, 0, 640, checks);
      Bracket(b, 0, 1330, checks);

      doc.NewSolution(false);
      var components = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in components) PrintMessages(c);
      Check(components.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"built: all {components.Count} components solve without warnings or errors");
      foreach (var (c, what, ok) in checks)
      {
        var box = BoundingBox.Empty;
        foreach (var g in Data(c, 0).OfType<IGH_GeometricGoo>()) box.Union(g.Boundingbox);
        Check(ok(box), $"{what}: {box.Min} → {box.Max}");
      }

      var io = new GH_DocumentIO(doc);
      Check(io.SaveQuiet(path), $"saved {path}");
      GhBuilder.Verify(path, Solver3D, Visualize3D, 3, 150, Check);
      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    static bool Near(Point3d a, double x, double y, double z) => a.DistanceTo(new Point3d(x, y, z)) < 1e-6;

    /// <summary>Cantilever box at the origin: fixed end face (x = 0), line load along the bottom of the free end.</summary>
    static void Cantilever(GhBuilder b, float x, float y, List<(GH_Component, string, Func<BoundingBox, bool>)> checks)
    {
      var m = new List<IGH_DocumentObject>();
      var length = b.Slider("Length", 10, 40, 100, x, y + 20, m);
      var width = b.Slider("Width", 5, 20, 60, x, y + 60, m);
      var height = b.Slider("Height", 5, 20, 60, x, y + 100, m);
      var res = b.Slider("Resolution", 8, 32, 96, x, y + 140, m, integer: true);
      var vol = b.Slider("Volume", 0.05, 0.2, 0.6, x, y + 180, m);
      var mode = b.Slider("Mode", 0, 1, 1, x, y + 220, m, integer: true);

      var plane = b.Native("XY Plane", "Vector", x + C1, y + 10, m);
      var box = b.Native("Domain Box", "Surface", x + C2, y + 10, m);
      b.Wire(plane, 0, box, 0);
      b.Wire(length, box, 1);
      b.Wire(width, box, 2);
      b.Wire(height, box, 3);

      var yz = b.Native("YZ Plane", "Vector", x + C1, y + 130, m);
      var rect = b.Native("Rectangle", "Curve", x + C2, y + 130, m);
      b.Wire(yz, 0, rect, 0);
      b.Wire(width, rect, 1);
      b.Wire(height, rect, 2);
      var face = b.Native("Boundary Surfaces", "Surface", x + C3, y + 130, m);
      b.Wire(rect, 0, face, 0);
      checks.Add((face, "A support face lies on x = 0", bb => Math.Abs(bb.Min.X) < 1e-9 && Math.Abs(bb.Max.X) < 1e-9 && Near(bb.Max, 0, 20, 20)));

      var pa = b.Native("Construct Point", "Vector", x + C1, y + 240, m);
      b.Wire(length, pa, 0);
      var pb = b.Native("Construct Point", "Vector", x + C1, y + 330, m);
      b.Wire(length, pb, 0);
      b.Wire(width, pb, 1);
      var line = b.Native("Line", "Curve", x + C2, y + 270, m);
      b.Wire(pa, 0, line, 0);
      b.Wire(pb, 0, line, 1);

      var bnd = b.Moyu(Boundary3D, x + M, y + 10, m);
      b.Wire(box, 0, bnd, 0);
      b.Wire(res, bnd, 1);
      var sup = b.Moyu(Support3D, x + M, y + 120, m);
      b.Wire(face, 0, sup, 0);
      var load = b.Moyu(Load3D, x + M, y + 240, m);
      b.Wire(line, 0, load, 0);

      var solver = b.Solver(x + S, y, vol, m);
      b.Wire(bnd, 0, solver, 0);
      b.Wire(load, 0, solver, 1);
      b.Wire(sup, 0, solver, 2);
      b.Visualize(x + V, y + 30, solver, mode, m);
      b.Group("A · 3D 悬臂梁（World XY，原点）· x = 0 端面固定，自由端底边线荷载",
        "A · 3D cantilever (World XY, origin) · end face at x = 0 fixed, line load on the free end's bottom edge", Color.FromArgb(70, 120, 170, 220), m);
    }

    /// <summary>Canopy at x = 60: surface load on the roof, four corner pins, solid roof slab, void passage underneath.</summary>
    static void Canopy(GhBuilder b, float x, float y, List<(GH_Component, string, Func<BoundingBox, bool>)> checks)
    {
      var m = new List<IGH_DocumentObject>();
      var span = b.Slider("Span", 10, 40, 80, x, y + 20, m);
      var height = b.Slider("Height", 4, 16, 40, x, y + 60, m);
      var res = b.Slider("Resolution", 8, 32, 96, x, y + 100, m, integer: true);
      var vol = b.Slider("Volume", 0.05, 0.2, 0.6, x, y + 140, m);
      var mode = b.Slider("Mode", 0, 1, 1, x, y + 180, m, integer: true);

      // Columns of this group: maths, points, planes, boxes/rectangles, surfaces, then Moyu.
      const float Cm = 200, Cp = 330, Cpl = 470, Cg1 = 610, Cg2 = 770, Cmoyu = 950;

      GH_Component Plane(float cy, double px, double py, GH_Component z = null)
      {
        var pt = b.Native("Construct Point", "Vector", x + Cp, y + cy, m);
        b.Set(pt, 0, new GH_Number(px));
        b.Set(pt, 1, new GH_Number(py));
        if (z != null) b.Wire(z, 0, pt, 2);
        var pl = b.Native("XY Plane", "Vector", x + Cpl, y + cy, m);
        b.Wire(pt, 0, pl, 0);
        return pl;
      }

      // Domain box.
      var basePlane = Plane(10, 60, 0);
      var box = b.Native("Domain Box", "Surface", x + Cg1, y + 10, m);
      b.Wire(basePlane, 0, box, 0);
      b.Wire(span, box, 1);
      b.Wire(span, box, 2);
      b.Wire(height, box, 3);

      // Roof surface carrying the load, at z = Height.
      var topPt = b.Native("Construct Point", "Vector", x + Cp, y + 120, m);
      b.Set(topPt, 0, new GH_Number(60));
      b.Wire(height, topPt, 2);
      var topPlane = b.Native("XY Plane", "Vector", x + Cpl, y + 120, m);
      b.Wire(topPt, 0, topPlane, 0);
      var topRect = b.Native("Rectangle", "Curve", x + Cg1, y + 120, m);
      b.Wire(topPlane, 0, topRect, 0);
      b.Wire(span, topRect, 1);
      b.Wire(span, topRect, 2);
      var roof = b.Native("Boundary Surfaces", "Surface", x + Cg2, y + 120, m);
      b.Wire(topRect, 0, roof, 0);
      checks.Add((roof, "B roof surface spans the top", bb => Near(bb.Min, 60, 0, 16) && Near(bb.Max, 100, 40, 16)));

      // Corner pins: vertices of the footprint rectangle.
      var foot = b.Native("Rectangle", "Curve", x + Cg1, y + 230, m);
      b.Wire(basePlane, 0, foot, 0);
      b.Wire(span, foot, 1);
      b.Wire(span, foot, 2);
      var corners = b.Native("Explode", "Curve", x + Cg2, y + 230, m);
      b.Wire(foot, 0, corners, 0);

      // Solid roof slab 1.5 thick under the roof surface.
      var slabZ = b.Native("Subtraction", "Maths", x + Cm, y + 340, m);
      b.Wire(height, slabZ, 0);
      b.Set(slabZ, 1, new GH_Number(1.5));
      var slabPlane = Plane(340, 60, 0, slabZ);
      var slab = b.Native("Domain Box", "Surface", x + Cg1, y + 340, m);
      b.Wire(slabPlane, 0, slab, 0);
      b.Wire(span, slab, 1);
      b.Wire(span, slab, 2);
      b.Set(slab, 3, new GH_Interval(new Interval(0, 1.5)));

      // Empty passage through the middle, 14 wide and 7 high.
      var passPlane = Plane(460, 73, -1);
      var passage = b.Native("Domain Box", "Surface", x + Cg1, y + 460, m);
      b.Wire(passPlane, 0, passage, 0);
      b.Set(passage, 1, new GH_Interval(new Interval(0, 14)));
      b.Set(passage, 2, new GH_Interval(new Interval(0, 42)));
      b.Set(passage, 3, new GH_Interval(new Interval(0, 7)));

      float mx = Cmoyu, sx = Cmoyu + 220, vx = Cmoyu + 500;
      var bnd = b.Moyu(Boundary3D, x + mx, y + 10, m);
      b.Wire(box, 0, bnd, 0);
      b.Wire(res, bnd, 1);
      var load = b.Moyu(Load3D, x + mx, y + 120, m);
      b.Wire(roof, 0, load, 0);
      var pins = b.Moyu(Support3D, x + mx, y + 230, m);
      b.Wire(corners, 1, pins, 0);
      var slabArea = b.Moyu(Area3D, x + mx, y + 350, m);
      b.Wire(slab, 0, slabArea, 0);
      b.Set(slabArea, 1, new GH_Boolean(true));
      var passArea = b.Moyu(Area3D, x + mx, y + 440, m);
      b.Wire(passage, 0, passArea, 0);

      var solver = b.Solver(x + sx, y + 60, vol, m);
      b.Wire(bnd, 0, solver, 0);
      b.Wire(load, 0, solver, 1);
      b.Wire(pins, 0, solver, 2);
      b.Wire(slabArea, 0, solver, 3);
      b.Wire(passArea, 0, solver, 3, append: true);
      b.Visualize(x + vx, y + 90, solver, mode, m);
      b.Group("B · 3D 雨棚（x = 60…100）· 顶面面荷载 + 四角铰支 + 实心屋面板 + 空心通道",
        "B · 3D canopy (x = 60…100) · surface load on the roof + four pinned corners + solid roof slab + void passage underneath", Color.FromArgb(70, 220, 170, 120), m);
    }

    /// <summary>Bracket at x = 120: extruded L plate with a bolt hole (nested cylinder), fixed top face, tip load.</summary>
    static void Bracket(GhBuilder b, float x, float y, List<(GH_Component, string, Func<BoundingBox, bool>)> checks)
    {
      var m = new List<IGH_DocumentObject>();
      var thick = b.Slider("Thickness", 4, 12, 30, x, y + 20, m);
      var res = b.Slider("Resolution", 8, 40, 96, x, y + 60, m, integer: true);
      var vol = b.Slider("Volume", 0.05, 0.3, 0.6, x, y + 100, m);
      var mode = b.Slider("Mode", 0, 1, 1, x, y + 140, m, integer: true);

      var outline = b.Native("PolyLine", "Curve", x + C1, y + 10, m);
      b.Set(outline, 0, new[] { (120, 0), (160, 0), (160, 40), (220, 40), (220, 100), (120, 100) }
        .Select(p => (IGH_Goo)new GH_Point(new Point3d(p.Item1, p.Item2, 0))).ToArray());
      b.Set(outline, 1, new GH_Boolean(true));
      var plan = b.Native("Boundary Surfaces", "Surface", x + C2, y + 10, m);
      b.Wire(outline, 0, plan, 0);
      var up = b.Native("Unit Z", "Vector", x + C1, y + 110, m);
      b.Wire(thick, up, 0);
      var plate = b.Native("Extrude", "Surface", x + C3, y + 10, m);
      b.Wire(plan, 0, plate, 0);
      b.Wire(up, 0, plate, 1);

      var holeCentre = b.Native("XY Plane", "Vector", x + C1, y + 200, m);
      b.Set(holeCentre, 0, new GH_Point(new Point3d(140, 75, 0)));
      var cylinder = b.Native("Cylinder", "Surface", x + C2, y + 200, m);
      b.Wire(holeCentre, 0, cylinder, 0);
      b.Set(cylinder, 1, new GH_Number(6));
      b.Wire(thick, cylinder, 2);
      var hole = b.Native("Cap Holes", "Surface", x + C3, y + 200, m);
      b.Wire(cylinder, 0, hole, 0);

      var supPlane = b.Native("XZ Plane", "Vector", x + C1, y + 300, m);
      b.Set(supPlane, 0, new GH_Point(new Point3d(120, 100, 0)));
      var supRect = b.Native("Rectangle", "Curve", x + C2, y + 300, m);
      b.Wire(supPlane, 0, supRect, 0);
      b.Set(supRect, 1, new GH_Interval(new Interval(0, 40)));
      b.Wire(thick, supRect, 2);
      var supFace = b.Native("Boundary Surfaces", "Surface", x + C3, y + 300, m);
      b.Wire(supRect, 0, supFace, 0);
      checks.Add((supFace, "C support face is the top edge face (y = 100)", bb =>
        Math.Abs(bb.Min.Y - 100) < 1e-9 && Math.Abs(bb.Max.Y - 100) < 1e-9 && Near(bb.Min, 120, 100, 0) && Near(bb.Max, 160, 100, 12)));

      var half = b.Native("Division", "Maths", x + C1, y + 410, m);
      b.Wire(thick, half, 0);
      b.Set(half, 1, new GH_Number(2));
      var tip = b.Native("Construct Point", "Vector", x + C2, y + 410, m);
      b.Set(tip, 0, new GH_Number(220));
      b.Set(tip, 1, new GH_Number(45));
      b.Wire(half, 0, tip, 2);

      var bnd = b.Moyu(Boundary3D, x + M, y + 10, m);
      b.Wire(plate, 0, bnd, 0);
      b.Wire(hole, 0, bnd, 0, append: true);
      b.Wire(res, bnd, 1);
      var sup = b.Moyu(Support3D, x + M, y + 120, m);
      b.Wire(supFace, 0, sup, 0);
      var load = b.Moyu(Load3D, x + M, y + 240, m);
      b.Wire(tip, 0, load, 0);
      b.Set(load, 1, new GH_Vector(new Vector3d(0, -5, 0)));

      var solver = b.Solver(x + S, y, vol, m);
      b.Wire(bnd, 0, solver, 0);
      b.Wire(load, 0, solver, 1);
      b.Wire(sup, 0, solver, 2);
      b.Visualize(x + V, y + 30, solver, mode, m);
      b.Group("C · 3D L 形支架 + 螺栓孔（x = 120…220）· 嵌套圆柱 = 孔，顶面固定，端部水平荷载",
        "C · 3D L-shaped bracket with a bolt hole (x = 120…220) · a nested cylinder is a hole, top face fixed, horizontal load at the tip", Color.FromArgb(70, 200, 140, 200), m);
    }
  }
}
