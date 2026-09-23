using Centertized.Core.Settings;

namespace Centertized.Core.Actions;

/// <summary>Vyřadí aplikaci aktivního okna z auto-centrování, případně ji do něj zase vrátí.</summary>
public sealed class ToggleAutoCenterForAppAction(AppRulesService rules) : IWindowAction
{
    public const string ActionId = "toggle-auto-center-for-app";

    public string Id => ActionId;

    public string DisplayName => "Toggle auto-center for this app";

    public string Description => "Excludes the app of the active window from auto-centering, or includes it again.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        var windowService = context.WindowService;
        var hwnd = windowService.GetForegroundWindowHandle();

        if (!windowService.IsEligibleForActions(hwnd))
        {
            return Task.CompletedTask;
        }

        var app = windowService.GetAppIdentity(hwnd);
        if (app is null)
        {
            rules.Announce(new AppRuleChange(AppRuleChangeKind.AppUnknown, "", "", null, null, false));
            return Task.CompletedTask;
        }

        rules.SetExcluded(app, !rules.IsExcluded(app.Key));
        return Task.CompletedTask;
    }
}
