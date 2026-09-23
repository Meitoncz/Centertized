using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

/// <summary>
/// Sdílená centrovací logika - používá ji jak <see cref="CenterActiveWindowAction"/>
/// (zkratka na aktuálně popředí okno), tak <see cref="WindowManagement.NewWindowWatcher"/>
/// (centruje konkrétní nově vzniklé okno, které nemusí být "popředím" v okamžiku
/// zpracování - proto bere hwnd jako parametr, ne přes GetForegroundWindowHandle()).
/// </summary>
internal static class WindowCenterer
{
    /// <summary>
    /// <paramref name="failureLogLevel"/> - volající si volí úroveň pro "nepovedlo se"
    /// případy (geometrie/SetWindowPos). Explicitní zkratka (<see cref="CenterActiveWindowAction"/>)
    /// chce Warning (uživatel něco stiskl, selhání je užitečné vidět). Watcher nových oken
    /// naopak naráží na spoustu cizích/přechodných oken (popupy, tooltips...), kde je
    /// selhání běžné a očekávané - Warning by tam přes TrayNotificationSink spamoval
    /// tray balonky při každém otevření menu/comboboxu kdekoliv v systému.
    /// </summary>
    public static bool TryCenter(IWin32WindowService windowService, ILogger logger, IntPtr hwnd, LogLevel failureLogLevel = LogLevel.Warning)
    {
        if (windowService.IsMinimized(hwnd))
        {
            logger.LogDebug("Přeskočeno – okno {Handle} je minimalizované.", hwnd);
            return false;
        }

        if (windowService.IsMaximized(hwnd))
        {
            windowService.Restore(hwnd);
        }

        if (!windowService.TryGetVisualBounds(hwnd, out var visualBounds) ||
            !windowService.TryGetWindowRect(hwnd, out var windowRect) ||
            !windowService.TryGetMonitorWorkArea(hwnd, out var workArea))
        {
            logger.Log(failureLogLevel, "Nepodařilo se zjistit geometrii okna {Handle}/monitoru.", hwnd);
            return false;
        }

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, visualBounds, windowRect);
        var moved = windowService.TrySetPosition(hwnd, left, top);

        if (moved)
        {
            logger.LogInformation("Okno {Handle} přesunuto na ({Left}, {Top}) [visual {Visual}, rect {Rect}, work {Work}] {Description}", hwnd, left, top, visualBounds, windowRect, workArea, windowService.DescribeWindow(hwnd));
        }
        else
        {
            // Typicky zvýšené (admin) okno - UIPI, viz CLAUDE.md. Není to bug, který
            // by šel odsud normálně "opravit".
            logger.Log(failureLogLevel, "SetWindowPos pro okno {Handle} selhal (pravděpodobně běží se zvýšenými právy).", hwnd);
        }

        return moved;
    }
}
