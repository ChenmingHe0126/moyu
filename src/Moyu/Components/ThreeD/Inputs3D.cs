using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Moyu.ThreeD;
using Rhino.Geometry;

namespace Moyu.Components.ThreeD
{
  /// <summary>Turns Grasshopper geometry into what the 3D conditions need.</summary>
  static class Inputs3D
  {
    /// <summary>Breps, boxes, extrusions, surfaces and meshes → one mesh. <paramref name="closed"/> reports solidity.</summary>
    public static Mesh ToMesh(IGH_GeometricGoo goo, out bool closed)
    {
      closed = false;
      if (goo is GH_Mesh gm && gm.Value != null)
      {
        closed = gm.Value.IsClosed;
        return gm.Value.DuplicateMesh();
      }
      Brep brep = null;
      if (!GH_Convert.ToBrep(goo, ref brep, GH_Conversion.Both) || brep == null) return null;
      closed = brep.IsSolid;
      var pieces = Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh);
      if (pieces == null || pieces.Length == 0) return null;
      var mesh = new Mesh();
      foreach (var p in pieces) mesh.Append(p);
      return mesh;
    }

    /// <summary>Point → point anchor; curve-like → curve anchor; surface, brep or mesh → surface anchor.</summary>
    public static Anchor3D ToAnchor(IGH_GeometricGoo goo)
    {
      if (goo is GH_Point gp) return new Anchor3D(gp.Value);
      if (goo is GH_Mesh)
      {
        // Same test as below: an empty wrapper or a faceless mesh gives the solver nothing to land on.
        var m = ToMesh(goo, out _);
        return m != null && m.Faces.Count > 0 ? new Anchor3D(m) : null;
      }
      Curve curve = null;
      if (!(goo is GH_Brep || goo is GH_Surface || goo is GH_Box) &&
          GH_Convert.ToCurve(goo, ref curve, GH_Conversion.Both) && curve != null)
        return new Anchor3D(curve.DuplicateCurve());
      var mesh = ToMesh(goo, out _);
      if (mesh != null && mesh.Faces.Count > 0) return new Anchor3D(mesh);
      var point = Point3d.Unset;
      if (GH_Convert.ToPoint3d(goo, ref point, GH_Conversion.Both) && point.IsValid) return new Anchor3D(point);
      return null;
    }
  }
}
