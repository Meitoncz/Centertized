using Centertized.Core.WindowManagement;

namespace Centertized.Core.Actions;

/// <summary>V1 featura appky – vycentruje aktivně zaměřené okno na jeho aktuálním monitoru.</summary>
public sealed class CenterActiveWindowAction : IWindowAction
{
    public const string ActionId = "center-active-window";

    public string Id => ActionId;

    public string DisplayName => "Center active window";

    public string Description => "Centers the currently active window on its current monitor.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        var windowService = context.WindowService;
        var hwnd = windowService.GetForegroundWindowHandle();

        if (!windowService.IsEligibleForActions(hwnd))
        {
            Log("přeskočeno – okno není způsobilé (vlastní okno appky, desktop, tool window...).");
            return Task.CompletedTask;
        }

        if (windowService.IsMinimized(hwnd))
        {
            // Nic rozumného k centrování - okno se neobnovuje, ať appka nepřekvapí
            // uživatele vyskočením okna, které si sám neschoval.
            Log("přeskočeno – okno je minimalizované.");
            return Task.CompletedTask;
        }

        if (windowService.IsMaximized(hwnd))
        {
            windowService.Restore(hwnd);
        }

        if (!windowService.TryGetVisualBounds(hwnd, out var visualBounds) ||
            !windowService.TryGetWindowRect(hwnd, out var windowRect) ||
            !windowService.TryGetMonitorWorkArea(hwnd, out var workArea))
        {
            Log("přeskočeno – nepodařilo se zjistit geometrii okna/monitoru.");
            return Task.CompletedTask;
        }

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, visualBounds, windowRect);
        var moved = windowService.TrySetPosition(hwnd, left, top);
        Log(moved ? $"okno přesunuto na ({left}, {top})." : "SetWindowPos selhal (např. zvýšené okno - viz CLAUDE.md).");

        return Task.CompletedTask;
    }

    private static void Log(string message)
    {
        // Dočasné - Fáze 4 nahradí Serilogem. I bez debuggeru si tak jde ověřit,
        // co akce udělala (nebo proč nic neudělala).
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Centertized", "logs");
        Directory.CreateDirectory(logDir);
        File.AppendAllText(Path.Combine(logDir, "activity.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ActionId} {message}{Environment.NewLine}");
    }
}
