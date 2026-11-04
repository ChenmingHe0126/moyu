using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Writes tests/gh/MoyuSnake-Test.gh for the Serpentine game: a ready-to-play Path Study on the built-in terrain,
  /// Site Surface's five shapes, and a native Grasshopper sphere; then reopens it and checks it.
  /// </summary>
  static class GhFileSnake
  {
    public static readonly Guid Study = new Guid("6a7a47ea-88fd-4baf-9269-f62e90906e5f");
    public static readonly Guid Site = new Guid("2f7d0cbc-3355-4c84-acf5-930e93c82e1a");

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
        "Serpentine 蜿蜒 · 曲面贪吃蛇（看起来是景观流线研究）· Snake on a surface, disguised as a circulation study\n" +
        "点 Path Study 下面的 ▶ Run · Click ▶ Run under Path Study.\n" +
        "← / A 左转 · → / D 右转（以蛇头为准）· 按住 ↑ / Shift 冲刺 · C 切换镜头 · Turn left / right, hold ↑ to sprint, C camera\n" +
        "Esc 暂停 / 继续，Shift+Esc 退出 · Home 复位视角 · Esc pause / resume, Shift+Esc quit, Home reset view\n" +
        "1 左边的键（美式键盘 `）老板键：切成研究图，再按一次回来 · Key left of 1 (` on US keyboards): boss key");
      QuickStart(b, 0, 0);
      Shapes(b, 0, 300);
      Sphere(b, 0, 720);

      doc.NewSolution(false);
      Inspect(doc, "built");
      EveryShape(doc);
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

    /// <summary>A: nothing to wire. A Path Study on its built-in terrain, with a metrics panel.</summary>
    static void QuickStart(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var study = b.Moyu(Study, x, y + 20, m);
      Metrics(b, study, x + 260, y + 20, m);
      b.Group("A · 快速开始：直接点 ▶ Run（内置起伏地形、28 格、经典模式）", Color.FromArgb(70, 190, 236, 90), m);
    }

    /// <summary>B: Site Surface's shapes. Its own preview is off so the game draws over a clean viewport.</summary>
    static void Shapes(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var type = b.Slider("Type", 0, 1, 4, x, y + 30, m, integer: true);
      var seed = b.Slider("Seed", 1, 1, 20, x, y + 70, m, integer: true);
      var site = b.Moyu(Site, x + 230, y + 30, m);
      site.Hidden = true;
      b.Wire(type, site, 0);
      b.Wire(seed, site, 2);
      var cells = b.Slider("Cells", 8, 28, 60, x + 230, y + 160, m, integer: true);
      var mode = b.Slider("Mode", 0, 0, 2, x + 230, y + 200, m, integer: true);
      var level = b.Slider("Level", 1, 1, 15, x + 230, y + 240, m, integer: true);
      var study = b.Moyu(Study, x + 480, y + 60, m);
      b.Wire(site, 0, study, 0);
      b.Wire(cells, study, 1);
      b.Wire(mode, study, 2);
      b.Wire(level, study, 3);
      Metrics(b, study, x + 740, y + 60, m);
      b.Group("B · 换场地：Type 0 起伏地形 · 1 环面（上下左右都能穿）· 2 圆管（绕一圈）· 3 马鞍屋面 · 4 球面。Mode 1 = 3 分钟闪电，2 = 禅（不会死）", Color.FromArgb(70, 90, 200, 210), m);
    }

    /// <summary>C: any surface works; here Grasshopper's own sphere.</summary>
    static void Sphere(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var radius = b.Slider("Radius", 4, 12, 30, x, y + 40, m);
      var sphere = b.Native("Sphere", "Surface", x + 230, y + 30, m);
      b.Wire(radius, sphere, 1);
      var study = b.Moyu(Study, x + 480, y + 30, m);
      b.Wire(sphere, 0, study, 0);
      Metrics(b, study, x + 740, y + 30, m);
      b.Group("C · 你自己的曲面：接任意单个曲面（这里是 GH 原生 Sphere）。闭合方向会绕过去，球的两极当墙", Color.FromArgb(70, 200, 150, 255), m);
    }

    static void Metrics(GhBuilder b, GH_Component study, float x, float y, List<IGH_DocumentObject> m)
    {
      var panel = b.Object(new GH_Panel { NickName = "Metrics" }, x, y, m);
      panel.Attributes.Bounds = new RectangleF(x, y, 230, 170);
      panel.AddSource(study.Params.Output[0]);
      panel.AddSource(study.Params.Output[5]);
    }

    static List<GH_Component> Studies(GH_Document doc) =>
      doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == Study).OrderBy(c => c.Attributes.Pivot.Y).ToList();

    static void Inspect(GH_Document doc, string when)
    {
      var components = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in components) PrintMessages(c);
      Check(components.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"{when}: all {components.Count} components solve without warnings or errors");
      var studies = Studies(doc);
      Check(studies.Count == 3 && studies.All(s => Data(s, 0).First().ToString() == "Ready"), $"{when}: all three Path Studies wait at Ready");
      Console.WriteLine($"  info: grids {string.Join(" | ", studies.Select(s => s.Message))}");
      Check(studies.Count == 3 && studies[0].Message.StartsWith("28 × ") && !studies[0].Message.Contains("wraps"), $"{when}: default terrain is an open 28-cell grid ({studies[0].Message})");
      Check(studies.Count == 3 && studies[2].Message.Contains("wraps"), $"{when}: the sphere wraps round ({studies[2].Message})");
      var site = components.Single(c => c.ComponentGuid == Site);
      Check(Data(site, 0).Count() == 1 && site.Hidden, $"{when}: Site Surface gives a surface, its preview off");
    }

    /// <summary>Every Site Surface type gives a playable grid with the expected wrapping.</summary>
    static void EveryShape(GH_Document doc)
    {
      var type = doc.Objects.OfType<GH_NumberSlider>().Single(s => s.NickName == "Type");
      var study = Studies(doc)[1];
      var expect = new[] { "", "wraps both ways", "wraps", "", "wraps" };
      for (int t = 0; t <= 4; t++)
      {
        type.SetSliderValue(t);
        doc.NewSolution(false);
        string msg = study.Message ?? "";
        bool ok = study.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank && (expect[t] == "" ? !msg.Contains("wraps") : msg.Contains(expect[t]));
        Check(ok, $"Site Surface type {t}: Path Study grid {msg}");
      }
      type.SetSliderValue(1);
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
