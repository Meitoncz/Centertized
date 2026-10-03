using System.Globalization;
using System.Windows;
using Centertized.Core.Settings;

namespace Centertized.Services;

/// <summary>A language we ship: its enum value, the dictionary file suffix, the Windows language code and its own name.</summary>
public sealed record LanguageInfo(AppLanguage Language, string Code, string NativeName);

/// <summary>
/// Localization via WPF resource dictionaries. The English dictionary is always loaded as the base,
/// the chosen language is added on top of it - a missing translation then falls back to English, not to
/// empty text. XAML uses {DynamicResource Key}, so the language changes live.
/// To add a language: add a value to <see cref="AppLanguage"/>, a row to <see cref="Languages"/> and a
/// <c>Strings.&lt;code&gt;.xaml</c> dictionary (the key-parity test guards the dictionary).
/// </summary>
public static class Loc
{
    /// <summary>Shipped languages in the order shown in the picker (names are written in the language itself).</summary>
    public static IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new(AppLanguage.English, "en", "English"),
        new(AppLanguage.Czech, "cs", "Čeština"),
        new(AppLanguage.German, "de", "Deutsch"),
        new(AppLanguage.Spanish, "es", "Español"),
        new(AppLanguage.French, "fr", "Français"),
        new(AppLanguage.Italian, "it", "Italiano"),
        new(AppLanguage.Polish, "pl", "Polski"),
        new(AppLanguage.Portuguese, "pt", "Português (Brasil)"),
        new(AppLanguage.Ukrainian, "uk", "Українська"),
        new(AppLanguage.Japanese, "ja", "日本語"),
        new(AppLanguage.Korean, "ko", "한국어"),
        new(AppLanguage.Chinese, "zh", "简体中文"),
    ];

    private static ResourceDictionary? _overlay;

    public static event Action? LanguageChanged;

    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    /// <summary>The language "System" resolves to: the Windows UI language when we have it, otherwise English.</summary>
    public static AppLanguage ResolveSystemLanguage(CultureInfo? culture = null)
    {
        var twoLetter = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        return Languages.FirstOrDefault(l => l.Code == twoLetter)?.Language ?? AppLanguage.English;
    }

    public static void Apply(AppLanguage preference)
    {
        var resolved = preference == AppLanguage.System ? ResolveSystemLanguage() : preference;

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

        var info = Languages.FirstOrDefault(l => l.Language == resolved);
        if (info is not null && resolved != AppLanguage.English)
        {
            _overlay = Load($"Strings.{info.Code}.xaml");
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
