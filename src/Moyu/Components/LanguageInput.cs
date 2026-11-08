using Grasshopper.Kernel;
using Moyu.Games.Common;
using Moyu.Games.Common.Core;

namespace Moyu.Components
{
  /// <summary>
  /// The Language input every game component has as its last input (last, so definitions saved before it existed
  /// still open with their wires in place).
  /// </summary>
  static class LanguageInput
  {
    public const string Name = "Language", Nickname = "Lang",
      Description = "Language of the text the game draws in the viewport: auto (default) follows the Windows display language, " +
        "ch is 中文 and en is English. The boss-key screens are always English.";

    /// <summary>The input's choice (null for auto). Unknown text gets a warning and counts as auto.</summary>
    public static UiLanguage? Read(GH_Component component, IGH_DataAccess da, int index)
    {
      string text = UiLanguages.Auto;
      da.GetData(index, ref text);
      if (UiLanguages.TryParse(text, out var choice)) return choice;
      component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Language \"{text}\" is not known; use auto, ch or en. Following the system for now.");
      return null;
    }

    /// <summary>Gives a running game the input's language; a change shows on the next frame.</summary>
    public static void Apply(GameSession session, UiLanguage? choice)
    {
      if (session != null) session.Language = GameSession.Resolve(choice);
    }
  }
}
