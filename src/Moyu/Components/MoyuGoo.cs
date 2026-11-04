using Grasshopper.Kernel.Types;

namespace Moyu.Components
{
  /// <summary>Marks a <see cref="MoyuGoo{T}"/> whatever its payload type.</summary>
  public interface IMoyuGoo { }

  /// <summary>
  /// Grasshopper wrapper for the plugin's immutable data objects. The payload is not serialised, so a value
  /// internalised on a parameter reloads empty; <see cref="MoyuComponent.WrongInput"/> says so.
  /// </summary>
  public sealed class MoyuGoo<T> : GH_Goo<T>, IMoyuGoo where T : class
  {
    public MoyuGoo() { }
    public MoyuGoo(T value) : base(value) { }

    public override bool IsValid => Value != null;
    public override string TypeName => typeof(T).Name;
    public override string TypeDescription => $"Moyu {typeof(T).Name}";
    public override IGH_Goo Duplicate() => new MoyuGoo<T>(Value);
    public override string ToString() => Value?.ToString() ?? $"Null {TypeName}";

    public override bool CastTo<Q>(ref Q target)
    {
      if (Value is Q q) { target = q; return true; }
      return false;
    }

    public override bool CastFrom(object source)
    {
      if (source is T t) { Value = t; return true; }
      return false;
    }

    /// <summary>Unwraps goo, raw values and script variables into <typeparamref name="T"/>.</summary>
    public static bool TryGet(object source, out T value)
    {
      value = source as T;
      if (value != null) return true;
      if (source is MoyuGoo<T> goo) value = goo.Value;
      else if (source is IGH_Goo other) value = other.ScriptVariable() as T;
      return value != null;
    }
  }
}
