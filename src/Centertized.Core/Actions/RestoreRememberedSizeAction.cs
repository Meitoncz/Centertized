using Centertized.Core.Settings;
using Centertized.Core.WindowManagement;

namespace Centertized.Core.Actions;

/// <summary>Nastaví aktivnímu oknu zapamatovanou velikost jeho aplikace a vycentruje ho.</summary>
public sealed class RestoreRememberedSizeAction(AppRulesService rules) : IWindowAction
{
    public const string ActionId = "restore-remembered-size";

    public string Id => ActionId;

    public string DisplayName => "Restore remembered size";

    public string Description => "Resizes the active window to the size remembered for its app and centers it.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        var windowService = context.WindowService;
        var hwnd = windowService.GetForegroundWindowHandle();

        if (!windowService.IsEligibleForActions(hwnd) || windowService.IsMinimized(hwnd))
        {
            return Task.CompletedTask;
        }

        var app = windowService.GetAppIdentity(hwnd);
        if (app is null || !rules.TryGetRememberedSize(app.Key, out _, out _))
        {
            rules.Announce(new AppRuleChange(
                app is null ? AppRuleChangeKind.AppUnknown : AppRuleChangeKind.NothingRemembered,
                app?.Key ?? "", app?.DisplayName ?? "", null, null, false));
            return Task.CompletedTask;
        }

        if (windowService.IsMaximized(hwnd))
        {
            windowService.Restore(hwnd);
        }

        // Politika bez ohledu na globální přepínač "Apply remembered sizes" - uživatel si
        // tuhle akci vyžádal výslovně.
        var policy = new RememberedSizePolicy(rules, windowService, () => true);
        WindowCenterer.TryCenter(windowService, context.Logger, hwnd, sizePolicy: policy);
        return Task.CompletedTask;
    }
}
