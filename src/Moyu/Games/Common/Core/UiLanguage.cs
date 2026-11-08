using System;

namespace Moyu.Games.Common.Core
{
  /// <summary>The language of the text a game draws in the viewport. The boss-mode disguises are always English.</summary>
  public enum UiLanguage { Chinese, English }

  /// <summary>The game components' language choice (right-click › Language), as saved in a definition. Rhino-free, so it is tested in Moyu.Core.Tests.</summary>
  public static class UiLanguages
  {
    public const string Auto = "auto";

    /// <summary>
    /// Reads a saved language choice: "auto" (or nothing) follows the system, "ch" / "zh" / "cn" / "中文" is Chinese and
    /// "en" / "english" / "英文" is English, in any case; culture names such as "zh-TW" or "en-GB" work too.
    /// <paramref name="language"/> is null for auto. False for text it does not know.
    /// </summary>
    public static bool TryParse(string text, out UiLanguage? language)
    {
      language = null;
      string t = (text ?? "").Trim().ToLowerInvariant();
      if (t.Length == 0 || t == Auto) return true;
      if (t == "ch" || t == "cn" || t == "chinese" || t == "中文" || t == "中" || t == "zh" || t.StartsWith("zh-", StringComparison.Ordinal))
        language = UiLanguage.Chinese;
      else if (t == "en" || t == "eng" || t == "english" || t == "英文" || t == "英" || t.StartsWith("en-", StringComparison.Ordinal))
        language = UiLanguage.English;
      return language != null;
    }

    /// <summary>The text a choice is saved as: "auto" (null), "ch" or "en". <see cref="TryParse"/> reads it back.</summary>
    public static string Code(UiLanguage? language) =>
      language == null ? Auto : language == UiLanguage.Chinese ? "ch" : "en";

    /// <summary>The language for a display culture such as "zh-CN" or "en-US": Chinese for any Chinese, English otherwise.</summary>
    public static UiLanguage FromCulture(string cultureName) =>
      (cultureName ?? "").StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Chinese : UiLanguage.English;
  }
}
