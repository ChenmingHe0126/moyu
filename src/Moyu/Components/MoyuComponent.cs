using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.TwoD;
using Rhino.Geometry;

namespace Moyu.Components
{
  /// <summary>
  /// The Moyu tab's panels. Grasshopper sorts panels by name, so each starts with its number: the Examples launcher
  /// first, then the parts in the order they were built (topology 2D and 3D, then VoxStack, Serpentine, Rebound, Swift).
  /// </summary>
  public static class Panels
  {
    public const string Examples = "0 | Examples", TwoD = "1 | 2D", ThreeD = "2 | 3D", Massing = "3 | Massing",
      Landscape = "4 | Landscape", Facade = "5 | Facade", Section = "6 | Section";
  }

  /// <summary>
  /// Base for all Moyu components. Output data implementing <see cref="IMoyuPreview"/> is drawn in the viewport,
  /// so condition components show their loads, supports and regions without extra wiring.
  /// </summary>
  public abstract class MoyuComponent : GH_Component
  {
    public const string Tab = "Moyu";

    protected MoyuComponent(string name, string nickname, string description, string subCategory)
      : base(name, nickname, description, Tab, subCategory) { }

    /// <summary>Embedded Icons/&lt;ClassName&gt;.png, e.g. Icons/SolverComponent.png.</summary>
    protected override Bitmap Icon => Icons.Get(GetType().Name);

    public override bool IsPreviewCapable => true;

    /// <summary>Components with plain geometry outputs (no <see cref="IMoyuPreview"/>) let Grasshopper preview them as usual.</summary>
    protected virtual bool NativePreview => false;

    protected IEnumerable<IMoyuPreview> PreviewItems()
    {
      foreach (var param in Params.Output)
        foreach (var item in param.VolatileData.AllData(true))
          if (item is IGH_Goo goo && goo.ScriptVariable() is IMoyuPreview preview)
            yield return preview;
    }

    public override BoundingBox ClippingBox
    {
      get
      {
        if (NativePreview) return base.ClippingBox;
        var box = BoundingBox.Empty;
        foreach (var p in PreviewItems()) box.Union(p.Bounds);
        return box;
      }
    }

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
      if (NativePreview)
      {
        base.DrawViewportWires(args);
        return;
      }
      Color? highlight = Attributes.Selected ? args.WireColour_Selected : (Color?)null;
      foreach (var p in PreviewItems())
      {
        try { p.Draw(args.Display, args.Viewport, highlight); }
        catch (Exception) { /* never let a preview glitch break the canvas */ }
      }
    }

    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
      if (NativePreview) base.DrawViewportMeshes(args);
    }

    /// <summary>Point → point anchor; anything curve-like → curve anchor.</summary>
    protected static Anchor2D ToAnchor(IGH_GeometricGoo goo)
    {
      if (goo is GH_Point gp) return new Anchor2D(gp.Value);
      Curve curve = null;
      if (GH_Convert.ToCurve(goo, ref curve, GH_Conversion.Both) && curve != null) return new Anchor2D(curve.DuplicateCurve());
      var point = Point3d.Unset;
      if (GH_Convert.ToPoint3d(goo, ref point, GH_Conversion.Both) && point.IsValid) return new Anchor2D(point);
      return null;
    }

    protected static List<T> Collect<T>(IGH_DataAccess da, int index, string label, List<string> errors) where T : class
    {
      var raw = new List<IGH_Goo>();
      var result = new List<T>();
      if (!da.GetDataList(index, raw)) return result;
      foreach (var item in raw)
      {
        if (item == null) continue;
        if (MoyuGoo<T>.TryGet(item, out var value)) result.Add(value);
        else errors.Add(WrongInput(label, item, typeof(T).Name));
      }
      return result;
    }

    /// <summary>
    /// Why <paramref name="item"/> cannot be used as <paramref name="expected"/>: the wrong type, or Moyu data that
    /// came back empty from a saved file (right-click &gt; Internalise data keeps only the wrapper).
    /// </summary>
    protected static string WrongInput(string label, IGH_Goo item, string expected)
    {
      if (item is IMoyuGoo && !item.IsValid)
        return $"{label} input holds an empty {item.TypeName}; reconnect the component that made it (Moyu data cannot be internalised).";
      return $"{label} input received {item.TypeName}; expected {expected}.";
    }
  }
}
