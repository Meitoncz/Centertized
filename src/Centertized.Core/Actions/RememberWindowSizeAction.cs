using Centertized.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

/// <summary>
/// Zapamatuje aktuální velikost aktivního okna pro jeho aplikaci - příště se nově otevřená
/// okna téhle appky při auto-centrování otevřou v téhle velikosti.
/// </summary>
public sealed class RememberWindowSizeAction(AppRulesService rules) : IWindowAction
{
    public const string ActionId = "remember-window-size";

    public string Id => ActionId;

    public string DisplayName => "Remember window size";

    public string Description => "Remembers the size of the active window for its app; new windows of that app open in this size.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        var windowService = context.WindowService;
        var hwnd = windowService.GetForegroundWindowHandle();

        if (!windowService.IsEligibleForActions(hwnd) || windowService.IsMinimized(hwnd) || windowService.IsMaximized(hwnd))
        {
            context.Logger.LogDebug("Zapamatování velikosti přeskočeno - okno {Handle} není způsobilé.", hwnd);
            return Task.CompletedTask;
        }

        var app = windowService.GetAppIdentity(hwnd);
        if (app is null || !windowService.TryGetWindowRect(hwnd, out var rect))
        {
            rules.Announce(new AppRuleChange(AppRuleChangeKind.AppUnknown, "", "", null, null, false));
            return Task.CompletedTask;
        }

        var dpi = windowService.GetDpi(hwnd);
        var width96 = (int)Math.Round(rect.Width * 96.0 / dpi);
        var height96 = (int)Math.Round(rect.Height * 96.0 / dpi);
        rules.SetRememberedSize(app, width96, height96);
        context.Logger.LogInformation("Zapamatována velikost {Width}x{Height} pro {App}.", width96, height96, app.Key);
        return Task.CompletedTask;
    }
}
