using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

/// <summary>
/// Shared centering logic - used both by <see cref="CenterActiveWindowAction"/>
/// (the shortcut for the current foreground window) and by <see cref="WindowManagement.NewWindowWatcher"/>
/// (centers a specific newly created window that may not be the "foreground" one at the moment
/// of processing - hence it takes the hwnd as a parameter, not via GetForegroundWindowHandle()).
/// </summary>
internal static class WindowCenterer
{
    /// <summary>
    /// <paramref name="failureLogLevel"/> - the caller chooses the level for the "it failed"
    /// cases (geometry/SetWindowPos). The explicit shortcut (<see cref="CenterActiveWindowAction"/>)
    /// wants Warning (the user pressed something, a failure is useful to see). The new-window watcher
    /// instead runs into lots of foreign/transient windows (popups, tooltips...) where a
    /// failure is common and expected - a Warning there would spam tray balloons via TrayNotificationSink
    /// on every menu/combobox opened anywhere in the system.
    /// </summary>
    public static bool TryCenter(IWin32WindowService windowService, ILogger logger, IntPtr hwnd, LogLevel failureLogLevel = LogLevel.Warning, IWindowSizePolicy? sizePolicy = null)
    {
        if (windowService.IsMinimized(hwnd))
        {
            logger.LogDebug("Skipped – window {Handle} is minimized.", hwnd);
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
            logger.Log(failureLogLevel, "Could not get the geometry of window {Handle}/monitor.", hwnd);
            return false;
        }

        int left, top;
        bool moved;
        if (sizePolicy is not null &&
            sizePolicy.TryGetTargetSize(hwnd, out var targetWidth, out var targetHeight) &&
            (targetWidth != windowRect.Width || targetHeight != windowRect.Height))
        {
            // Size and position in one SetWindowPos - the window doesn't flicker in its old size first.
            var bounds = WindowCenteringCalculator.CalculateResized(workArea, visualBounds, windowRect, targetWidth, targetHeight);
            (left, top) = (bounds.Left, bounds.Top);
            moved = windowService.TrySetBounds(hwnd, bounds);
        }
        else
        {
            (left, top) = WindowCenteringCalculator.Calculate(workArea, visualBounds, windowRect);
            moved = windowService.TrySetPosition(hwnd, left, top);
        }

        if (moved)
        {
            logger.LogInformation("Window {Handle} moved to ({Left}, {Top}) [visual {Visual}, rect {Rect}, work {Work}] {Description}", hwnd, left, top, visualBounds, windowRect, workArea, windowService.DescribeWindow(hwnd));
        }
        else
        {
            // Typically an elevated (admin) window - UIPI, see CLAUDE.md. It's not a bug that
            // could normally be "fixed" from here.
            logger.Log(failureLogLevel, "SetWindowPos for window {Handle} failed (it probably runs elevated).", hwnd);
        }

        return moved;
    }
}
