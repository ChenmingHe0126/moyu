using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using static Moyu.RhinoTests.Canvas;

namespace Moyu.RhinoTests
{
  /// <summary>Places sliders, native and Moyu components on a document with their top-left corner at (x, y).</summary>
  sealed class GhBuilder
  {
    readonly GH_Document _doc;
    readonly Guid _solver, _visualize;

    public GhBuilder(GH_Document doc, Guid solver, Guid visualize)
    {
      _doc = doc;
      _solver = solver;
      _visualize = visualize;
    }

    T Place<T>(T obj, float x, float y, List<IGH_DocumentObject> members) where T : IGH_DocumentObject
    {
      obj.CreateAttributes();
      obj.Attributes.Pivot = new PointF(x, y);
      _doc.AddObject(obj, false);
      obj.Attributes.ExpireLayout();
      obj.Attributes.PerformLayout();
      // Pivots differ per object type (components centre vertically on theirs); align the box's top-left instead.
      var box = obj.Attributes.Bounds;
      obj.Attributes.Pivot = new PointF(obj.Attributes.Pivot.X + x - box.X, obj.Attributes.Pivot.Y + y - box.Y);
      obj.Attributes.ExpireLayout();
      obj.Attributes.PerformLayout();
      members?.Add(obj);
      return obj;
    }

    /// <summary>Any canvas object (parameter, panel …) with its top-left corner at (x, y).</summary>
    public T Object<T>(T obj, float x, float y, List<IGH_DocumentObject> members) where T : IGH_DocumentObject => Place(obj, x, y, members);

    public GH_NumberSlider Slider(string name, double min, double value, double max, float x, float y, List<IGH_DocumentObject> members, bool integer = false)
    {
      var s = new GH_NumberSlider { NickName = name };
      s.Slider.Type = integer ? GH_SliderAccuracy.Integer : GH_SliderAccuracy.Float;
      s.Slider.DecimalPlaces = integer ? 0 : 2;
      s.Slider.Minimum = (decimal)min;
      s.Slider.Maximum = (decimal)max;
      s.SetSliderValue((decimal)value);
      return Place(s, x, y, members);
    }

    public GH_Component Native(string name, string category, float x, float y, List<IGH_DocumentObject> members)
    {
      var hits = Instances.ComponentServer.ObjectProxies
        .Where(p => string.Equals(p.Desc.Name, name, StringComparison.OrdinalIgnoreCase) && p.Desc.Category == category &&
          !p.Obsolete && p.Kind == GH_ObjectType.CompiledObject)
        .ToList();
      if (hits.Count != 1)
        throw new InvalidOperationException($"'{name}' in {category}: {hits.Count} matches " +
          string.Join(", ", Instances.ComponentServer.ObjectProxies.Where(p => p.Desc.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(p => $"{p.Desc.Name}/{p.Desc.Category}/{p.Desc.SubCategory}")));
      var c = (GH_Component)hits[0].CreateInstance();
      c.Hidden = true; // keep construction geometry out of the viewport
      return Place(c, x, y, members);
    }

    public GH_Component Moyu(Guid id, float x, float y, List<IGH_DocumentObject> members)
    {
      var c = (GH_Component)Instances.ComponentServer.EmitObject(id);
      return Place(c, x, y, members);
    }

    /// <summary>Solver with a Run toggle and Reset button underneath and a Timer above that targets it.</summary>
    public GH_Component Solver(float x, float y, GH_NumberSlider volume, List<IGH_DocumentObject> members)
    {
      var s = Moyu(_solver, x, y, members);
      Wire(volume, s, 5);
      float below = s.Attributes.Bounds.Bottom + 16;
      var run = Place(new GH_BooleanToggle { NickName = "Run", Value = false }, x, below, members);
      Wire(run, s, 10);
      var reset = Place(new GH_ButtonObject { NickName = "Reset" }, x, below + 36, members);
      Wire(reset, s, 11);
      var timer = Place(new GH_Timer { NickName = "Trigger", Interval = 50 }, x + 10, y - 56, members);
      // GH_Timer.Targets hands out a copy; the canvas drag-to-connect writes the private list directly.
      var targets = (List<Guid>)typeof(GH_Timer).GetField("m_targets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(timer);
      targets.Add(s.InstanceGuid);
      return s;
    }

    /// <summary>Visualizer plus a status panel showing iteration, compliance, volume and convergence.</summary>
    public GH_Component Visualize(float x, float y, GH_Component solver, GH_NumberSlider mode, List<IGH_DocumentObject> members)
    {
      var v = Moyu(_visualize, x, y, members);
      Wire(solver, 0, v, 0);
      Wire(mode, v, 1);
      float below = v.Attributes.Bounds.Bottom + 16;
      var panel = Place(new GH_Panel { NickName = "Status" }, x, below, members);
      panel.Attributes.Bounds = new RectangleF(x, below, 200, 130);
      foreach (int o in new[] { 1, 2, 3, 4 }) panel.AddSource(solver.Params.Output[o]);
      return v;
    }

    public void Title(float x, float y, string text)
    {
      var s = new GH_Scribble { Text = text, Font = new Font("Microsoft YaHei", 14f) };
      Place(s, x, y, null);
    }

    /// <summary>A coloured group around <paramref name="members"/>, labelled in Chinese and English (one line each).</summary>
    public void Group(string chinese, string english, Color colour, List<IGH_DocumentObject> members)
    {
      var g = new GH_Group { NickName = chinese + "\n" + english, Colour = colour };
      g.CreateAttributes();
      foreach (var m in members) g.AddObject(m.InstanceGuid);
      _doc.AddObject(g, false, 0);
      g.ExpireCaches();
    }

    public void Set(GH_Component c, int input, params IGH_Goo[] values) => Canvas.Set(c, input, values);

    public void Wire(GH_Component src, int output, GH_Component dst, int input, bool append = false) =>
      Canvas.Wire(src, output, dst, input, append);

    public void Wire(IGH_Param src, GH_Component dst, int input) => dst.Params.Input[input].AddSource(src);

    /// <summary>
    /// Reopens a saved test file like Grasshopper would: checks wiring, that it opens idle and warning-free,
    /// that nothing overlaps on the canvas, then switches every Run toggle on and ticks until all solvers converge.
    /// </summary>
    public static void Verify(string path, Guid solverId, Guid visualizeId, int examples, int maxTicks, Action<bool, string> check)
    {
      Console.WriteLine($"Reopening {path}");
      var io = new GH_DocumentIO();
      check(io.Open(path), "file opens");
      var doc = io.Document;
      doc.Enabled = true;
      var solvers = doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == solverId).OrderBy(c => c.Attributes.Pivot.Y).ToList();
      var timers = doc.Objects.OfType<GH_Timer>().ToList();
      var toggles = doc.Objects.OfType<GH_BooleanToggle>().ToList();
      check(solvers.Count == examples && doc.Objects.OfType<GH_Component>().Count(c => c.ComponentGuid == visualizeId) == examples,
        $"{examples} solvers and {examples} visualizers");
      check(timers.Count == examples && solvers.All(s => timers.Any(t => t.Targets.Contains(s.InstanceGuid))), "every solver has a Timer targeting it");
      check(toggles.All(t => !t.Value), "Run toggles are saved off");

      doc.NewSolution(false);
      var opened = doc.Objects.OfType<GH_Component>().ToList();
      foreach (var c in opened) PrintMessages(c);
      check(opened.All(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank || c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Remark),
        $"reopened: all {opened.Count} components solve without warnings or errors");
      check(solvers.All(s => Data(s, 1).Any() && Int(s, 1) == 0 && s.Message == "Ready"), "opened file waits at iteration 0");

      var boxes = doc.Objects.Where(o => !(o is GH_Group)).Select(o => (o, r: o.Attributes.Bounds)).ToList();
      var clashes = new List<string>();
      for (int i = 0; i < boxes.Count; i++)
        for (int j = i + 1; j < boxes.Count; j++)
          if (boxes[i].r.IntersectsWith(boxes[j].r)) clashes.Add($"{boxes[i].o.NickName} {Box(boxes[i].r)} x {boxes[j].o.NickName} {Box(boxes[j].r)}");
      var groups = doc.Objects.OfType<GH_Group>().Select(g => (g, r: g.Attributes.Bounds)).ToList();
      for (int i = 0; i < groups.Count; i++)
        for (int j = i + 1; j < groups.Count; j++)
          if (groups[i].r.IntersectsWith(groups[j].r)) clashes.Add($"group {i} {Box(groups[i].r)} x group {j} {Box(groups[j].r)}");
      check(clashes.Count == 0, "canvas layout has no overlaps" + (clashes.Count > 0 ? ": " + string.Join("; ", clashes) : ""));

      foreach (var t in toggles) { t.Value = true; t.ExpireSolution(false); }
      doc.NewSolution(false);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      for (int k = 0; k < maxTicks && solvers.Any(s => !Bool(s, 4)); k++)
        foreach (var s in solvers.Where(s => !Bool(s, 4))) Tick(doc, s);
      foreach (var s in solvers)
      {
        PrintMessages(s);
        check(Bool(s, 4) && !HasMessage(s, GH_RuntimeMessageLevel.Error), $"{s.Message}, compliance {Num(s, 2):G5}, volume {Num(s, 3):F3}");
      }
      Console.WriteLine($"  all {examples} converged in {sw.ElapsedMilliseconds} ms of simulated Timer ticks");
    }

    static string Box(RectangleF r) => $"[{r.Left:F0},{r.Top:F0} → {r.Right:F0},{r.Bottom:F0}]";
  }
}
