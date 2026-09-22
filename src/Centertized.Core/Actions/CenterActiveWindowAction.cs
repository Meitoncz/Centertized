using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

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
            context.Logger.LogDebug("Přeskočeno – okno {Handle} není způsobilé (vlastní okno appky, desktop, tool window...).", hwnd);
            return Task.CompletedTask;
        }

        if (windowService.IsMinimized(hwnd))
        {
            // Nic rozumného k centrování - okno se neobnovuje, ať appka nepřekvapí
            // uživatele vyskočením okna, které si sám neschoval.
            context.Logger.LogDebug("Přeskočeno – okno {Handle} je minimalizované.", hwnd);
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
            context.Logger.LogWarning("Nepodařilo se zjistit geometrii okna {Handle}/monitoru.", hwnd);
            return Task.CompletedTask;
        }

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, visualBounds, windowRect);
        var moved = windowService.TrySetPosition(hwnd, left, top);

        if (moved)
        {
            context.Logger.LogInformation("Okno {Handle} přesunuto na ({Left}, {Top}).", hwnd, left, top);
        }
        else
        {
            // Typicky zvýšené (admin) okno - UIPI, viz CLAUDE.md. Není to bug, který
            // by šel odsud normálně "opravit".
            context.Logger.LogWarning("SetWindowPos pro okno {Handle} selhal (pravděpodobně běží se zvýšenými právy).", hwnd);
        }

        return Task.CompletedTask;
    }
}
