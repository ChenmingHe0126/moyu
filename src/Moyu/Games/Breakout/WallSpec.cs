using Moyu.Games.Breakout.Core;

namespace Moyu.Games.Breakout
{
  /// <summary>What Brick Bond hands to Facade Study: a bond for every level, or a custom wall.</summary>
  public sealed class WallSpec
  {
    public readonly BondType? Bond;
    public readonly CustomWall Custom;
    public readonly int Courses;

    public WallSpec(BondType? bond, CustomWall custom, int courses)
    {
      Bond = bond;
      Custom = custom;
      Courses = courses;
    }

    public override string ToString() =>
      Custom != null ? $"Custom wall ({Custom.Rects.Count} panels)" : Bond != null ? $"{Bond} bond, {Courses} courses" : $"Bond sequence, {Courses} courses";
  }
}
