using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Moyu.Components.Examples
{
  /// <summary>
  /// A launcher for the example definitions: one button per example, each opening it in a new Grasshopper tab, so
  /// nobody has to find the package's examples folder.
  /// </summary>
  public class ExamplesComponent : MoyuComponent
  {
    public ExamplesComponent()
      : base("Examples", "Examples",
        "Opens a Moyu example in a new Grasshopper tab: click one of the buttons on the component. " +
        "点组件上的按钮，在新标签页里打开对应的示例。",
        Panels.Examples)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override bool IsPreviewCapable => false;

    /// <summary>Found by typing any of these in the canvas search.</summary>
    public override IEnumerable<string> Keywords => new[] { "example", "examples", "demo", "tutorial", "sample", "示例", "案例", "例子", "教程" };

    public override void CreateAttributes() => m_attributes = new ExampleButtonsAttributes(this);

    protected override void RegisterInputParams(GH_InputParamManager pManager) { }
    protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }
    protected override void SolveInstance(IGH_DataAccess DA) { }

    /// <summary>Opens one example; a failure shows on the component and in the Rhino command line.</summary>
    public void Open(Example example)
    {
      ClearRuntimeMessages();
      if (!ExampleFiles.Open(example, out string error))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{example.File}.gh did not open: {error}");
        Rhino.RhinoApp.WriteLine($"Moyu Examples: {example.File}.gh did not open: {error}");
        Instances.RedrawCanvas();
      }
    }

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
      base.AppendAdditionalComponentMenuItems(menu);
      Menu_AppendSeparator(menu);
      foreach (var example in ExampleFiles.All)
      {
        var e = example;
        var item = Menu_AppendItem(menu, $"Open · 打开  {e.Label}", (s, a) => Open(e), ExampleFiles.Exists(e));
        item.ToolTipText = e.About;
      }
    }

    public override Guid ComponentGuid => new Guid("a106111d-3409-475f-99ce-8a0d65cdfc47");
  }

  /// <summary>Draws the Examples component as a titled panel with one button per example.</summary>
  public sealed class ExampleButtonsAttributes : GH_ComponentAttributes
  {
    const float Width = 196, Header = 30, ButtonHeight = 20, Gap = 4, Pad = 6;

    readonly ExamplesComponent _owner;
    RectangleF[] _buttons = new RectangleF[0];
    int _pressed = -1;

    public ExampleButtonsAttributes(ExamplesComponent owner) : base(owner) { _owner = owner; }

    protected override void Layout()
    {
      base.Layout();
      int n = ExampleFiles.All.Length;
      var box = new RectangleF(Pivot.X, Pivot.Y, Width, Header + n * (ButtonHeight + Gap) - Gap + Pad);
      _buttons = new RectangleF[n];
      for (int i = 0; i < n; i++)
        _buttons[i] = new RectangleF(box.X + Pad, box.Y + Header + i * (ButtonHeight + Gap), box.Width - 2 * Pad, ButtonHeight);
      Bounds = box;
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
      if (channel != GH_CanvasChannel.Objects) return;
      using (var body = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal, 4, 0))
        body.Render(graphics, Selected, Owner.Locked, false);

      // Title row: the icon and the name, in both languages.
      var icon = Owner.Icon_24x24;
      float x = Bounds.X + Pad;
      if (icon != null)
      {
        graphics.DrawImage(icon, x, Bounds.Y + (Header - 24) / 2 - 1, 24, 24);
        x += 28;
      }
      using (var ink = new SolidBrush(Owner.Locked ? Color.Gray : Color.Black))
      {
        var title = new RectangleF(x, Bounds.Y, Bounds.Right - x - Pad, Header - 2);
        var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        graphics.DrawString("Examples · 示例", GH_FontServer.Standard, ink, title, format);
      }

      for (int i = 0; i < _buttons.Length; i++)
      {
        var example = ExampleFiles.All[i];
        var palette = !ExampleFiles.Exists(example) ? GH_Palette.Grey : i == _pressed ? GH_Palette.Grey : GH_Palette.Black;
        using (var button = GH_Capsule.CreateTextCapsule(_buttons[i], _buttons[i], palette, example.Label, 2, 0))
          button.Render(graphics, Selected, Owner.Locked, false);
      }
    }

    int ButtonAt(PointF canvasPoint)
    {
      for (int i = 0; i < _buttons.Length; i++)
        if (_buttons[i].Contains(canvasPoint)) return i;
      return -1;
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      int i = e.Button == MouseButtons.Left && !Owner.Locked ? ButtonAt(e.CanvasLocation) : -1;
      if (i >= 0 && ExampleFiles.Exists(ExampleFiles.All[i]))
      {
        _pressed = i;
        sender.Refresh();
        return GH_ObjectResponse.Capture;
      }
      return base.RespondToMouseDown(sender, e);
    }

    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (_pressed < 0) return base.RespondToMouseUp(sender, e);
      int i = _pressed;
      _pressed = -1;
      sender.Refresh();
      // Only a release on the same button opens it, like any button; dragging off cancels.
      if (ButtonAt(e.CanvasLocation) == i) _owner.Open(ExampleFiles.All[i]);
      return GH_ObjectResponse.Release;
    }

    public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
    {
      int i = ButtonAt(canvasPoint);
      if (i < 0)
      {
        base.SetupTooltip(canvasPoint, e);
        return;
      }
      var example = ExampleFiles.All[i];
      e.Title = example.Label;
      e.Text = example.About;
      e.Description = ExampleFiles.Exists(example)
        ? "Click to open it in a new tab. Save asks where to keep your copy; the example itself never changes.\n点击在新标签页打开。保存时会问存到哪里，示例本身不会被改动。"
        : $"{example.File}.gh is missing from this build.";
      e.Icon = Owner.Icon_24x24;
    }
  }
}
