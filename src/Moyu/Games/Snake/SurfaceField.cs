using System;
using System.Collections.Generic;
using System.Linq;
using Moyu.Games.Snake.Core;
using Rhino.Geometry;

namespace Moyu.Games.Snake
{
  /// <summary>
  /// A surface cut into the snake's play field: cells of about equal size along the surface, wrapping where the
  /// surface is closed, blocked where a trim removed it or the surface pinches to a point. Also holds what the
  /// renderer needs: cell centres, outward normals, tangents, the surface sampled twice per cell and a mesh of the
  /// playable cells.
  /// </summary>
  public sealed class SurfaceField
  {
    public readonly SurfaceGrid Grid;
    /// <summary>Typical cell size in model units.</summary>
    public readonly double Step;
    /// <summary>The playable cells, two quads a side, lifted a hair off the surface so it wins over a preview of it.</summary>
    public readonly Mesh Mesh;
    public readonly BoundingBox Bounds;
    /// <summary>Closed in at least one direction (cylinder, torus, sphere): no "above" to keep the camera at.</summary>
    public readonly bool Closed;
    /// <summary>An open site that clearly faces up (terrain, a roof), as opposed to a wall-like one or a closed form.</summary>
    public readonly bool Upright;

    readonly Point3d[] _centres, _samples;
    readonly Vector3d[] _normals, _du, _dv, _sampleNormals;

    SurfaceField(SurfaceGrid grid, double step, Mesh mesh, bool closed, bool upright, Point3d[] centres, Vector3d[] normals, Vector3d[] du, Vector3d[] dv,
      Point3d[] samples, Vector3d[] sampleNormals)
    {
      _samples = samples;
      _sampleNormals = sampleNormals;
      Grid = grid;
      Step = step;
      Mesh = mesh;
      Closed = closed;
      Upright = upright;
      _centres = centres;
      _normals = normals;
      _du = du;
      _dv = dv;
      Bounds = mesh.GetBoundingBox(false);
    }

    /// <summary>
    /// The surface sampled at cell corners, edge midpoints and centres: (2 Nu + 1) × (2 Nv + 1) points, cell (u, v)
    /// spanning k = 2u … 2u + 2 and l = 2v … 2v + 2.
    /// </summary>
    public Point3d Sample(int k, int l) => _samples[l * (2 * Grid.Nu + 1) + k];
    public Vector3d SampleNormal(int k, int l) => _sampleNormals[l * (2 * Grid.Nu + 1) + k];

    public Point3d Centre(Cell c) => _centres[Grid.Index(c)];
    public Vector3d Normal(Cell c) => _normals[Grid.Index(c)];
    /// <summary>A point <paramref name="height"/> above the cell centre, along the outward normal.</summary>
    public Point3d Lifted(Cell c, double height) => _centres[Grid.Index(c)] + _normals[Grid.Index(c)] * height;
    /// <summary>Unit 3D direction of the grid step (du, dv) at a cell.</summary>
    public Vector3d Direction(Cell c, int du, int dv)
    {
      int i = Grid.Index(c);
      var v = _du[i] * du + _dv[i] * dv;
      v.Unitize();
      return v;
    }

    public string Describe()
    {
      string wrap = Grid.WrapU && Grid.WrapV ? " · wraps both ways" : Grid.WrapU ? " · wraps U" : Grid.WrapV ? " · wraps V" : "";
      return $"{Grid.Nu} × {Grid.Nv}{wrap}";
    }

    /// <summary>Cuts a face into about <paramref name="cells"/> cells along its longer direction.</summary>
    public static SurfaceField Build(BrepFace face, int cells, out string error)
    {
      error = null;
      // Domain, closure and isocurves describe the untrimmed surface; shrink a copy so they describe the face the
      // user passed, and the cell budget is spent on the site, not on a far larger terrain it was cut from.
      if (!face.IsSurface)
      {
        var shrunk = face.Brep.DuplicateBrep();
        if (shrunk.Faces.ShrinkFaces() && face.FaceIndex < shrunk.Faces.Count) face = shrunk.Faces[face.FaceIndex];
      }
      var domU = face.Domain(0);
      var domV = face.Domain(1);
      bool wrapU = face.IsClosed(0), wrapV = face.IsClosed(1);

      double lenU = IsoLength(face, 0, domV), lenV = IsoLength(face, 1, domU);
      if (!(lenU > 0) || !(lenV > 0))
      {
        error = "The surface has no area to play on.";
        return null;
      }
      double longer = Math.Max(lenU, lenV);
      int nu = Math.Max(6, (int)Math.Round(cells * lenU / longer));
      int nv = Math.Max(6, (int)Math.Round(cells * lenV / longer));

      // Parameters spaced by arc length along the middle isocurves, so cells come out about even.
      var us = Samples(face, 0, domU, domV.Mid, 2 * nu);
      var vs = Samples(face, 1, domV, domU.Mid, 2 * nv);
      int su = 2 * nu + 1, sv = 2 * nv + 1;
      var pts = new Point3d[su * sv];
      var nrm = new Vector3d[su * sv];
      var box = BoundingBox.Empty;
      for (int l = 0; l < sv; l++)
        for (int k = 0; k < su; k++)
        {
          var p = face.PointAt(us[k], vs[l]);
          pts[l * su + k] = p;
          nrm[l * su + k] = face.NormalAt(us[k], vs[l]);
          box.Union(p);
        }

      bool closed = wrapU || wrapV;
      var centre = box.Center;
      // Pinched spots (sphere poles) have no normal; borrow the direction away from the middle, or world up.
      for (int i = 0; i < nrm.Length; i++)
        if (!nrm[i].IsValid || !nrm[i].Unitize())
        {
          var away = pts[i] - centre;
          nrm[i] = closed && away.Unitize() ? away : Vector3d.ZAxis;
        }
      // Normals point up on an open surface and outwards on a closed one, whatever the surface's own orientation.
      double vote = 0;
      for (int i = 0; i < nrm.Length; i++) vote += closed ? nrm[i] * (pts[i] - centre) : nrm[i].Z;
      if (vote < 0)
        for (int i = 0; i < nrm.Length; i++) nrm[i] = -nrm[i];
      // A wall-like open surface votes near zero: its sign is noise, so the camera must not lean on it.
      bool upright = !closed && Math.Abs(vote) / nrm.Length > 0.3;

      Point3d P(int k, int l) => pts[l * su + k];
      Vector3d N(int k, int l) => nrm[l * su + k];

      int n = nu * nv;
      var x = new double[n];
      var y = new double[n];
      var z = new double[n];
      var centres = new Point3d[n];
      var normals = new Vector3d[n];
      var du = new Vector3d[n];
      var dv = new Vector3d[n];
      var chordU = new double[n];
      var chordV = new double[n];
      var blocked = new bool[n];
      bool trimmed = !face.IsSurface;
      for (int j = 0; j < nv; j++)
        for (int i = 0; i < nu; i++)
        {
          int c = j * nu + i;
          var p = P(2 * i + 1, 2 * j + 1);
          centres[c] = p;
          normals[c] = N(2 * i + 1, 2 * j + 1);
          x[c] = p.X;
          y[c] = p.Y;
          z[c] = p.Z;
          du[c] = P(2 * i + 2, 2 * j + 1) - P(2 * i, 2 * j + 1);
          dv[c] = P(2 * i + 1, 2 * j + 2) - P(2 * i + 1, 2 * j);
          chordU[c] = du[c].Length;
          chordV[c] = dv[c].Length;
          if (!du[c].Unitize()) du[c] = Vector3d.CrossProduct(dv[c], normals[c]);
          if (!dv[c].Unitize()) dv[c] = Vector3d.CrossProduct(normals[c], du[c]);
          du[c].Unitize();
          dv[c].Unitize();
          // Outside the trim at the centre, or at two of its corners, and the cell is a wall; one clipped corner is
          // left playable so a curved boundary does not cost a whole ring of cells.
          if (trimmed && (Outside(face, us[2 * i + 1], vs[2 * j + 1]) || Corners(face, us, vs, i, j) >= 2)) blocked[c] = true;
        }
      // Cells squeezed to a sliver (next to a pole) would be a cramped, jumpy place to steer: treat them as walls.
      // Each direction is judged on its own, so the evenly elongated cells of a long thin strip still play.
      double typU = Median(chordU), typV = Median(chordV);
      bool slivers = false;
      for (int c = 0; c < n; c++)
        if (!blocked[c] && (chordU[c] < 0.2 * typU || chordV[c] < 0.2 * typV))
        {
          blocked[c] = true;
          slivers = true;
        }

      var grid = new SurfaceGrid(nu, nv, wrapU, wrapV, x, y, z, blocked);
      var free = grid.FreeCells().ToList();
      if (free.Count < 12)
      {
        error = slivers
          ? "Too little of the surface is left to play on (it is mostly trimmed away, pinched or squeezed into sliver cells); raising Cells helps."
          : "Too little of the surface is left to play on (it is mostly trimmed away or pinched).";
        return null;
      }
      double step = free.Average(c => (chordU[grid.Index(c)] + chordV[grid.Index(c)]) / 2);

      double meshLift = 0.01 * step;
      var mesh = new Mesh();
      for (int i = 0; i < pts.Length; i++)
      {
        mesh.Vertices.Add(pts[i] + nrm[i] * meshLift);
        mesh.Normals.Add(nrm[i]);
      }
      foreach (var c in free)
      {
        int k = 2 * c.U, l = 2 * c.V;
        for (int a = 0; a < 2; a++)
          for (int b = 0; b < 2; b++)
            mesh.Faces.AddFace((l + b) * su + k + a, (l + b) * su + k + a + 1, (l + b + 1) * su + k + a + 1, (l + b + 1) * su + k + a);
      }
      mesh.Compact();
      return new SurfaceField(grid, step, mesh, closed, upright, centres, normals, du, dv, pts, nrm);
    }

    static bool Outside(BrepFace face, double u, double v) => face.IsPointOnFace(u, v) == PointFaceRelation.Exterior;

    /// <summary>How many of cell (i, j)'s four corners the trim has removed.</summary>
    static int Corners(BrepFace face, double[] us, double[] vs, int i, int j)
    {
      int n = 0;
      for (int a = 0; a <= 2; a += 2)
        for (int b = 0; b <= 2; b += 2)
          if (Outside(face, us[2 * i + a], vs[2 * j + b])) n++;
      return n;
    }

    /// <summary>Mean length of three isocurves running in <paramref name="direction"/>.</summary>
    static double IsoLength(Surface s, int direction, Interval other)
    {
      double sum = 0;
      foreach (double t in new[] { 0.25, 0.5, 0.75 })
        sum += s.IsoCurve(direction, other.ParameterAt(t))?.GetLength() ?? 0;
      return sum / 3;
    }

    /// <summary><paramref name="count"/> + 1 parameters along a direction, evenly spaced by length on the isocurve at <paramref name="at"/>.</summary>
    static double[] Samples(Surface s, int direction, Interval dom, double at, int count)
    {
      var t = new double[count + 1];
      var iso = s.IsoCurve(direction, at);
      bool byLength = iso != null && iso.GetLength() > 1e-9;
      for (int k = 0; k <= count; k++)
      {
        double f = (double)k / count;
        t[k] = byLength && iso.NormalizedLengthParameter(f, out double p) ? p : dom.ParameterAt(f);
      }
      // The isocurve may come back with its own domain; keep the ends exactly on the surface's.
      t[0] = dom.T0;
      t[count] = dom.T1;
      return t;
    }

    static double Median(IEnumerable<double> values)
    {
      var v = values.Where(d => d > 0).OrderBy(d => d).ToList();
      return v.Count == 0 ? 0 : v[v.Count / 2];
    }
  }
}
