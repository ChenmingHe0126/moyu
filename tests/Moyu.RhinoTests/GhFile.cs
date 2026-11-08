using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Writes tests/gh/Moyu2D-Test.gh: three ready-to-run 2D examples wired with sliders, toggles,
  /// buttons and Timers, then reopens the file and checks that every example solves.
  /// </summary>
  static class GhFile
  {
    static int _failures;

    static void Check(bool ok, string what)
    {
      Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
      if (!ok) _failures++;
    }

    public static int Run(string gha, string path)
    {
      LoadPlugin(gha);
      Directory.CreateDirectory(Path.GetDirectoryName(path));

      var doc = NewDocument();
      var b = new GhBuilder(doc, Solver, Visualize);
      b.Title(0, -150,
        "Moyu 2D 拓扑优化示例 · 2D topology optimisation example\n" +
        "把某组的 Run 开关拨到 True 开始（Timer 已接好），Reset 回到均匀密度；运行中可以拖滑块。\n" +
        "Set a group's Run toggle to True to start (the Timer is wired), Reset starts over; drag sliders while it runs.");
      Cantilever(b, 0, 0);
      Bridge(b, 0, 560);
      Bracket(b, 0, 1300);

      doc.NewSolution(false);
      var components = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in components) PrintMessages(c);
      Check(components.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"built: all {components.Count} components solve without warnings or errors");

      var io = new GH_DocumentIO(doc);
      Check(io.SaveQuiet(path), $"saved {path}");

      GhBuilder.Verify(path, Solver, Visualize, 3, 60, Check);
      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    // ---------- examples ----------

    /// <summary>Cantilever in World XY: left edge fixed, tip load whose height is a slider.</summary>
    static void Cantilever(GhBuilder b, float x, float y)
    {
      var members = new List<IGH_DocumentObject>();
      var width = b.Slider("Width", 10, 60, 200, x, y + 20, members);
      var height = b.Slider("Height", 10, 30, 100, x, y + 60, members);
      var res = b.Slider("Resolution", 20, 90, 300, x, y + 100, members, integer: true);
      var loadY = b.Slider("Load Y", 0, 15, 30, x, y + 140, members);
      var vol = b.Slider("Volume", 0.1, 0.4, 0.9, x, y + 180, members);
      var mode = b.Slider("Mode", 0, 0, 2, x, y + 220, members, integer: true);

      var rect = b.Native("Rectangle", "Curve", x + 260, y + 20, members);
      b.Wire(width, rect, 1);
      b.Wire(height, rect, 2);
      var unitY = b.Native("Unit Y", "Vector", x + 260, y + 120, members);
      var edge = b.Native("Line SDL", "Curve", x + 400, y + 110, members);
      b.Set(edge, 0, new GH_Point(Point3d.Origin));
      b.Wire(unitY, 0, edge, 1);
      b.Wire(height, edge, 2);
      var tip = b.Native("Construct Point", "Vector", x + 260, y + 190, members);
      b.Wire(width, tip, 0);
      b.Wire(loadY, tip, 1);

      var bnd = b.Moyu(Boundary, x + 560, y + 10, members);
      b.Wire(rect, 0, bnd, 0);
      b.Wire(res, bnd, 1);
      var sup = b.Moyu(Support, x + 560, y + 110, members);
      b.Wire(edge, 0, sup, 0);
      var load = b.Moyu(Load, x + 560, y + 200, members);
      b.Wire(tip, 0, load, 0);

      var solver = b.Solver(x + 780, y, vol, members);
      b.Wire(bnd, 0, solver, 0);
      b.Wire(load, 0, solver, 1);
      b.Wire(sup, 0, solver, 2);
      var vis = b.Visualize(x + 1060, y + 30, solver, mode, members);
      b.Group("A · 悬臂梁（World XY）· 左边固定，右端荷载 · 拖 Load Y 可在运行中移动荷载",
        "A · Cantilever (World XY) · fixed on the left, load at the right end · drag Load Y to move the load while it runs", Color.FromArgb(70, 120, 170, 220), members);
    }

    /// <summary>Bridge elevation in World XZ: line load on the deck, pin + roller, solid deck, void opening, steel E.</summary>
    static void Bridge(GhBuilder b, float x, float y)
    {
      var members = new List<IGH_DocumentObject>();
      var span = b.Slider("Span", 20, 100, 200, x, y + 20, members);
      var rise = b.Slider("Height", 10, 40, 80, x, y + 60, members);
      var res = b.Slider("Resolution", 20, 120, 300, x, y + 100, members, integer: true);
      var vol = b.Slider("Volume", 0.1, 0.3, 0.9, x, y + 140, members);
      var hole = b.Slider("Opening R", 1, 9, 18, x, y + 180, members);
      var mode = b.Slider("Mode", 0, 0, 2, x, y + 220, members, integer: true);

      var origin = b.Native("Construct Point", "Vector", x + 260, y + 10, members);
      b.Set(origin, 1, new GH_Number(-60));
      var plane = b.Native("XZ Plane", "Vector", x + 400, y + 10, members);
      b.Wire(origin, 0, plane, 0);
      var rect = b.Native("Rectangle", "Curve", x + 260, y + 90, members);
      b.Wire(plane, 0, rect, 0);
      b.Wire(span, rect, 1);
      b.Wire(rise, rect, 2);
      var explode = b.Native("Explode", "Curve", x + 420, y + 90, members);
      b.Wire(rect, 0, explode, 0);
      var top = b.Native("List Item", "Sets", x + 420, y + 180, members);
      b.Wire(explode, 0, top, 0);
      b.Set(top, 1, new GH_Integer(2));
      var bottom = b.Native("List Item", "Sets", x + 420, y + 270, members);
      b.Wire(explode, 0, bottom, 0);
      b.Set(bottom, 1, new GH_Integer(0));
      var ends = b.Native("End Points", "Curve", x + 560, y + 270, members);
      b.Wire(bottom, 0, ends, 0);
      var down = b.Native("Unit Z", "Vector", x + 560, y + 190, members);
      b.Set(down, 0, new GH_Number(-10));

      // Deck band: a 3-unit tall rectangle hugging the top edge.
      var deckPlane = b.Native("XZ Plane", "Vector", x + 260, y + 360, members);
      var deckOrigin = b.Native("Construct Point", "Vector", x + 120, y + 360, members);
      b.Set(deckOrigin, 1, new GH_Number(-60));
      var deckZ = b.Native("Subtraction", "Maths", x + 120, y + 300, members);
      b.Wire(rise, deckZ, 0);
      b.Set(deckZ, 1, new GH_Number(3));
      b.Wire(deckZ, 0, deckOrigin, 2);
      b.Wire(deckOrigin, 0, deckPlane, 0);
      var deck = b.Native("Rectangle", "Curve", x + 400, y + 360, members);
      b.Wire(deckPlane, 0, deck, 0);
      b.Wire(span, deck, 1);
      b.Set(deck, 2, new GH_Interval(new Interval(0, 3)));
      // Opening: circle centred low in the middle of the span.
      var mid = b.Native("Division", "Maths", x + 120, y + 470, members);
      b.Wire(span, mid, 0);
      b.Set(mid, 1, new GH_Number(2));
      var centre = b.Native("Construct Point", "Vector", x + 260, y + 470, members);
      b.Wire(mid, 0, centre, 0);
      b.Set(centre, 1, new GH_Number(-60));
      b.Set(centre, 2, new GH_Number(14));
      var holePlane = b.Native("XZ Plane", "Vector", x + 400, y + 470, members);
      b.Wire(centre, 0, holePlane, 0);
      var circle = b.Native("Circle", "Curve", x + 540, y + 470, members);
      b.Wire(holePlane, 0, circle, 0);
      b.Wire(hole, circle, 1);

      var bnd = b.Moyu(Boundary, x + 700, y + 10, members);
      b.Wire(rect, 0, bnd, 0);
      b.Wire(res, bnd, 1);
      var load = b.Moyu(Load, x + 700, y + 110, members);
      b.Wire(top, 0, load, 0);
      b.Wire(down, 0, load, 1);
      var pin = b.Moyu(Support, x + 700, y + 200, members);
      b.Wire(ends, 0, pin, 0);
      var roller = b.Moyu(Support, x + 700, y + 290, members);
      b.Wire(ends, 1, roller, 0);
      b.Set(roller, 1, new GH_Boolean(false));
      var deckArea = b.Moyu(Area, x + 700, y + 380, members);
      b.Wire(deck, 0, deckArea, 0);
      b.Set(deckArea, 1, new GH_Boolean(true));
      var opening = b.Moyu(Area, x + 700, y + 460, members);
      b.Wire(circle, 0, opening, 0);
      var steel = b.Moyu(Material, x + 700, y + 540, members);
      b.Set(steel, 0, new GH_Number(210000));

      var solver = b.Solver(x + 920, y + 60, vol, members);
      b.Wire(bnd, 0, solver, 0);
      b.Wire(load, 0, solver, 1);
      b.Wire(pin, 0, solver, 2);
      b.Wire(roller, 0, solver, 2, append: true);
      b.Wire(deckArea, 0, solver, 3);
      b.Wire(opening, 0, solver, 3, append: true);
      b.Wire(steel, 0, solver, 4);
      b.Set(solver, 7, new GH_Number(2.5));
      var vis = b.Visualize(x + 1200, y + 90, solver, mode, members);
      b.Group("B · 立面桥（World XZ，y = -60）· 顶边线荷载 + 实心桥面 + 空心洞口 + 钢材 E",
        "B · Bridge elevation (World XZ, y = -60) · line load on the top edge + solid deck + void opening + steel E", Color.FromArgb(70, 220, 170, 120), members);
    }

    /// <summary>L-shaped bracket with a bolt hole (nested boundary curve), smooth display.</summary>
    static void Bracket(GhBuilder b, float x, float y)
    {
      var members = new List<IGH_DocumentObject>();
      var res = b.Slider("Resolution", 20, 80, 300, x, y + 20, members, integer: true);
      var vol = b.Slider("Volume", 0.1, 0.35, 0.9, x, y + 60, members);
      var mode = b.Slider("Mode", 0, 2, 2, x, y + 100, members, integer: true);

      var outline = b.Native("Polyline", "Curve", x + 260, y + 10, members);
      b.Set(outline, 0, new[] { (120, 0), (160, 0), (160, 40), (220, 40), (220, 100), (120, 100) }
        .Select(p => (IGH_Goo)new GH_Point(new Point3d(p.Item1, p.Item2, 0))).ToArray());
      b.Set(outline, 1, new GH_Boolean(true));
      var bolt = b.Native("Circle CNR", "Curve", x + 260, y + 100, members);
      b.Set(bolt, 0, new GH_Point(new Point3d(140, 75, 0)));
      b.Set(bolt, 2, new GH_Number(6));
      var edge = b.Native("Line", "Curve", x + 260, y + 190, members);
      b.Set(edge, 0, new GH_Point(new Point3d(120, 100, 0)));
      b.Set(edge, 1, new GH_Point(new Point3d(160, 100, 0)));

      var bnd = b.Moyu(Boundary, x + 460, y + 10, members);
      b.Wire(outline, 0, bnd, 0);
      b.Wire(bolt, 0, bnd, 0, append: true);
      b.Wire(res, bnd, 1);
      var sup = b.Moyu(Support, x + 460, y + 110, members);
      b.Wire(edge, 0, sup, 0);
      var load = b.Moyu(Load, x + 460, y + 200, members);
      b.Set(load, 0, new GH_Point(new Point3d(220, 45, 0)));
      b.Set(load, 1, new GH_Vector(new Vector3d(0, -5, 0)));

      var solver = b.Solver(x + 680, y, vol, members);
      b.Wire(bnd, 0, solver, 0);
      b.Wire(load, 0, solver, 1);
      b.Wire(sup, 0, solver, 2);
      var vis = b.Visualize(x + 960, y + 30, solver, mode, members);
      b.Group("C · L 形支架 + 螺栓孔（World XY，x = 120…220）· 嵌套曲线 = 孔，平滑显示",
        "C · L-shaped bracket with a bolt hole (World XY, x = 120…220) · a nested curve is a hole, smooth display", Color.FromArgb(70, 200, 140, 200), members);
    }
  }
}
