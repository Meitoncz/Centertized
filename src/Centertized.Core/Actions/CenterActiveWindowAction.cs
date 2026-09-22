namespace Centertized.Core.Actions;

/// <summary>
/// V1 featura appky. Skutečná Win32 logika (DWM extended frame bounds, práce
/// s monitory, atd.) přijde ve Fázi 3 – zatím jen ověřuje, že hotkey pipeline
/// (zachycení -> registrace -> WM_HOTKEY -> dispatch) funguje od konce do konce.
/// </summary>
public sealed class CenterActiveWindowAction : IWindowAction
{
    public const string ActionId = "center-active-window";

    public string Id => ActionId;

    public string DisplayName => "Center active window";

    public string Description => "Centers the currently active window on its current monitor.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        // TODO Fáze 3: nahradit skutečným centrováním přes IWin32WindowService.
        // Zápis do souboru (ne jen Debug.WriteLine) záměrně - i uživatel bez
        // debuggeru si tak může ověřit, že zkratka doopravdy vyvolala akci.
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Centertized", "logs");
        Directory.CreateDirectory(logDir);
        File.AppendAllText(
            Path.Combine(logDir, "activity.log"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ActionId} spuštěna (Fáze 3 zatím neimplementovaná){Environment.NewLine}");
        return Task.CompletedTask;
    }
}
