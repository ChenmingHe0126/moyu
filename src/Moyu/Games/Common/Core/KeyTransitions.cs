using System.Collections.Generic;

namespace Moyu.Games.Common.Core
{
  /// <summary>
  /// Turns the keyboard hook's notifications into presses and releases, each acted on once. Windows may report one
  /// message twice (peeked, then removed) or only as peeked, and auto-repeat sends more "down" messages while a key is
  /// held. Rhino-free, so the rules are tested in Moyu.Core.Tests.
  /// </summary>
  public sealed class KeyTransitions
  {
    /// <summary>A key still marked down from a lost release counts as pressed again after this many milliseconds.</summary>
    public const long LostReleaseMs = 250;

    // Keys seen going down (and not yet up), with the time of that press.
    readonly Dictionary<int, long> _down = new Dictionary<int, long>();

    /// <summary>
    /// True exactly once per press and once per release. <paramref name="wasDown"/> is Windows' "previous key state"
    /// flag. It is not trusted for a key whose release was seen here: with an input method such as Microsoft Pinyin,
    /// Windows can flag a brand-new press as already down. The flag only tells auto-repeat (flag set) from a fresh
    /// press after a lost release (flag clear).
    /// </summary>
    public bool Fresh(int key, bool up, bool wasDown, long nowMs)
    {
      if (up) return _down.Remove(key);
      bool fresh = !_down.TryGetValue(key, out long since) || (!wasDown && nowMs - since > LostReleaseMs);
      if (fresh) _down[key] = nowMs;
      return fresh;
    }

    /// <summary>Forgets which keys are down, e.g. after the app lost focus and missed their release.</summary>
    public void Reset() => _down.Clear();
  }
}
