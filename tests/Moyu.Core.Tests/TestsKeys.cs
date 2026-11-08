using System;
using Moyu.Games.Common.Core;

namespace Moyu.Core.Tests
{
  /// <summary>How the games' keyboard hook turns Windows' key notifications into presses and releases (no Rhino needed).</summary>
  static class TestsKeys
  {
    const int Space = 0x20, Enter = 0x0D, Left = 0x25;

    public static void Run(Action<bool, string> check)
    {
      Console.WriteLine("Key transitions");
      var k = new KeyTransitions();

      // A plain tap.
      check(k.Fresh(Space, false, false, 0), "a tap's press counts");
      check(k.Fresh(Space, true, true, 60), "its release counts");

      // Held: Windows auto-repeats "down" with the previous-state flag set.
      check(k.Fresh(Space, false, false, 1000), "press before holding counts");
      check(!k.Fresh(Space, false, true, 1500) && !k.Fresh(Space, false, true, 1533) && !k.Fresh(Space, false, true, 2400),
        "auto-repeat while held does not count");
      check(k.Fresh(Space, true, true, 2450), "release after holding counts");

      // One message reported twice (peeked, then removed).
      check(k.Fresh(Enter, false, false, 3000) && !k.Fresh(Enter, false, false, 3004), "a press reported twice counts once");
      check(k.Fresh(Enter, true, true, 3080) && !k.Fresh(Enter, true, true, 3083), "a release reported twice counts once");

      // Microsoft Pinyin: a new press arrives already flagged as down although its release was seen.
      check(k.Fresh(Space, false, true, 4000), "a new press flagged as already down still counts after a seen release");
      check(k.Fresh(Space, true, true, 4060) && k.Fresh(Space, false, true, 4500), "... every time");
      k.Fresh(Space, true, true, 4560);
      var fresh = new KeyTransitions();
      check(fresh.Fresh(Space, false, true, 0), "the first press after the hook is set counts even when flagged as down");

      // A lost release (another window took the keyboard): the next press counts once the press is 250 ms old.
      check(k.Fresh(Left, false, false, 5000), "press whose release will be lost counts");
      check(!k.Fresh(Left, false, false, 5100), "a second report within 250 ms does not");
      check(k.Fresh(Left, false, false, 5400), "a new press after a lost release counts");

      // Keys are independent, and Reset forgets everything.
      check(k.Fresh(Enter, false, false, 6000) && k.Fresh(Space, false, false, 6001), "two keys pressed together both count");
      k.Reset();
      check(k.Fresh(Enter, false, true, 6100), "after Reset a held key's next report counts as a press");
      check(!k.Fresh(Space, true, true, 6150), "after Reset a release of a forgotten press does not count");
    }
  }
}
