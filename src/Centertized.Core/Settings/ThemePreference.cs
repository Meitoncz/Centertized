namespace Centertized.Core.Settings;

/// <summary>
/// Our own enum, no dependency on WPF-UI types (Core has no WPF, see CLAUDE.md).
/// It's called "Preference", not "ThemeMode" - that already exists as the (experimental)
/// System.Windows.ThemeMode and the name would collide wherever "using System.Windows" is present.
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}
