using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Moyu.Games.Snake
{
  /// <summary>Ready-made playing surfaces, each a plausible site or roof so they pass for design geometry.</summary>
  public static class SiteSurfaces
  {
    public static readonly string[] Names = { "Rolling terrain", "Torus", "Tube", "Saddle roof", "Sphere" };

    /// <param name="type">0 terrain, 1 torus, 2 tube, 3 saddle roof, 4 sphere.</param>
    /// <param name="size">Overall size in model units.</param>
    public static Brep Create(int type, double size, int seed)
    {
      switch (type)
      {
        case 1:
          {
            double r = size * 0.14, big = size * 0.36;
            return new Torus(new Plane(new Point3d(0, 0, r), Vector3d.ZAxis), big, r).ToNurbsSurface()?.ToBrep();
          }
        case 2: return Tube(size, seed);
        case 3:
          {
            double h = size * 0.18, a = size / 2;
            return NurbsSurface.CreateFromCorners(new Point3d(-a, -a, h), new Point3d(a, -a, size * 0.02), new Point3d(a, a, h), new Point3d(-a, a, size * 0.02))?.ToBrep();
          }
        case 4:
          {
            double r = size * 0.32;
            return new Sphere(new Point3d(0, 0, r), r).ToNurbsSurface()?.ToBrep();
          }
        default: return Terrain(size, seed);
      }
    }

    /// <summary>Gentle hills and a hollow over a 3 : 2 site, about 10 % of the site width high.</summary>
    static Brep Terrain(double size, int seed)
    {
      var rng = new Random(seed);
      double w = size, d = size * 2 / 3;
      const int cu = 11, cv = 8;
      var bumps = Enumerable.Range(0, 5).Select(_ => (x: rng.NextDouble(), y: rng.NextDouble(), r: 0.18 + 0.2 * rng.NextDouble(), h: (rng.NextDouble() - 0.35) * 2)).ToList();
      var points = new List<Point3d>();
      for (int i = 0; i < cu; i++)
        for (int j = 0; j < cv; j++)
        {
          double fx = (double)i / (cu - 1), fy = (double)j / (cv - 1);
          double z = 0.25 * Math.Sin(fx * 5.1 + seed) * Math.Cos(fy * 3.7);
          foreach (var b in bumps)
          {
            double dx = (fx - b.x) / b.r, dy = (fy - b.y) / b.r;
            z += b.h * Math.Exp(-(dx * dx + dy * dy));
          }
          points.Add(new Point3d((fx - 0.5) * w, (fy - 0.5) * d, z * size * 0.055));
        }
      return NurbsSurface.CreateFromPoints(points, cu, cv, 3, 3)?.ToBrep();
    }

    /// <summary>A pipe that rises and dips along an S-curve: closed round its girth, open at the two ends.</summary>
    static Brep Tube(double size, int seed)
    {
      double r = size * 0.1;
      var rail = new List<Point3d>();
      for (int i = 0; i <= 8; i++)
      {
        double f = i / 8.0;
        rail.Add(new Point3d((f - 0.5) * size * 1.2, Math.Sin(f * Math.PI * 2 + seed) * size * 0.18, r * 1.2 + (1 - Math.Cos(f * Math.PI * 2)) * size * 0.08));
      }
      var curve = Curve.CreateInterpolatedCurve(rail, 3);
      var circle = new Circle(new Plane(rail[0], curve.TangentAtStart), r).ToNurbsCurve();
      var sweep = Brep.CreateFromSweep(curve, circle, false, 1e-3 * size);
      return sweep?.FirstOrDefault();
    }
  }
}
