using System.Globalization;
using System.Windows;
using Centertized.Core.Settings;

namespace Centertized.Services;

/// <summary>
/// Lokalizace přes WPF resource dictionaries. Anglický slovník je vždy načtený jako základ,
/// vybraný jazyk se přidá nad něj - chybějící překlad tak spadne na angličtinu, ne na
/// prázdný text. XAML používá {DynamicResource Klíč}, takže se jazyk mění naživo.
/// </summary>
public static class Loc
{
    private static ResourceDictionary? _overlay;

    public static event Action? LanguageChanged;

    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    public static void Apply(AppLanguage preference)
    {
        var resolved = preference == AppLanguage.System
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "cs" ? AppLanguage.Czech : AppLanguage.English)
            : preference;

        var merged = Application.Current.Resources.MergedDictionaries;

        if (!merged.Any(d => d.Source?.OriginalString.EndsWith("Strings.en.xaml", StringComparison.Ordinal) == true))
        {
            merged.Insert(0, Load("Strings.en.xaml"));
        }

        if (_overlay is not null)
        {
            merged.Remove(_overlay);
            _overlay = null;
        }

        if (resolved == AppLanguage.Czech)
        {
            _overlay = Load("Strings.cs.xaml");
            merged.Add(_overlay);
        }

        Current = resolved;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);

    private static ResourceDictionary Load(string file) =>
        new() { Source = new Uri($"pack://application:,,,/Resources/{file}", UriKind.Absolute) };
}
