using Centertized.Core.Settings;

namespace Centertized.Core.WindowManagement;

/// <summary>Size according to the app's rules (<see cref="AppRulesService"/>), converted to the window's DPI.</summary>
public sealed class RememberedSizePolicy(AppRulesService rules, IWin32WindowService windowService, Func<bool> isEnabled) : IWindowSizePolicy
{
    public bool TryGetTargetSize(IntPtr windowHandle, out int widthPixels, out int heightPixels)
    {
        widthPixels = heightPixels = 0;

        if (!isEnabled() || !windowService.IsResizable(windowHandle))
        {
            return false;
        }

        var app = windowService.GetAppIdentity(windowHandle);
        if (app is null || !rules.TryGetRememberedSize(app.Key, out var width96, out var height96))
        {
            return false;
        }

        var dpi = windowService.GetDpi(windowHandle);
        widthPixels = (int)Math.Round(width96 * dpi / 96.0);
        heightPixels = (int)Math.Round(height96 * dpi / 96.0);
        return true;
    }
}
