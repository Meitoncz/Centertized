using Centertized.Core.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Centertized.Services;

/// <summary>
/// Applying the theme to the whole app (resource dictionaries). It must run already at startup, not only when
/// the Settings window is created - otherwise the tray menu (which lives without a window) keeps the default light
/// look even when Windows is dark.
/// </summary>
public static class ThemeService
{
    private static ThemePreference _preference = ThemePreference.System;

    /// <summary>Whether the theme is effectively dark right now (by the choice, for "System" by Windows).</summary>
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
    /// SystemThemeWatcher needs a window; the tray-only run has none, so the system theme is watched
    /// via SystemEvents. It reacts only in "System" mode - an explicitly chosen Light/Dark doesn't change.
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
