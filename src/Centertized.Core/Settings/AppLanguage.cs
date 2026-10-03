namespace Centertized.Core.Settings;

/// <summary>
/// UI language. System = by the Windows language (falls back to English when we have no translation).
/// Values are stored in settings.json by name, so adding new ones never breaks existing files.
/// </summary>
public enum AppLanguage
{
    System,
    English,
    Czech,
    German,
    Spanish,
    French,
    Italian,
    Polish,
    Portuguese,
    Ukrainian,
    Japanese,
    Korean,
    Chinese,
}
