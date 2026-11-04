using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Moyu.Games.Stack.Core;
using Rhino.Display;
using Rhino.Geometry;

namespace Moyu.Games.Stack
{
  /// <summary>
  /// What the boss sees: the stacked voxels redrawn as a white-model massing study on a site, with floor lines,
  /// crease edges, surrounding blocks and flat sun shadows. Geometry is in "bay" units (one voxel = 1 × 1 bay,
  /// three storeys = 1.5 bays tall) and placed in the world with <see cref="ToWorld"/>.
  /// </summary>
  public sealed class MassingDisguise
  {
    public const double VoxelHeight = MassingMetrics.StoreysPerVoxel * MassingMetrics.StoreyHeight / MassingMetrics.Bay;
    static readonly Vector3d Sun = new Vector3d(0.75, -0.55, -1.0);

    public readonly Transform ToWorld;
    public readonly MassingMetrics Metrics;
    public readonly bool IsDecoy;
    public readonly Mesh Mass, Shadows, Context, Ground, Roads, Site;
    public readonly Line[] Creases, Floors, ContextEdges, SiteBoundary;
    public readonly BoundingBox WorldBounds;

    readonly HashSet<Int3> _cells;

    public MassingDisguise(IEnumerable<Int3> wellCells, int nx, int ny, int nz, Plane plane, double voxelSize, int seed)
    {
      ToWorld = Transform.PlaneToPlane(Plane.WorldXY, plane) * Transform.Scale(Point3d.Origin, voxelSize);
      var cells = wellCells.ToList();
      IsDecoy = cells.Count < 12;
      if (IsDecoy) cells = MassingMetrics.Decoy(nx, ny, nz, seed);
      _cells = new HashSet<Int3>(cells);
      Metrics = MassingMetrics.From(cells, nx, ny);

      Mass = ExteriorFaces();
      Creases = CreaseEdges(nx, ny, nz).ToArray();
      Floors = FloorLines().ToArray();

      double m = MassingMetrics.SiteMargin;
      Site = Rect(-m, -m, nx + m, ny + m);
      SiteBoundary = Dashes(new[] { new Point3d(-m, -m, 0), new Point3d(nx + m, -m, 0), new Point3d(nx + m, ny + m, 0), new Point3d(-m, ny + m, 0) }, 0.35);
      double r0 = m + 0.6, r1 = r0 + 0.9, far = Math.Max(nx, ny) * 6;
      Ground = Rect(-far, -far, nx + far, ny + far);
      Roads = new Mesh();
      Roads.Append(Rect(-r1, -far, -r0, ny + far));
      Roads.Append(Rect(-far, -r1, nx + far, -r0));
      Roads.Append(Rect(nx + r0, -far, nx + r1, ny + far));
      Roads.Append(Rect(-far, ny + r0, nx + far, ny + r1));

      var rng = new Random(seed * 7919 + 13);
      Context = new Mesh();
      var edges = new List<Line>();
      // Neighbours sized to the scheme, so a small massing is not dwarfed and a tower still has company.
      double massTop = Mass.GetBoundingBox(false).Max.Z;
      foreach (var box in ContextBoxes(nx, ny, r1, rng, massTop))
      {
        var bm = Mesh.CreateFromBox(box, 1, 1, 1);
        Context.Append(bm);
        edges.AddRange(box.GetEdges());
      }
      ContextEdges = edges.ToArray();
      Shadows = Flatten(Context);
      Shadows.Append(Flatten(Mass));

      var b = Mass.GetBoundingBox(false);
      b.Union(new BoundingBox(-far, -far, 0, nx + far, ny + far, 0));
      b.Transform(ToWorld);
      WorldBounds = b;
    }

    bool Occ(int x, int y, int z) => _cells.Contains(new Int3(x, y, z));

    Mesh ExteriorFaces()
    {
      var mesh = new Mesh();
      double h = VoxelHeight;
      foreach (var c in _cells)
      {
        double x0 = c.X, x1 = c.X + 1, y0 = c.Y, y1 = c.Y + 1, z0 = c.Z * h, z1 = (c.Z + 1) * h;
        if (!Occ(c.X + 1, c.Y, c.Z)) Quad(mesh, new Point3d(x1, y0, z0), new Point3d(x1, y1, z0), new Point3d(x1, y1, z1), new Point3d(x1, y0, z1));
        if (!Occ(c.X - 1, c.Y, c.Z)) Quad(mesh, new Point3d(x0, y1, z0), new Point3d(x0, y0, z0), new Point3d(x0, y0, z1), new Point3d(x0, y1, z1));
        if (!Occ(c.X, c.Y + 1, c.Z)) Quad(mesh, new Point3d(x1, y1, z0), new Point3d(x0, y1, z0), new Point3d(x0, y1, z1), new Point3d(x1, y1, z1));
        if (!Occ(c.X, c.Y - 1, c.Z)) Quad(mesh, new Point3d(x0, y0, z0), new Point3d(x1, y0, z0), new Point3d(x1, y0, z1), new Point3d(x0, y0, z1));
        if (!Occ(c.X, c.Y, c.Z + 1)) Quad(mesh, new Point3d(x0, y0, z1), new Point3d(x1, y0, z1), new Point3d(x1, y1, z1), new Point3d(x0, y1, z1));
        if (c.Z > 0 && !Occ(c.X, c.Y, c.Z - 1)) Quad(mesh, new Point3d(x0, y1, z0), new Point3d(x1, y1, z0), new Point3d(x1, y0, z0), new Point3d(x0, y0, z0));
      }
      mesh.Normals.ComputeNormals();
      return mesh;
    }

    static void Quad(Mesh m, Point3d a, Point3d b, Point3d c, Point3d d)
    {
      int i = m.Vertices.Count;
      m.Vertices.Add(a);
      m.Vertices.Add(b);
      m.Vertices.Add(c);
      m.Vertices.Add(d);
      m.Faces.AddFace(i, i + 1, i + 2, i + 3);
    }

    /// <summary>Grid edges where the surface turns (one or three of the four surrounding cells filled, or two diagonally).</summary>
    IEnumerable<Line> CreaseEdges(int nx, int ny, int nz)
    {
      double h = VoxelHeight;
      for (int axis = 0; axis < 3; axis++)
      {
        int na = axis == 0 ? nx : axis == 1 ? ny : nz;
        int nb = axis == 0 ? ny : nx;
        int nt = axis == 2 ? ny : nz;
        for (int a = 0; a < na; a++)
          for (int b = 0; b <= nb; b++)
            for (int t = 0; t <= nt; t++)
            {
              bool o00, o10, o01, o11;
              Point3d p0, p1;
              if (axis == 0)
              {
                o00 = Occ(a, b - 1, t - 1); o10 = Occ(a, b, t - 1); o01 = Occ(a, b - 1, t); o11 = Occ(a, b, t);
                p0 = new Point3d(a, b, t * h); p1 = new Point3d(a + 1, b, t * h);
              }
              else if (axis == 1)
              {
                o00 = Occ(b - 1, a, t - 1); o10 = Occ(b, a, t - 1); o01 = Occ(b - 1, a, t); o11 = Occ(b, a, t);
                p0 = new Point3d(b, a, t * h); p1 = new Point3d(b, a + 1, t * h);
              }
              else
              {
                o00 = Occ(b - 1, t - 1, a); o10 = Occ(b, t - 1, a); o01 = Occ(b - 1, t, a); o11 = Occ(b, t, a);
                p0 = new Point3d(b, t, a * h); p1 = new Point3d(b, t, (a + 1) * h);
              }
              int n = (o00 ? 1 : 0) + (o10 ? 1 : 0) + (o01 ? 1 : 0) + (o11 ? 1 : 0);
              if (n == 1 || n == 3 || (n == 2 && o00 == o11)) yield return new Line(p0, p1);
            }
      }
    }

    /// <summary>A line per storey on every outside wall, so the voxels read as floors.</summary>
    IEnumerable<Line> FloorLines()
    {
      double h = VoxelHeight, storey = h / MassingMetrics.StoreysPerVoxel;
      var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
      foreach (var c in _cells)
        foreach (var (dx, dy) in dirs)
        {
          if (Occ(c.X + dx, c.Y + dy, c.Z)) continue;
          const double off = 0.004;
          Point3d a, b;
          if (dx != 0)
          {
            double x = c.X + (dx > 0 ? 1 : 0) + dx * off;
            a = new Point3d(x, c.Y, 0);
            b = new Point3d(x, c.Y + 1, 0);
          }
          else
          {
            double y = c.Y + (dy > 0 ? 1 : 0) + dy * off;
            a = new Point3d(c.X, y, 0);
            b = new Point3d(c.X + 1, y, 0);
          }
          int first = c.Z > 0 && Occ(c.X, c.Y, c.Z - 1) && !Occ(c.X + dx, c.Y + dy, c.Z - 1) ? 0 : 1;
          for (int s = first; s < MassingMetrics.StoreysPerVoxel; s++)
          {
            double z = c.Z * h + s * storey;
            yield return new Line(new Point3d(a.X, a.Y, z), new Point3d(b.X, b.Y, z));
          }
        }
    }

    static IEnumerable<BoundingBox> ContextBoxes(int nx, int ny, double road, Random rng, double massTop)
    {
      // Blocks on a loose ring of plots beyond the roads: low on the camera's (south-east) side so they never hide
      // the scheme, taller behind it.
      double nearBase = Math.Max(0.8, 0.18 * massTop), farBase = Math.Max(2.0, 0.5 * massTop);
      double start = road + 0.5;
      for (int ring = 0; ring < 2; ring++)
      {
        double inner = start + ring * 4.2, outer = inner + 3.2;
        double min = -outer, max = Math.Max(nx, ny) + outer;
        for (double u = min; u < max - 1; u += 3.6 + rng.NextDouble() * 1.2)
        {
          double w = 2.2 + rng.NextDouble() * 1.2;
          foreach (var side in new[] { 0, 1, 2, 3 })
          {
            if (rng.NextDouble() < 0.3) continue;
            double depth = 2.0 + rng.NextDouble() * 1.2;
            double x0, y0, x1, y1;
            switch (side)
            {
              case 0: x0 = u; x1 = u + w; y0 = -inner - depth; y1 = -inner; break;
              case 1: x0 = u; x1 = u + w; y0 = ny + inner; y1 = ny + inner + depth; break;
              case 2: x0 = -inner - depth; x1 = -inner; y0 = u; y1 = u + w; break;
              default: x0 = nx + inner; x1 = nx + inner + depth; y0 = u; y1 = u + w; break;
            }
            bool nearSide = side == 0 || side == 3;
            double height = nearSide ? nearBase * (0.5 + rng.NextDouble()) : farBase * (0.5 + 1.2 * rng.NextDouble());
            yield return new BoundingBox(x0, y0, 0, x1, y1, height);
          }
        }
      }
    }

    static Mesh Rect(double x0, double y0, double x1, double y1)
    {
      var m = new Mesh();
      m.Vertices.Add(x0, y0, 0);
      m.Vertices.Add(x1, y0, 0);
      m.Vertices.Add(x1, y1, 0);
      m.Vertices.Add(x0, y1, 0);
      m.Faces.AddFace(0, 1, 2, 3);
      m.Normals.ComputeNormals();
      return m;
    }

    static Line[] Dashes(Point3d[] loop, double dash)
    {
      var lines = new List<Line>();
      for (int i = 0; i < loop.Length; i++)
      {
        var a = loop[i];
        var b = loop[(i + 1) % loop.Length];
        double len = a.DistanceTo(b);
        var dir = (b - a) / len;
        for (double s = 0; s < len; s += 2 * dash)
          lines.Add(new Line(a + dir * s, a + dir * Math.Min(len, s + dash)));
      }
      return lines.ToArray();
    }

    /// <summary>Squashes a mesh onto the ground along the sun direction: cheap flat shadows.</summary>
    static Mesh Flatten(Mesh source)
    {
      var m = source.DuplicateMesh();
      for (int i = 0; i < m.Vertices.Count; i++)
      {
        var p = (Point3d)m.Vertices[i];
        var q = p - p.Z / Sun.Z * Sun;
        m.Vertices.SetVertex(i, q.X, q.Y, 0);
      }
      m.Normals.Clear();
      m.Normals.ComputeNormals();
      return m;
    }

    // Colours of the white-model look.
    static readonly DisplayMaterial GroundMat = Flat(Color.FromArgb(222, 226, 230));
    static readonly DisplayMaterial RoadMat = Flat(Color.FromArgb(200, 205, 211));
    static readonly DisplayMaterial SiteMat = Flat(Color.FromArgb(240, 242, 244));
    static readonly DisplayMaterial ShadowMat = Flat(Color.FromArgb(178, 184, 192));
    static readonly DisplayMaterial ContextMat = new DisplayMaterial(Color.FromArgb(206, 211, 217)) { Emission = Color.FromArgb(70, 72, 76) };
    static readonly DisplayMaterial MassMat = new DisplayMaterial(Color.FromArgb(240, 240, 238)) { Emission = Color.FromArgb(105, 105, 103), Specular = Color.FromArgb(20, 20, 20) };

    static DisplayMaterial Flat(Color c) => new DisplayMaterial(c) { Emission = Color.FromArgb(c.R / 3, c.G / 3, c.B / 3) };

    public void Draw(DisplayPipeline d)
    {
      d.PushModelTransform(ToWorld);
      try
      {
        // Ground layers in painter's order without depth, so nothing z-fights at z = 0.
        d.EnableDepthTesting(false);
        d.EnableDepthWriting(false);
        d.DrawMeshShaded(Ground, GroundMat);
        d.DrawMeshShaded(Roads, RoadMat);
        d.DrawMeshShaded(Site, SiteMat);
        d.DrawMeshShaded(Shadows, ShadowMat);
        d.DrawLines(SiteBoundary, Color.FromArgb(214, 52, 52), 2);
        d.EnableDepthTesting(true);
        d.EnableDepthWriting(true);

        d.DrawMeshShaded(Context, ContextMat);
        d.DrawLines(ContextEdges, Color.FromArgb(150, 157, 166), 1);
        d.DrawMeshShaded(Mass, MassMat);
        d.DrawLines(Floors, Color.FromArgb(150, 156, 164), 1);
        d.DrawLines(Creases, Color.FromArgb(40, 44, 52), 2);
      }
      finally
      {
        d.PopModelTransform();
      }
    }

    /// <summary>A two-point aerial view of the massing, as an architect would set it up.</summary>
    public void CameraFor(int nx, int ny, out Point3d location, out Point3d target)
    {
      var mb = Mass.GetBoundingBox(false);
      double top = mb.IsValid ? mb.Max.Z : 1;
      // Stand back far enough for the footprint or the height, whichever needs more.
      double f = Math.Max(Math.Max(nx, ny), top * 0.8);
      double cz = top * 0.4;
      var t = new Point3d(nx * 0.5, ny * 0.45, cz);
      var l = t + new Vector3d(5.0 * f, -4.55 * f, 3.8 * f);
      t.Transform(ToWorld);
      l.Transform(ToWorld);
      location = l;
      target = t;
    }

    /// <summary>World-space mesh of the massing, for the component's output.</summary>
    public Mesh WorldMass()
    {
      var m = Mass.DuplicateMesh();
      m.Transform(ToWorld);
      return m;
    }
  }
}
