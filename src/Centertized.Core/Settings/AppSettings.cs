namespace Centertized.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Action id -> canonical text format of the Hotkey (e.g. "Ctrl+Alt+C").</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    public bool StartWithWindows { get; set; }

    public ThemePreference ThemeMode { get; set; } = ThemePreference.System;

    // Mica only - Acrylic was dropped (the user compared them live, Mica matches
    // how the rest of Windows looks better, see TODO.md).

    /// <summary>By default the app shows the Settings window after start - this suppresses that.</summary>
    public bool StartMinimized { get; set; }

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>
    /// The app runs with elevated rights (UAC) so it can also move windows of apps run as
    /// administrator (Windows forbids that to an unprivileged process - UIPI).
    /// </summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>After the app starts, check whether a new version is available (installed app only).</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    /// <summary>Whether the app has already shown the "the app runs in the background" tray balloon once.</summary>
    public bool HasShownTrayHint { get; set; }

    /// <summary>Automatically center every newly opened window (see NewWindowWatcher).</summary>
    public bool AutoCenterNewWindows { get; set; }

    /// <summary>
    /// Remember window sizes per app (learned automatically from the user's resizing)
    /// and open new windows in that size when auto-centering.
    /// </summary>
    public bool RememberWindowSizes { get; set; } = true;

    /// <summary>Per-app rules, key = lowercase .exe name (see AppIdentity).</summary>
    public Dictionary<string, AppRule> AppRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
