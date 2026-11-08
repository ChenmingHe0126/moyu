using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>
  /// Writes tests/gh/MoyuSwift-Test.gh for the Swift flyer: a study ready to fly with the defaults, and one driven by
  /// Storey Grid (exposure, mode, opening, bay, seed sliders); then reopens it and checks it.
  /// </summary>
  static class GhFileSwift
  {
    public static readonly Guid Study = new Guid("2a073a5f-8b4d-42aa-9b77-59a090251c1f");
    public static readonly Guid Grid = new Guid("14ceb1e1-aa5e-4512-8e8c-1fb4be8a1459");

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
        "Swift 雨燕 · 扇翅穿墙（看起来是剖面采光通风分析）· Flappy flight, disguised as a section daylight study\n" +
        "点 Airflow Study 下面的 ▶ Run · Click ▶ Run under Airflow Study.\n" +
        "空格 / ↑ / W / 鼠标左键 扇翅，穿过墙上的开口 · Space, ↑, W or left click flaps through the openings\n" +
        "Esc 暂停 / 继续，Shift+Esc 退出 · Home 复位视角 · Esc pause / resume, Shift+Esc quit, Home reset view\n" +
        "1 左边的键（美式键盘 `）老板键：切成研究图，再按一次回来 · Key left of 1 (` on US keyboards): boss key\n" +
        "右键游戏组件 › Language · 语言：Auto 跟随 Windows / 中文 / English · Right-click the study › Language: Auto (follows Windows), 中文 or English");
      QuickStart(b, 0, 0);
      Settings(b, 0, 300);

      doc.NewSolution(false);
      Inspect(doc, "built");
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
      panel.Attributes.Bounds = new RectangleF(x, y, 250, 180);
      panel.AddSource(study.Params.Output[0]);
      panel.AddSource(study.Params.Output[5]);
    }

    static void QuickStart(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var study = b.Moyu(Study, x + 230, y + 20, m);
      Metrics(b, study, x + 490, y + 20, m);
      b.Group("A · 快速开始：直接点 ▶ Run（普通风压，经典模式）",
        "A · Quick start: just click ▶ Run (normal exposure, Classic mode)", Color.FromArgb(70, 229, 107, 93), m);
    }

    /// <summary>B: Storey Grid with sliders for exposure, mode, opening, bay and seed.</summary>
    static void Settings(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var exposure = b.Slider("Exposure", 0, 1, 2, x, y + 30, m, integer: true);
      var mode = b.Slider("Mode", 0, 0, 1, x, y + 75, m, integer: true);
      var opening = b.Slider("Opening", 0.8, 1, 1.4, x, y + 120, m);
      var bay = b.Slider("Bay", 0.8, 1, 1.5, x, y + 165, m);
      var seed = b.Slider("Seed", 0, 0, 999, x, y + 210, m, integer: true);
      var grid = b.Moyu(Grid, x + 260, y + 60, m);
      grid.Hidden = true;
      b.Wire(exposure, grid, 0);
      b.Wire(mode, grid, 1);
      b.Wire(opening, grid, 2);
      b.Wire(bay, grid, 3);
      b.Wire(seed, grid, 4);
      var study = b.Moyu(Study, x + 490, y + 60, m);
      b.Wire(grid, 0, study, 0);
      Metrics(b, study, x + 750, y + 60, m);
      b.Group("B · 调参数：Exposure 0 避风 / 1 普通 / 2 迎风（越大越快越窄），Mode 1 = Zen，Opening 开口高度系数，Bay 开间系数，Seed 固定关卡（0 = 每次随机）",
        "B · Settings: Exposure 0 sheltered / 1 normal / 2 exposed (higher is faster and tighter), Mode 1 = Zen, Opening = opening height factor, Bay = bay width factor, Seed fixes the course (0 = new each time)", Color.FromArgb(70, 140, 132, 200), m);
    }

    static void Inspect(GH_Document doc, string when)
    {
      var components = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in components) PrintMessages(c);
      Check(components.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"{when}: all {components.Count} components solve without warnings or errors");
      var studies = components.Where(c => c.ComponentGuid == Study).OrderBy(c => c.Attributes.Pivot.Y).ToList();
      Check(studies.Count == 2 && studies.All(s => Data(s, 0).First().ToString() == "Ready"), $"{when}: both Airflow Studies wait at Ready ({string.Join(" | ", studies.Select(s => s.Message))})");
      Check(studies.Count == 2 && studies.All(s => s.Message == "Normal"), $"{when}: the defaults and Storey Grid both give normal exposure");
      var grid = components.Single(c => c.ComponentGuid == Grid);
      Check(grid.Hidden && grid.Message == "Normal" && Data(grid, 1).Count() >= 8, $"{when}: Storey Grid previews floor, ceiling and the first walls (preview off, {Data(grid, 1).Count()} curves)");
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
