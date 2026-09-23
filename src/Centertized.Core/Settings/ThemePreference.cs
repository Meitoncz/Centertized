namespace Centertized.Core.Settings;

/// <summary>
/// Vlastní enum, žádná závislost na WPF-UI typech (Core je bez WPF, viz CLAUDE.md).
/// Jmenuje se "Preference", ne "ThemeMode" - to už existuje jako (experimentální)
/// System.Windows.ThemeMode a jméno by kolidovalo všude, kde je "using System.Windows".
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}
