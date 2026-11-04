using System;

namespace Moyu.Games.Stack.Core
{
  /// <summary>Integer grid coordinate; z is up.</summary>
  public readonly struct Int3 : IEquatable<Int3>
  {
    public readonly int X, Y, Z;

    public Int3(int x, int y, int z)
    {
      X = x;
      Y = y;
      Z = z;
    }

    public static readonly Int3 Zero = new Int3(0, 0, 0);
    public static readonly Int3 UnitX = new Int3(1, 0, 0);
    public static readonly Int3 UnitY = new Int3(0, 1, 0);
    public static readonly Int3 UnitZ = new Int3(0, 0, 1);
    public static readonly Int3 Down = new Int3(0, 0, -1);

    public static Int3 operator +(Int3 a, Int3 b) => new Int3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new Int3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator -(Int3 a) => new Int3(-a.X, -a.Y, -a.Z);
    public static Int3 operator *(int s, Int3 a) => new Int3(s * a.X, s * a.Y, s * a.Z);
    public static bool operator ==(Int3 a, Int3 b) => a.Equals(b);
    public static bool operator !=(Int3 a, Int3 b) => !a.Equals(b);

    public static int Dot(Int3 a, Int3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Int3 Cross(Int3 a, Int3 b) => new Int3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Quarter turn about an axis-aligned unit axis, right-handed when <paramref name="positive"/> (Rodrigues with cos 0, sin ±1).</summary>
    public static Int3 Rotate(Int3 v, Int3 axis, bool positive)
    {
      var c = Cross(axis, v);
      var along = Dot(axis, v) * axis;
      return positive ? along + c : along - c;
    }

    public bool Equals(Int3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object obj) => obj is Int3 o && Equals(o);
    public override int GetHashCode() => unchecked((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791));
    public override string ToString() => $"({X}, {Y}, {Z})";
  }
}
