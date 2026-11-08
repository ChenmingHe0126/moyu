using System;
using System.Drawing;
using System.IO;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;

namespace Moyu.Components.Examples
{
  /// <summary>One example definition shipped inside the plugin.</summary>
  public sealed class Example
  {
    /// <summary>File name without .gh, as in packaging/examples.</summary>
    public readonly string File;
    public readonly string Chinese, English, About;

    public Example(string file, string chinese, string english, string about)
    {
      File = file;
      Chinese = chinese;
      English = english;
      About = about;
    }

    /// <summary>The button text: Chinese · English.</summary>
    public string Label => $"{Chinese} · {English}";
  }

  /// <summary>
  /// The six example definitions (packaging/examples), embedded in the .gha at build time so they are there however
  /// Moyu was installed. Opening one never touches a file: it becomes a new, unnamed document, so Save asks where to
  /// keep the user's copy and the example itself stays as shipped.
  /// </summary>
  public static class ExampleFiles
  {
    public static readonly Example[] All =
    {
      new Example("Topo2D", "2D 拓扑优化", "Topology 2D", "Cantilever, bridge elevation, L-bracket with a bolt hole.\n悬臂、立面桥、带螺栓孔的 L 形支架。"),
      new Example("Topo3D", "3D 拓扑优化", "Topology 3D", "Voxel optimisation: cantilever, canopy, bracket.\n体素优化：悬臂、雨棚、支架。"),
      new Example("VoxStack", "叠楼", "VoxStack", "3D falling blocks (Massing panel), with custom units.\n3D 落块（Massing 面板），含自定义积木。"),
      new Example("Serpentine", "蜿蜒", "Serpentine", "Snake on a surface (Landscape panel).\n曲面贪吃蛇（Landscape 面板）。"),
      new Example("Rebound", "回弹", "Rebound", "Brick breaker on a facade (Facade panel).\n立面打砖块（Facade 面板）。"),
      new Example("Swift", "雨燕", "Swift", "One-button flight through a section (Section panel).\n剖面里扇翅穿墙（Section 面板）。"),
    };

    static string Resource(Example e) => $"Moyu.Examples.{e.File}.gh";

    /// <summary>True when the example was embedded in this build.</summary>
    public static bool Exists(Example e) => typeof(ExampleFiles).Assembly.GetManifestResourceNames().Contains(Resource(e));

    /// <summary>Reads the embedded example into a new document that has no file path. Throws when it cannot be read.</summary>
    public static GH_Document Load(Example e)
    {
      byte[] bytes;
      using (var stream = typeof(ExampleFiles).Assembly.GetManifestResourceStream(Resource(e)))
      {
        if (stream == null) throw new InvalidOperationException($"{e.File}.gh is not in this build of Moyu.");
        using (var memory = new MemoryStream())
        {
          stream.CopyTo(memory);
          bytes = memory.ToArray();
        }
      }
      var archive = new GH_Archive();
      if (!archive.Deserialize_Binary(bytes)) throw new InvalidOperationException($"{e.File}.gh could not be read.");
      var doc = new GH_Document();
      if (!archive.ExtractObject(doc, "Definition")) throw new InvalidOperationException($"{e.File}.gh holds no definition.");
      return doc;
    }

    /// <summary>Opens the example in a new Grasshopper tab, framed so its note at the top is in view.</summary>
    public static bool Open(Example e, out string error)
    {
      error = null;
      try
      {
        var doc = Load(e);
        var canvas = Instances.ActiveCanvas;
        if (canvas == null) throw new InvalidOperationException("Grasshopper's canvas is not open.");
        Instances.DocumentServer.AddDocument(doc);
        canvas.Document = doc;
        doc.Enabled = true;
        doc.NewSolution(false);
        doc.IsModified = false;
        Frame(canvas, doc);
        canvas.Refresh();
        return true;
      }
      catch (Exception ex)
      {
        error = ex.Message;
        return false;
      }
    }

    /// <summary>
    /// Shows the definition from its top-left corner (the note), at the canvas's normal zoom or smaller so the full
    /// width fits. A file saved without a canvas carries no view, and Grasshopper would open it at (0, 0), cutting off
    /// everything above.
    /// </summary>
    static void Frame(Grasshopper.GUI.Canvas.GH_Canvas canvas, GH_Document doc)
    {
      var box = doc.BoundingBox(false);
      if (box.Width <= 0 || box.Height <= 0) return;
      const float margin = 24;
      var vp = canvas.Viewport;
      float fit = (canvas.Width - 2 * margin) / box.Width;
      // Never zoom in past the normal size, and never below half of it (labels must stay readable).
      vp.Zoom = Math.Max(vp.Zoom * 0.5f, Math.Min(vp.Zoom, fit));
      vp.ComputeProjection();
      // The projection is linear in the offsets: measure how far the corner moves per unit and solve for the margin.
      var at = new PointF(box.Left, box.Top);
      float x0 = vp.ProjectPoint(at).X, y0 = vp.ProjectPoint(at).Y;
      vp.Tx += 1;
      vp.Ty += 1;
      vp.ComputeProjection();
      float dx = vp.ProjectPoint(at).X - x0, dy = vp.ProjectPoint(at).Y - y0;
      if (Math.Abs(dx) > 1e-6) vp.Tx += (int)Math.Round((margin - (x0 + dx)) / dx);
      if (Math.Abs(dy) > 1e-6) vp.Ty += (int)Math.Round((margin - (y0 + dy)) / dy);
      vp.ComputeProjection();
    }
  }
}
