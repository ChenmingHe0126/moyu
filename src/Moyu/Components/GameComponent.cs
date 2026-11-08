using System;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Moyu.Games.Common;
using Moyu.Games.Common.Core;

namespace Moyu.Components
{
  /// <summary>
  /// Base for the four game components (Massing, Path, Facade and Airflow Study): the Run button, and the language of
  /// the text the game draws, picked from the component's right-click menu (Auto, 中文 or English) and saved with the
  /// definition. Auto, the default, follows the Windows display language.
  /// </summary>
  public abstract class GameComponent : MoyuComponent, IRunnable, ILanguageOwner
  {
    const string LanguageKey = "MoyuLanguage";

    /// <summary>The right-click menu's choice; null is Auto (follows the Windows display language).</summary>
    UiLanguage? _language;

    protected GameComponent(string name, string nickname, string description, string subCategory)
      : base(name, nickname, description, subCategory) { }

    public override void CreateAttributes() => m_attributes = new RunButtonAttributes(this);

    public bool IsRunning => GameSession.Current != null && ReferenceEquals(GameSession.Current.Owner, this);

    public abstract void Run();

    /// <summary>The language the game's text is drawn in: the menu's choice, else the system's. A session reads it as it starts.</summary>
    public UiLanguage Language => GameSession.Resolve(_language);

    /// <summary>Called after the menu changed the language (the running game, if any, already switched).</summary>
    protected virtual void OnLanguageChanged() { }

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
      base.AppendAdditionalComponentMenuItems(menu);
      Menu_AppendSeparator(menu);
      var root = Menu_AppendItem(menu, "Language · 语言");
      root.ToolTipText = "Language of the text the game draws in the viewport. The boss-key screens are always English.";
      string system = GameSession.SystemLanguage() == UiLanguage.Chinese ? "中文" : "English";
      Menu_AppendItem(root.DropDown, $"Auto · 自动 (Windows: {system})", (s, e) => SetLanguage(null), true, _language == null);
      Menu_AppendItem(root.DropDown, "中文", (s, e) => SetLanguage(UiLanguage.Chinese), true, _language == UiLanguage.Chinese);
      Menu_AppendItem(root.DropDown, "English", (s, e) => SetLanguage(UiLanguage.English), true, _language == UiLanguage.English);
    }

    void SetLanguage(UiLanguage? choice)
    {
      if (_language == choice) return;
      RecordUndoEvent("Language");
      _language = choice;
      // Saved with the definition, so the document has unsaved changes now (a running game may not solve anything).
      OnPingDocument()?.Modified();
      if (IsRunning) GameSession.Current.Language = Language;
      OnLanguageChanged();
    }

    public override bool Write(GH_IWriter writer)
    {
      writer.SetString(LanguageKey, UiLanguages.Code(_language));
      return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
      _language = null;
      if (reader.ItemExists(LanguageKey))
      {
        if (UiLanguages.TryParse(reader.GetString(LanguageKey), out var choice)) _language = choice;
      }
      else _language = OldLanguageInput(reader);
      // Undo / redo of the Language menu restores this object through Read: the running game follows. (A loaded or
      // pasted copy never owns the game; the undo's own solution refreshes the outputs.)
      if (IsRunning) GameSession.Current.Language = Language;
      return base.Read(reader);
    }

    /// <summary>
    /// The language typed into the Language input that a short-lived build (423f3da) added as the last input; the
    /// menu replaced it, and Grasshopper skips that input's saved chunk without a word. Null (Auto) when the
    /// definition has no such input, the input was wired, or its text cannot be read.
    /// </summary>
    UiLanguage? OldLanguageInput(GH_IReader reader)
    {
      try
      {
        var input = reader.FindChunk("param_input", Params.Input.Count);
        if (input == null || !input.ItemExists("Name") || input.GetString("Name") != "Language") return null;
        if (input.ItemExists("SourceCount") && input.GetInt32("SourceCount") > 0)
        {
          // The wired value lives in another object, and the wire itself is gone.
          Rhino.RhinoApp.WriteLine($"{Name}: the Language input is now in the component's right-click menu; its wire was removed.");
          return null;
        }
        var item = input.FindChunk("PersistentData")?.FindChunk("Branch", 0)?.FindChunk("Item", 0);
        if (item == null || !item.ItemExists("string")) return null;
        return UiLanguages.TryParse(item.GetString("string"), out var choice) ? choice : null;
      }
      catch (Exception)
      {
        // An odd old chunk must never stop the definition opening.
        return null;
      }
    }
  }
}
