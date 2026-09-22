namespace Centertized.Core.Settings;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Id akce -> kanonický textový formát Hotkey (např. "Ctrl+Alt+C").</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    public bool StartWithWindows { get; set; }
}
