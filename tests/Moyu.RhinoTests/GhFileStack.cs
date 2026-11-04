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
  /// Writes tests/gh/MoyuStack-Test.gh for the VoxStack game: a ready-to-play Massing Study, and a custom-unit example
  /// (sketched boxes and a text pattern → Voxel Unit → Unit Library → Massing Study); then reopens it and checks it.
  /// </summary>
  static class GhFileStack
  {
    public static readonly Guid Study = new Guid("6f1d7a52-3c84-4e0b-9a51-2d7c9e4b8f10");
    public static readonly Guid Unit = new Guid("b8e2c4d1-7a93-4f56-8c0e-3e5a1f6d2b47");
    public static readonly Guid Library = new Guid("4c9a1e73-58d2-4b0f-a6e1-91f3d0c7b528");

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
        "VoxStack 叠楼 · 3D 落块（看起来是体块研究）· 3D falling blocks, disguised as a massing study\n" +
        "点 Massing Study 下面的 ▶ Run，在当前 Rhino 视口里玩 · Click ▶ Run under Massing Study to play in the active viewport.\n" +
        "← ↑ ↓ → 移动 · 拖右下角 gumball 圆环或 Q E / W S / A D 旋转 · 空格 落下 · C 暂存 · Move, rotate (gumball or keys), Space drop, C hold\n" +
        "Esc 暂停 / 继续，Shift+Esc 退出 · Home 复位视角 · Esc pause / resume, Shift+Esc quit, Home reset view\n" +
        "1 左边的键（美式键盘 `）老板键：切成研究图，再按一次回来 · Key left of 1 (` on US keyboards): boss key");
      QuickStart(b, 0, 0);
      Custom(b, 0, 330);

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

    /// <summary>A: nothing to wire. One Massing Study with a metrics panel (no score on the canvas, for the boss's sake).</summary>
    static void QuickStart(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      var study = b.Moyu(Study, x, y + 20, m);
      var panel = b.Object(new GH_Panel { NickName = "Metrics" }, x + 260, y + 20, m);
      panel.Attributes.Bounds = new RectangleF(x + 260, y + 20, 230, 170);
      panel.AddSource(study.Params.Output[0]);
      panel.AddSource(study.Params.Output[6]);
      b.Group("A · 快速开始：直接点 ▶ Run（默认 5×5×12 井、标准 8 块、马拉松模式）", Color.FromArgb(70, 255, 140, 90), m);
    }

    /// <summary>B: two custom units (sketched boxes, a text pattern) plus the standard set, played in Blitz mode.</summary>
    static void Custom(GhBuilder b, float x, float y)
    {
      var m = new List<IGH_DocumentObject>();
      // Three boxes drawn roughly, off any grid: a bar, a two-storey corner and a small wing (the "Pavilion").
      const double s = 3.6, ox = -6 * s;
      var boxes = new[]
      {
        new BoundingBox(ox + 0.1 * s, 0.05 * s, 0, ox + 2.9 * s, 0.95 * s, 0.9 * s),
        new BoundingBox(ox, 0.9 * s, 0, ox + 0.95 * s, 2.05 * s, 1.95 * s),
        new BoundingBox(ox + 2.05 * s, 1.0 * s, 0, ox + 2.95 * s, 1.9 * s, 1.0 * s),
      };
      var sketch = b.Object(new Param_Brep { NickName = "Sketch" }, x, y + 30, m);
      foreach (var box in boxes) sketch.PersistentData.Append(new GH_Brep(box.ToBrep()));
      var pattern = b.Object(new GH_Panel { NickName = "Pattern", UserText = "#.#/#.#|###" }, x, y + 200, m);
      pattern.Attributes.Bounds = new RectangleF(x, y + 200, 150, 24);

      var unitA = b.Moyu(Unit, x + 210, y, m);
      b.Wire(sketch, unitA, 0);
      b.Set(unitA, 4, new GH_String("Pavilion 小亭"));
      var unitB = b.Moyu(Unit, x + 210, y + 175, m);
      b.Wire(pattern, unitB, 2);
      b.Set(unitB, 4, new GH_String("Gate 门楼"));
      b.Set(unitB, 5, new GH_Colour(Color.FromArgb(76, 201, 240)));

      var preset = b.Slider("Preset", -1, 1, 2, x + 210, y + 360, m, integer: true);
      var lib = b.Moyu(Library, x + 470, y + 120, m);
      b.Wire(unitA, 0, lib, 0);
      b.Wire(unitB, 0, lib, 0, append: true);
      b.Wire(preset, lib, 1);

      var mode = b.Slider("Mode", 0, 1, 2, x + 470, y + 230, m, integer: true);
      var width = b.Slider("Width", 4, 5, 10, x + 470, y + 270, m, integer: true);
      var depth = b.Slider("Depth", 4, 5, 10, x + 470, y + 310, m, integer: true);
      var height = b.Slider("Height", 8, 12, 20, x + 470, y + 350, m, integer: true);
      var study = b.Moyu(Study, x + 730, y + 110, m);
      b.Wire(lib, 0, study, 0);
      b.Wire(width, study, 1);
      b.Wire(depth, study, 2);
      b.Wire(height, study, 3);
      b.Wire(mode, study, 6);
      var panel = b.Object(new GH_Panel { NickName = "Metrics" }, x + 990, y + 110, m);
      panel.Attributes.Bounds = new RectangleF(x + 990, y + 110, 230, 170);
      panel.AddSource(study.Params.Output[0]);
      panel.AddSource(study.Params.Output[6]);
      b.Group("B · 自定义积木：Sketch 里 3 个随手画的盒子 + Pattern 文本 → Voxel Unit → Unit Library（Preset：-1 只用自定义，0 入门，1 标准，2 困难）。Mode 1 = 3 分钟闪电", Color.FromArgb(70, 120, 170, 220), m);
    }

    static void Inspect(GH_Document doc, string when)
    {
      var components = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in components) PrintMessages(c);
      Check(components.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"{when}: all {components.Count} components solve without warnings or errors");
      var units = components.Where(c => c.ComponentGuid == Unit).OrderBy(c => c.Attributes.Pivot.Y).ToList();
      Check(units.Count == 2 && Int(units[0], 2) == 6 && Int(units[0], 3) == 24, $"{when}: sketched boxes → 6-voxel Pavilion with 24 orientations");
      Check(units.Count == 2 && Int(units[1], 2) == 7, $"{when}: pattern #.#/#.#|### → 7-voxel Gate");
      var lib = components.Single(c => c.ComponentGuid == Library);
      Check(Data(lib, 1).Count() == 10, $"{when}: library = 8 standard + 2 custom units");
      var studies = components.Where(c => c.ComponentGuid == Study).ToList();
      Check(studies.Count == 2 && studies.All(s => Data(s, 0).First().ToString() == "Ready" && s.Message == "Ready"), $"{when}: both Massing Studies wait at Ready");
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
