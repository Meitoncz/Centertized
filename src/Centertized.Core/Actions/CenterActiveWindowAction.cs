using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

/// <summary>The app's v1 feature – centers the active (focused) window on its current monitor.</summary>
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
            context.Logger.LogDebug("Skipped – window {Handle} is not eligible (the app's own window, desktop, tool window...).", hwnd);
            return Task.CompletedTask;
        }

        WindowCenterer.TryCenter(windowService, context.Logger, hwnd);
        return Task.CompletedTask;
    }
}
