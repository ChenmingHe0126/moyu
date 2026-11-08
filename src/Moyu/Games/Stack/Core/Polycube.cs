using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Stack.Core
{
  /// <summary>Shape helpers for sets of unit cubes: normalising, rotations, connectivity, text patterns.</summary>
  public static class Polycube
  {
    /// <summary>Distinct cells shifted so the bounding box starts at the origin, sorted z, y, x.</summary>
    public static Int3[] Normalize(IEnumerable<Int3> cells)
    {
      var list = cells.Distinct().ToList();
      if (list.Count == 0) return Array.Empty<Int3>();
      var min = new Int3(list.Min(c => c.X), list.Min(c => c.Y), list.Min(c => c.Z));
      return list.Select(c => c - min).OrderBy(c => c.Z).ThenBy(c => c.Y).ThenBy(c => c.X).ToArray();
    }

    /// <summary>Bounding box size of the cells.</summary>
    public static Int3 Size(IEnumerable<Int3> cells)
    {
      var list = cells.ToList();
      if (list.Count == 0) return Int3.Zero;
      return new Int3(list.Max(c => c.X) - list.Min(c => c.X) + 1, list.Max(c => c.Y) - list.Min(c => c.Y) + 1, list.Max(c => c.Z) - list.Min(c => c.Z) + 1);
    }

    static string Key(IEnumerable<Int3> cells) => string.Join(";", Normalize(cells).Select(c => $"{c.X},{c.Y},{c.Z}"));

    /// <summary>All distinct orientations reachable by rotation (at most 24; fewer for symmetric shapes), normalised.</summary>
    public static List<Int3[]> Orientations(IEnumerable<Int3> cells)
    {
      var start = Normalize(cells);
      var seen = new HashSet<string> { Key(start) };
      var result = new List<Int3[]> { start };
      var axes = new[] { Int3.UnitX, Int3.UnitY, Int3.UnitZ };
      for (int i = 0; i < result.Count; i++)
        foreach (var axis in axes)
        {
          var turned = Normalize(result[i].Select(c => Int3.Rotate(c, axis, true)));
          if (seen.Add(Key(turned))) result.Add(turned);
        }
      return result;
    }

    /// <summary>True when one shape can be rotated onto the other (mirror images do not count).</summary>
    public static bool SameShape(IEnumerable<Int3> a, IEnumerable<Int3> b)
    {
      var key = Key(b);
      return Orientations(a).Any(o => Key(o) == key);
    }

    /// <summary>Every cell reachable from every other through shared faces.</summary>
    public static bool IsConnected(IEnumerable<Int3> cells) => Components(cells) <= 1;

    /// <summary>Number of face-connected parts.</summary>
    public static int Components(IEnumerable<Int3> cells)
    {
      var left = new HashSet<Int3>(cells);
      int parts = 0;
      var stack = new Stack<Int3>();
      while (left.Count > 0)
      {
        parts++;
        var first = left.First();
        left.Remove(first);
        stack.Push(first);
        while (stack.Count > 0)
        {
          var c = stack.Pop();
          foreach (var n in Neighbours(c))
            if (left.Remove(n)) stack.Push(n);
        }
      }
      return parts;
    }

    public static IEnumerable<Int3> Neighbours(Int3 c)
    {
      yield return c + Int3.UnitX;
      yield return c - Int3.UnitX;
      yield return c + Int3.UnitY;
      yield return c - Int3.UnitY;
      yield return c + Int3.UnitZ;
      yield return c - Int3.UnitZ;
    }

    /// <summary>All cells share one coordinate, so the shape also exists in flat falling-block games.</summary>
    public static bool IsPlanar(IEnumerable<Int3> cells)
    {
      var s = Size(cells);
      return s.X == 1 || s.Y == 1 || s.Z == 1;
    }

    /// <summary>
    /// Parses a text pattern: layers bottom-up separated by '|', rows within a layer separated by '/' or new lines
    /// (a '/' followed by a new line counts once; row index → y, character index → x). '#', 'x', 'X', 'o', 'O', '1'
    /// and '█' are filled; anything else, a space or a blank row included, is empty.
    /// </summary>
    public static Int3[] ParsePattern(string pattern)
    {
      var cells = new List<Int3>();
      if (string.IsNullOrWhiteSpace(pattern)) return Array.Empty<Int3>();
      // A line break right after a '/' only lays the text out ("###/\n#.#" is two rows, not three); '//' or two
      // line breaks still give a blank row.
      var layers = pattern.Replace("\r\n", "\n").Replace('\r', '\n').Replace("/\n", "/").Split('|');
      for (int z = 0; z < layers.Length; z++)
      {
        // Line breaks next to a '|' only lay the text out. Blank rows inside a layer and leading spaces are empty
        // cells, so they are kept: trimming them would shift a row and give another shape. Normalize drops any margin.
        var rows = layers[z].Trim('\n').Split('/', '\n');
        for (int y = 0; y < rows.Length; y++)
        {
          var row = rows[y];
          for (int x = 0; x < row.Length; x++)
            if ("#xXoO1█".IndexOf(row[x]) >= 0) cells.Add(new Int3(x, y, z));
        }
      }
      return Normalize(cells);
    }
  }
}
