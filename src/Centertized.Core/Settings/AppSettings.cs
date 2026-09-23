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

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Po startu appky zkontrolovat, jestli je dostupná nová verze (jen v nainstalované appce).</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    /// <summary>Jestli appka už jednou ukázala tray balonek "appka běží na pozadí".</summary>
    public bool HasShownTrayHint { get; set; }

    /// <summary>Automaticky vycentrovat každé nově otevřené okno (viz NewWindowWatcher).</summary>
    public bool AutoCenterNewWindows { get; set; }

    /// <summary>
    /// Pamatovat si velikost oken podle aplikace (učí se automaticky ze změn velikosti od uživatele)
    /// a nová okna při auto-centrování otevírat v téhle velikosti.
    /// </summary>
    public bool RememberWindowSizes { get; set; } = true;

    /// <summary>Pravidla podle aplikace, klíč = název .exe malými písmeny (viz AppIdentity).</summary>
    public Dictionary<string, AppRule> AppRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
