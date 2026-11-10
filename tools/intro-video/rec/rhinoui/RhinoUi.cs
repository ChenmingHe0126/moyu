using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Moyu.Components.Massing;
using Moyu.Games.Common;
using Moyu.Games.Common.Core;
using Moyu.Games.Stack;
using Moyu.Games.Stack.Core;
using Rhino;

namespace MoyuRec
{
  /// <summary>
  /// The whole Rhino window and the Grasshopper window, frame by frame: the VoxStack example is open, the custom-unit
  /// Massing Study's ▶ Run is pressed at `click` s, the game starts in Rhino's own Perspective viewport (real 3-2-1),
  /// a bot drops pieces, and at `boss` s the boss key turns the viewport into the massing study while the component
  /// and Rhino's command line report "Option B". Frame: Rhino window (1920×1080) above the Grasshopper window (1920×gh).
  /// args: click, boss, gh (Grasshopper window height), zoom (canvas).
  /// </summary>
  public sealed class RhinoUi : IShot, ICustomCapture
  {
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const int W = 1920, RH = 1080;
    int _gh = 1400;
    IntPtr _main, _editor;
    GH_Component _comp;
    StackSession _s;
    double _click, _boss;
    bool _clicked, _bossOn;
    int _planFor = -1;
    Int3 _target;
    double _nextAct;

    public int Frames => 60 * 10;
    public Size FrameSize => new Size(W, RH + _gh);

    public void Setup(Rec rec)
    {
      _gh = (int)rec.Arg("gh", 1400);
      _click = rec.Arg("click", 1.0);
      _boss = rec.Arg("boss", 6.5);

      _main = RhinoApp.MainWindowHandle();
      ShowWindow(_main, 9);
      SetWindowPos(_main, IntPtr.Zero, 0, 0, W, RH, 0x0004);
      var gh = (Grasshopper.Plugin.GH_RhinoScriptInterface)RhinoApp.GetPlugInObject("Grasshopper");
      gh.DisableBanner();
      gh.LoadEditor();
      gh.ShowEditor();
      var ed = Grasshopper.Instances.DocumentEditor;
      // A fresh copy every take, so the components show "Ready" and no earlier game's metrics.
      gh.CloseAllDocuments();
      gh.OpenDocument(rec.Arg("file", @"<moyu repo>\packaging\examples\VoxStack.gh"));
      ed.WindowState = FormWindowState.Normal;
      _editor = ed.Handle;
      SetWindowPos(_editor, IntPtr.Zero, W, 0, W, _gh, 0x0004);
      Application.DoEvents();

      // Ribbon on the Moyu tab, so the Massing / Landscape / Facade / Section panels show.
      foreach (var c in All(ed))
        if (c.GetType().Name == "GH_Ribbon") { dynamic r = c; r.ActiveTabName = "Moyu"; c.Refresh(); }

      // Canvas: the custom-unit group (Brep sketch → Voxel Unit → Unit Library → Massing Study), whole and centred.
      var canvas = Grasshopper.Instances.ActiveCanvas;
      var doc = canvas.Document;
      _comp = doc.Objects.OfType<MassingStudyComponent>().OrderByDescending(c => c.Attributes.Bounds.Y).First();
      var keep = doc.Objects.Where(o => !(o is Grasshopper.Kernel.Special.GH_Group) && o.Attributes.Bounds.Y > 300).Select(o => o.Attributes.Bounds).ToList();
      var box = keep.Aggregate(RectangleF.Union);
      box.Inflate(30, 40);
      float zoom = (float)Math.Min(canvas.Width / box.Width, canvas.Height / box.Height) * (float)rec.Arg("zoom", 0.97);
      canvas.Viewport.Zoom = zoom;
      canvas.Viewport.MidPoint = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
      canvas.Refresh();

      // Where the ▶ Run button is, in the Grasshopper part of the frame (for the cursor drawn in the edit).
      var b = _comp.Attributes.Bounds;
      var button = canvas.Viewport.ProjectPoint(new PointF(b.X + b.Width / 2, b.Bottom - 14));
      var screen = canvas.PointToScreen(Point.Round(button));
      GetWindowRect(_editor, out var er);
      rec.Note($"run button at frame ({screen.X - er.L}, {RH + screen.Y - er.T}); canvas {canvas.Size}, zoom {zoom:F2}");
      var canvasTop = canvas.PointToScreen(Point.Empty);
      rec.Note($"canvas in frame: x {canvasTop.X - er.L}, y {RH + canvasTop.Y - er.T}, {canvas.Width} x {canvas.Height}");
      var panel = doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Panel>().OrderByDescending(o => o.Attributes.Bounds.Y).First();
      var pr = canvas.Viewport.ProjectRectangle(panel.Attributes.Bounds);
      var pp = canvas.PointToScreen(Point.Round(pr.Location));
      rec.Note($"metrics panel in frame: x {pp.X - er.L}, y {RH + pp.Y - er.T}, {pr.Width:F0} x {pr.Height:F0}");

      // A clean Rhino: four views with Perspective active, nothing on the command history.
      rec.Doc.Views.ActiveView = rec.Doc.Views.First(v => v.ActiveViewport.Name == "Perspective");
      rec.View = rec.Doc.Views.ActiveView;
      RhinoApp.ClearCommandHistoryWindow();
      rec.Doc.Views.Redraw();
      Application.DoEvents();
    }

    static IEnumerable<Control> All(Control root)
    {
      yield return root;
      foreach (Control c in root.Controls)
        foreach (var d in All(c)) yield return d;
    }

    public void Frame(Rec rec, int i)
    {
      double t = i * rec.Dt;
      // The loader's own output lands on the command line after Setup: start the recording with an empty history.
      if (i == 0) RhinoApp.ClearCommandHistoryWindow();
      if (!_clicked && t >= _click - 1e-9)
      {
        _clicked = true;
        ((MassingStudyComponent)_comp).Run();
        _s = GameSession.Current as StackSession ?? throw new InvalidOperationException("Run did not start a VoxStack session");
        rec.View = _s.Doc().Views.Find(_s.ViewportId);
        rec.Adopt(_s);
        // The component's own choice is Auto (the Windows language); the English cut wants the English HUD.
        if (rec.Arg("lang", "") == "en") _s.Language = UiLanguage.English;
      }
      if (_s != null)
      {
        if (!_bossOn && t >= _boss - 1e-9) { _bossOn = true; _s.ToggleBoss(); }
        if (!_s.Boss && _s.Countdown <= 0) Bot(_s.Game, t);
        rec.Step();
      }
    }

    /// <summary>Puts each piece where it lands lowest with the fewest holes under it, a step every few frames.</summary>
    void Bot(StackGame g, double t)
    {
      if (g.State != StackState.Playing || g.Current == null || t < _nextAct) return;
      var well = g.Well;
      if (_planFor != g.PiecesPlaced)
      {
        _planFor = g.PiecesPlaced;
        var best = (score: double.MaxValue, at: g.Position);
        for (int x = -3; x < well.Nx + 3; x++)
          for (int y = -3; y < well.Ny + 3; y++)
          {
            var p = new Int3(x, y, g.Position.Z);
            if (!g.Offsets.All(o => well.IsFree(p + o))) continue;
            int d = 0;
            while (g.Offsets.All(o => well.IsFree(p + o + (d + 1) * Int3.Down))) d++;
            var cells = g.Offsets.Select(o => p + o + d * Int3.Down).ToList();
            int holes = cells.Count(c => c.Z > 0 && well.IsFree(c + Int3.Down) && !cells.Contains(c + Int3.Down));
            double score = cells.Max(c => c.Z) + 0.6 * holes + 0.04 * (Math.Abs(x - g.Position.X) + Math.Abs(y - g.Position.Y));
            if (score < best.score) best = (score, p);
          }
        _target = best.at;
        _nextAct = t + 0.12;
        return;
      }
      var pos = g.Position;
      if (pos.X != _target.X) g.Move(new Int3(Math.Sign(_target.X - pos.X), 0, 0));
      else if (pos.Y != _target.Y) g.Move(new Int3(0, Math.Sign(_target.Y - pos.Y), 0));
      else g.HardDrop();
      _nextAct = t + 0.09;
    }

    public Bitmap Capture(Rec rec)
    {
      rec.Doc.Views.Redraw();
      Grasshopper.Instances.ActiveCanvas.Refresh();
      var frame = new Bitmap(W, RH + _gh, PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(frame))
      {
        g.Clear(Color.Black);
        using (var a = Grab(_main)) g.DrawImage(a, 0, 0, W, RH);
        using (var b = Grab(_editor)) g.DrawImage(b, 0, RH, W, _gh);
      }
      return frame;
    }

    static Bitmap Grab(IntPtr h)
    {
      GetWindowRect(h, out var r);
      var bmp = new Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T), PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(bmp))
      {
        var dc = g.GetHdc();
        PrintWindow(h, dc, 2);
        g.ReleaseHdc(dc);
      }
      return bmp;
    }

    public void Teardown(Rec rec) => _s?.Stop();
  }

  static class SessionExt
  {
    public static RhinoDoc Doc(this GameSession s) => RhinoDoc.ActiveDoc;
  }
}
