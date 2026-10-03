using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

/// <summary>
/// Phase 5 – the second real action (an idea from IDEAS.md) that verifies that adding
/// a feature really takes just a new class + one line in the catalog (see CLAUDE.md).
/// Maximizes the active window; when run again on a window that the app itself
/// maximized, it restores it to its previous position and size.
/// </summary>
public sealed class ToggleMaximizeAction : IWindowAction
{
    public const string ActionId = "toggle-maximize-active-window";

    // Keyed by HWND – the app runs as a single instance and the state doesn't survive a
    // restart, so a simple in-memory dictionary is enough (no persistence).
    private readonly Dictionary<IntPtr, WindowRect> _boundsBeforeMaximize = new();

    public string Id => ActionId;

    public string DisplayName => "Toggle maximize active window";

    public string Description => "Maximizes the active window; pressing it again restores the previous size and position.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        var windowService = context.WindowService;
        var hwnd = windowService.GetForegroundWindowHandle();

        if (!windowService.IsEligibleForActions(hwnd))
        {
            context.Logger.LogDebug("Skipped – window {Handle} is not eligible.", hwnd);
            return Task.CompletedTask;
        }

        if (windowService.IsMaximized(hwnd))
        {
            windowService.Restore(hwnd);

            if (_boundsBeforeMaximize.Remove(hwnd, out var previousBounds))
            {
                windowService.TrySetBounds(hwnd, previousBounds);
            }
            // If it isn't in the map (the app "didn't see" the maximization, e.g. it happened another way
            // than through this action), we leave the window at its default restored size/position.

            context.Logger.LogInformation("Window {Handle} restored to its previous position/size.", hwnd);
        }
        else
        {
            if (windowService.TryGetWindowRect(hwnd, out var currentBounds))
            {
                _boundsBeforeMaximize[hwnd] = currentBounds;
            }

            windowService.Maximize(hwnd);
            context.Logger.LogInformation("Window {Handle} maximized.", hwnd);
        }

        return Task.CompletedTask;
    }
}
