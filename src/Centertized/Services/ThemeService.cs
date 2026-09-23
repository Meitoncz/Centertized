using Centertized.Core.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Centertized.Services;

/// <summary>
/// Aplikace motivu na celou appku (resource dictionaries). Musí běžet už při startu, ne až při
/// vytvoření okna Nastavení - jinak má tray menu (které žije bez okna) pořád výchozí světlý
/// vzhled, i když je Windows tmavé.
/// </summary>
public static class ThemeService
{
    private static ThemePreference _preference = ThemePreference.System;

    /// <summary>Jestli je právě efektivně tmavý motiv (podle volby, u "System" podle Windows).</summary>
    public static bool IsDark => _preference switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark,
    };

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;

        var theme = preference switch
        {
            ThemePreference.Light => ApplicationTheme.Light,
            ThemePreference.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
    }

    /// <summary>
    /// SystemThemeWatcher potřebuje okno; tray-only běh ho nemá, takže systémový motiv sledujeme
    /// přes SystemEvents. Reaguje jen v režimu "System" - natvrdo zvolený Light/Dark se nemění.
    /// </summary>
    public static void WatchSystemTheme()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _preference == ThemePreference.System)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Apply(ThemePreference.System));
            }
        };
    }
}
