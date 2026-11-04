using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Writes tests/gh/MoyuBreakout-Test.gh for the Rebound game: a ready-to-play Facade Study, Brick Bond's bonds, and
  /// a custom wall from closed curves; then reopens it and checks it.
  /// </summary>
  static class GhFileBreakout
  {
    public static readonly Guid Study = new Guid("b232de6c-3642-427f-90d5-1e90050e09cc");
    public static readonly Guid Bond = new Guid("20176648-5aa5-4679-959c-cb355cd79773");

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
      var b = new GhBuilder(doc, Guid.Empty, Guid.Empty);
      b.Title(0, -250,
        "Rebound 回弹 · 打砖块（看起来是立面研究）· Brick breaker, disguised as a facade study\n" +
        "点 Facade Study 下面的 ▶ Run · Click ▶ Run under Facade Study.\n" +
        "← → / A D 或移动鼠标 控制挡板 · 空格 发球 · Paddle with arrows, A D or the mouse, Space serves\n" +
        "Esc 暂停 / 继续，Shift+Esc 退出 · Home 复位视角 · Esc pause / resume, Shift+Esc quit, Home reset view\n" +
        "1 左边的键（美式键盘 `）老板键：切成研究图，再按一次回来 · Key left of 1 (` on US keyboards): boss key");
      QuickStart(b, 0, 0);
      Bonds(b, 0, 300);
      Custom(b, 0, 640);

      doc.NewSolution(false);
      Inspect(doc, "built");
      EveryBond(doc);
      var io = new GH_DocumentIO(doc);
      Check(io.SaveQuiet(path), $"saved {path}");
      Console.WriteLine($"Reopening {path}");
      var reopen = new GH_DocumentIO();
      Check(reopen.Open(path), "file opens");
      reopen.Document.Enabled = true;
      reopen.Document.NewSolution(false);
      Inspect(reopen.Document, "reopened");
      Overlaps(reopen.Document);
      Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILURE(S)");
      return _failures == 0 ? 0 : 1;
    }

    static void Metrics(GhBuilder b, GH_Component study, float x, float y, List<IGH_DocumentObject> m)
    {
      var panel = b.Object(new GH_Panel { NickName = "Metrics" }, x, y, m);
      panel.Attributes.Bounds = new RectangleF(x, y, 230, 170);
      panel.AddSource(study.Params.Output[0]);
      panel.AddSource(study.Params.Output[6]);
    }

    static void QuickStart(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var study = b.Moyu(Study, x, y + 20, m);
      Metrics(b, study, x + 260, y + 20, m);
      b.Group("A · 快速开始：直接点 ▶ Run（六种砌法逐关轮换、经典模式、3 条命）", Color.FromArgb(70, 227, 111, 90), m);
    }

    /// <summary>B: one bond for every level. Brick Bond's own preview is off so the game draws over a clean viewport.</summary>
    static void Bonds(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var type = b.Slider("Type", -1, 2, 5, x, y + 30, m, integer: true);
      var courses = b.Slider("Courses", 6, 10, 14, x, y + 70, m, integer: true);
      var bond = b.Moyu(Bond, x + 230, y + 30, m);
      bond.Hidden = true;
      b.Wire(type, bond, 0);
      b.Wire(courses, bond, 2);
      var mode = b.Slider("Mode", 0, 0, 2, x + 230, y + 170, m, integer: true);
      var level = b.Slider("Level", 1, 1, 15, x + 230, y + 210, m, integer: true);
      var study = b.Moyu(Study, x + 480, y + 60, m);
      b.Wire(bond, 0, study, 0);
      b.Wire(mode, study, 1);
      b.Wire(level, study, 2);
      Metrics(b, study, x + 740, y + 60, m);
      b.Group("B · 换砌法：Type −1 轮换 · 0 顺砌 · 1 通缝 · 2 英式 · 3 荷兰式 · 4 花墙 · 5 拱。Mode 1 = 3 分钟闪电，2 = 禅（不掉命）", Color.FromArgb(70, 240, 138, 98), m);
    }

    /// <summary>C: a custom wall drawn as closed curves (a little composition of panels and a band of small tiles).</summary>
    static void Custom(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var curves = b.Object(new Param_Curve { NickName = "Panels" }, x, y + 40, m);
      var rng = new Random(2);
      for (int row = 0; row < 6; row++)
      {
        double z = 40 - row * 4.2;
        double xx = 0;
        while (xx < 60)
        {
          double w = row == 2 ? 3 : 6 + rng.Next(3) * 3;
          var rect = new Rectangle3d(new Plane(new Point3d(xx, 0, z), Vector3d.XAxis, Vector3d.ZAxis), Math.Min(w, 60 - xx) - 0.4, 3.6);
          curves.PersistentData.Append(new GH_Curve(rect.ToNurbsCurve()));
          xx += w;
        }
      }
      var bond = b.Moyu(Bond, x + 230, y + 30, m);
      bond.Hidden = true;
      b.Wire(curves, bond, 1);
      var study = b.Moyu(Study, x + 480, y + 30, m);
      b.Wire(bond, 0, study, 0);
      Metrics(b, study, x + 740, y + 30, m);
      b.Group("C · 自定义墙：Panels 里是一组闭合矩形（按大小分成玻璃 / 砖 / 混凝土），可以换成你在 Rhino 里画的立面分格", Color.FromArgb(70, 150, 170, 220), m);
    }

    static List<GH_Component> Studies(GH_Document doc) => doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == Study).OrderBy(c => c.Attributes.Pivot.Y).ToList();

    static void Inspect(GH_Document doc, string when)
    {
      var components = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in components) PrintMessages(c);
      Check(components.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"{when}: all {components.Count} components solve without warnings or errors");
      var studies = Studies(doc);
      Check(studies.Count == 3 && studies.All(s => Data(s, 0).First().ToString() == "Ready"), $"{when}: all three Facade Studies wait at Ready ({string.Join(" | ", studies.Select(s => s.Message))})");
      var bonds = components.Where(c => c.ComponentGuid == Bond).OrderBy(c => c.Attributes.Pivot.Y).ToList();
      Check(bonds.Count == 2 && bonds.All(c => c.Hidden), $"{when}: Brick Bond previews are off");
      int drawn = doc.Objects.OfType<Param_Curve>().Single(p => p.NickName == "Panels").VolatileData.DataCount;
      Check(bonds.Count == 2 && bonds[1].Message.StartsWith("Custom") && Data(bonds[1], 1).Count() == drawn,
        $"{when}: {drawn} closed curves make a custom wall of as many panels ({bonds[1].Message})");
    }

    /// <summary>Every bond type lays a wall Facade Study accepts.</summary>
    static void EveryBond(GH_Document doc)
    {
      var type = doc.Objects.OfType<GH_NumberSlider>().Single(s => s.NickName == "Type");
      var bond = doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == Bond).OrderBy(c => c.Attributes.Pivot.Y).First();
      var study = Studies(doc)[1];
      for (int t = -1; t <= 5; t++)
      {
        type.SetSliderValue(t);
        doc.NewSolution(false);
        int bricks = Data(bond, 1).Count();
        Check(bond.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank && study.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank && bricks > 20,
          $"Brick Bond type {t}: {bond.Message}, {bricks} bricks, Facade Study {study.Message}");
      }
      type.SetSliderValue(2);
      doc.NewSolution(false);
    }

    static void Overlaps(GH_Document doc)
    {
      var boxes = doc.Objects.Where(o => !(o is GH_Group)).Select(o => (o, r: o.Attributes.Bounds)).ToList();
      var clashes = new List<string>();
      for (int i = 0; i < boxes.Count; i++)
        for (int j = i + 1; j < boxes.Count; j++)
          if (boxes[i].r.IntersectsWith(boxes[j].r)) clashes.Add($"{boxes[i].o.NickName} x {boxes[j].o.NickName}");
      var groups = doc.Objects.OfType<GH_Group>().Select(g => g.Attributes.Bounds).ToList();
      for (int i = 0; i < groups.Count; i++)
        for (int j = i + 1; j < groups.Count; j++)
          if (groups[i].IntersectsWith(groups[j])) clashes.Add($"group {i} x group {j}");
      Check(clashes.Count == 0, "canvas layout has no overlaps" + (clashes.Count > 0 ? ": " + string.Join("; ", clashes) : ""));
    }
  }
}
