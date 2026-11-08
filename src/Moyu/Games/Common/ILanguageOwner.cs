using Moyu.Games.Common.Core;

namespace Moyu.Games.Common
{
  /// <summary>A session's owner (a game component) that says which language the game's text starts in.</summary>
  public interface ILanguageOwner
  {
    UiLanguage Language { get; }
  }
}
