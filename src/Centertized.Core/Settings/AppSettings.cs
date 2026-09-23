namespace Centertized.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Id akce -> kanonický textový formát Hotkey (např. "Ctrl+Alt+C").</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    public bool StartWithWindows { get; set; }

    public ThemePreference ThemeMode { get; set; } = ThemePreference.System;

    // Jen Mica - Acrylic byl vyřazený (uživatel si to porovnal naživo, Mica lépe
    // odpovídá tomu, jak vypadá zbytek Windows, viz TODO.md).

    /// <summary>Výchozí chování appky je ukázat Settings okno po startu - tohle to potlačí.</summary>
    public bool StartMinimized { get; set; }
}
