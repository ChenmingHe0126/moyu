using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Moyu.Components
{
  /// <summary>A component that starts and stops a game from its Run button.</summary>
  public interface IRunnable
  {
    bool IsRunning { get; }
    void Run();
  }

  /// <summary>Adds a Run / Stop button under a game component.</summary>
  public sealed class RunButtonAttributes : GH_ComponentAttributes
  {
    RectangleF _button;

    public RunButtonAttributes(GH_Component owner) : base(owner) { }

    IRunnable Game => (IRunnable)Owner;

    protected override void Layout()
    {
      base.Layout();
      var b = Bounds;
      _button = new RectangleF(b.X + 4, b.Bottom + 2, b.Width - 8, 20);
      Bounds = new RectangleF(b.X, b.Y, b.Width, b.Height + 26);
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
      base.Render(canvas, graphics, channel);
      if (channel != GH_CanvasChannel.Objects) return;
      string label = Game.IsRunning ? "■ Stop" : "▶ Run";
      using (var capsule = GH_Capsule.CreateTextCapsule(_button, _button, Game.IsRunning ? GH_Palette.Grey : GH_Palette.Black, label, 2, 0))
        capsule.Render(graphics, Selected, Owner.Locked, false);
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (e.Button == MouseButtons.Left && _button.Contains(e.CanvasLocation))
      {
        // A disabled component has not solved, so its settings are stale or missing; a running game can still be stopped.
        if (!Owner.Locked || Game.IsRunning) Game.Run();
        sender.Refresh();
        return GH_ObjectResponse.Handled;
      }
      return base.RespondToMouseDown(sender, e);
    }
  }
}
