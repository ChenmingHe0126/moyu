using System;
using System.Collections.Generic;
using System.Linq;

namespace Moyu.Games.Stack.Core
{
  /// <summary>One kind of falling piece: its cubes, display colour, share of the bag and score multiplier.</summary>
  public sealed class PieceDef
  {
    /// <summary>Validation limits for custom pieces.</summary>
    public const int MaxCells = 10, MaxExtent = 4;
    /// <summary>Most copies of one kind per bag; the bag holds that many entries, so a huge weight would stall the game.</summary>
    public const double MaxWeight = 20;

    public readonly string Name;
    /// <summary>Normalised cells (bounding box starts at the origin).</summary>
    public readonly Int3[] Cells;
    /// <summary>ARGB colour of the falling piece and its previews; never 0, which the well reads as an empty cell.</summary>
    public readonly int Colour;
    /// <summary>Copies per bag: 1 = once per bag, 0.5 = every other bag on average.</summary>
    public readonly double Weight;
    public readonly bool IsCustom;

    public PieceDef(string name, IEnumerable<Int3> cells, int colour, double weight = 1.0, bool isCustom = false)
    {
      Name = name;
      Cells = Polycube.Normalize(cells);
      if (Cells.Length == 0) throw new ArgumentException("A piece needs at least one cell.", nameof(cells));
      Colour = colour == 0 ? unchecked((int)0xFF000000) : colour;
      // NaN → 0; infinity and huge values → MaxWeight (past 2^31 the copy count would also overflow to none).
      Weight = weight > 0 ? Math.Min(weight, MaxWeight) : 0;
      IsCustom = isCustom;
    }

    public Int3 Size => Polycube.Size(Cells);
    public bool IsPlanar => Polycube.IsPlanar(Cells);
    public int OrientationCount => Polycube.Orientations(Cells).Count;

    /// <summary>Custom pieces earn more the bigger they are (harder to place); non-planar ones get another 0.1.</summary>
    public double ScoreMultiplier
    {
      get
      {
        if (!IsCustom) return 1.0;
        int n = Cells.Length;
        double m = n <= 4 ? 1.0 : n == 5 ? 1.2 : n == 6 ? 1.5 : 2.0;
        return IsPlanar ? m : m + 0.1;
      }
    }

    public PieceDef WithWeight(double weight) => new PieceDef(Name, Cells, Colour, weight, IsCustom);

    /// <summary>Why a custom piece cannot be played in a well of the given size, or null when it can.</summary>
    public static string Validate(IReadOnlyCollection<Int3> cells, int wellX, int wellY, int wellZ)
    {
      if (cells.Count == 0) return "The unit has no voxels.";
      if (cells.Count > MaxCells) return $"The unit has {cells.Count} voxels; the most a piece may have is {MaxCells}.";
      int parts = Polycube.Components(cells);
      if (parts > 1) return $"The unit falls apart into {parts} pieces; voxels must touch face to face (edges and corners do not count).";
      var s = Polycube.Size(cells);
      int longest = Math.Max(s.X, Math.Max(s.Y, s.Z));
      if (longest > MaxExtent) return $"The unit is {s.X}×{s.Y}×{s.Z} voxels; no side may exceed {MaxExtent}.";
      if (longest > Math.Min(wellX, wellY)) return $"The unit is {longest} voxels long and cannot turn inside a {wellX}×{wellY} well.";
      if (longest > wellZ - 2) return $"The well is too shallow ({wellZ}) for a unit {longest} voxels long.";
      return null;
    }

    public override string ToString() => $"{Name} ({Cells.Length} voxels)";
  }

  /// <summary>The built-in piece sets, named after building types.</summary>
  public static class PieceSets
  {
    public const int Easy = 0, Standard = 1, Hard = 2;
    public static readonly string[] Names = { "Easy", "Standard", "Hard" };

    static Int3 C(int x, int y, int z = 0) => new Int3(x, y, z);
    static PieceDef P(string name, uint colour, params Int3[] cells) => new PieceDef(name, cells, unchecked((int)colour));

    public static readonly PieceDef I3 = P("I3 短板", 0xFFFFD166, C(0, 0), C(1, 0), C(2, 0));
    public static readonly PieceDef L3 = P("L3 小转角", 0xFF06D6A0, C(0, 0), C(1, 0), C(0, 1));
    public static readonly PieceDef I4 = P("I4 板楼", 0xFFFFB547, C(0, 0), C(1, 0), C(2, 0), C(3, 0));
    public static readonly PieceDef O4 = P("O 点式", 0xFF46D39A, C(0, 0), C(1, 0), C(0, 1), C(1, 1));
    public static readonly PieceDef L4 = P("L 翼", 0xFFFF7A45, C(0, 0), C(1, 0), C(2, 0), C(2, 1));
    public static readonly PieceDef T4 = P("T 翼", 0xFFC77DFF, C(0, 0), C(1, 0), C(2, 0), C(1, 1));
    public static readonly PieceDef S4 = P("S 错层", 0xFF4CC9F0, C(0, 0), C(1, 0), C(1, 1), C(2, 1));
    public static readonly PieceDef SpiralL = P("螺旋左", 0xFFF15BB5, C(0, 1), C(1, 1), C(1, 0), C(1, 0, 1));
    public static readonly PieceDef SpiralR = P("螺旋右", 0xFF8AC926, C(0, 0), C(1, 0), C(1, 1), C(1, 1, 1));
    public static readonly PieceDef Tripod = P("三脚转角", 0xFF6C8CFF, C(0, 0), C(1, 0), C(0, 1), C(0, 0, 1));
    public static readonly PieceDef Cross = P("十字", 0xFFEF476F, C(1, 0), C(0, 1), C(1, 1), C(2, 1), C(1, 2));
    public static readonly PieceDef U5 = P("合院 U", 0xFFFF9F1C, C(0, 0), C(2, 0), C(0, 1), C(1, 1), C(2, 1));
    public static readonly PieceDef V5 = P("大转角 V", 0xFF2EC4B6, C(0, 0), C(1, 0), C(2, 0), C(0, 1), C(0, 2));
    public static readonly PieceDef W5 = P("阶梯 W", 0xFFE9C46A, C(0, 0), C(1, 0), C(1, 1), C(2, 1), C(2, 2));
    public static readonly PieceDef Tower = P("塔楼", 0xFFB388EB, C(0, 0), C(1, 0), C(0, 1), C(0, 0, 1), C(0, 0, 2));

    public static IReadOnlyList<PieceDef> Get(int preset)
    {
      switch (preset)
      {
        case Easy: return new[] { I3, L3, I4, O4, L4, T4, S4 };
        case Hard: return new[] { I4, O4, L4, T4, S4, SpiralL, SpiralR, Tripod, Cross, U5, V5, W5, Tower };
        default: return new[] { I4, O4, L4, T4, S4, SpiralL, SpiralR, Tripod };
      }
    }
  }
}
