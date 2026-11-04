using System;
using System.Collections.Generic;

namespace Moyu.Games.Stack.Core
{
  /// <summary>
  /// Bag randomiser: every piece kind goes into a bag (weight 1 = one copy; fractions are a chance of one more copy),
  /// the bag is shuffled and dealt out, then refilled. No kind can stay away for long.
  /// </summary>
  public sealed class Bag
  {
    IReadOnlyList<PieceDef> _defs;
    readonly Random _rng;
    readonly List<PieceDef> _queue = new List<PieceDef>();

    public Bag(IReadOnlyList<PieceDef> defs, Random rng)
    {
      if (defs == null || defs.Count == 0) throw new ArgumentException("The bag needs at least one piece kind.", nameof(defs));
      _defs = defs;
      _rng = rng;
    }

    /// <summary>Deal from these kinds from the next refill on (the pieces already queued stay, so the preview holds).</summary>
    public void SetPool(IReadOnlyList<PieceDef> defs)
    {
      if (defs != null && defs.Count > 0) _defs = defs;
    }

    public PieceDef Next()
    {
      var p = Peek(0);
      _queue.RemoveAt(0);
      return p;
    }

    public PieceDef Peek(int index)
    {
      while (_queue.Count <= index) Refill();
      return _queue[index];
    }

    void Refill()
    {
      var bag = new List<PieceDef>();
      foreach (var d in _defs)
      {
        int copies = (int)Math.Floor(d.Weight);
        if (_rng.NextDouble() < d.Weight - copies) copies++;
        for (int i = 0; i < copies; i++) bag.Add(d);
      }
      if (bag.Count == 0) bag.AddRange(_defs);
      for (int i = bag.Count - 1; i > 0; i--)
      {
        int j = _rng.Next(i + 1);
        var t = bag[i];
        bag[i] = bag[j];
        bag[j] = t;
      }
      _queue.AddRange(bag);
    }
  }
}
