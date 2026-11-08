using System;
using Moyu.Games.Common.Core;

namespace Moyu.Core.Tests
{
  /// <summary>The game components' Language input (no Rhino needed).</summary>
  static class TestsLanguage
  {
    public static void Run(Action<bool, string> check)
    {
      Console.WriteLine("Language input");
      bool Is(string text, UiLanguage? expected) => UiLanguages.TryParse(text, out var got) && got == expected;

      check(Is(null, null) && Is("", null) && Is("  ", null) && Is("auto", null) && Is("AUTO", null), "nothing or auto follows the system");
      check(Is("ch", UiLanguage.Chinese) && Is("CH", UiLanguage.Chinese) && Is("zh", UiLanguage.Chinese) && Is("cn", UiLanguage.Chinese), "ch / zh / cn is Chinese");
      check(Is("中文", UiLanguage.Chinese) && Is("Chinese", UiLanguage.Chinese) && Is(" zh-TW ", UiLanguage.Chinese), "中文, Chinese and culture names are Chinese");
      check(Is("en", UiLanguage.English) && Is("English", UiLanguage.English) && Is("英文", UiLanguage.English) && Is("en-GB", UiLanguage.English), "en / English / 英文 / en-GB is English");
      check(!UiLanguages.TryParse("fr", out _) && !UiLanguages.TryParse("chn", out _) && !UiLanguages.TryParse("english please", out _), "unknown text is refused");

      check(UiLanguages.FromCulture("zh-CN") == UiLanguage.Chinese && UiLanguages.FromCulture("zh-Hant-TW") == UiLanguage.Chinese, "a Chinese Windows shows Chinese");
      check(UiLanguages.FromCulture("en-US") == UiLanguage.English && UiLanguages.FromCulture("de-DE") == UiLanguage.English, "English, and any other language, shows English");
      check(UiLanguages.FromCulture(null) == UiLanguage.English && UiLanguages.FromCulture("") == UiLanguage.English, "no culture shows English");
    }
  }
}
