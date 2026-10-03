using Centertized.Core.Settings;
using Microsoft.Extensions.Logging;
using static Centertized.Core.WindowManagement.NativeMethods;

namespace Centertized.Core.WindowManagement;

/// <summary>
/// Automatically remembers the size the user gave an app's windows: when a move/resize finishes
/// (EVENT_SYSTEM_MOVESIZEEND) the window's size is stored for its app.
/// New windows of that app are then (via <see cref="RememberedSizePolicy"/>) opened in the same size.
/// Just a notification hook like <see cref="NewWindowWatcher"/> - it blocks and suppresses nothing.
/// </summary>
public sealed class WindowSizeLearner : IDisposable
{
    // A smaller window is almost certainly a dialog/popup, not a working window whose size is worth remembering.
    private const int MinimumWidth = 240;
    private const int MinimumHeight = 160;

    // With Aero Snap (half/third of the screen) the system "snaps" the window and MOVESIZEEND fires too -
    // we don't want to remember such a size, it would then become the window's "normal" size.
    private const int SnapTolerancePixels = 24;

    private readonly IWin32WindowService _windowService;
    private readonly AppRulesService _rules;
    private readonly ILogger _logger;
    private readonly WinEventDelegate _callback;
    private IntPtr _hookHandle;

    public WindowSizeLearner(IWin32WindowService windowService, AppRulesService rules, ILogger logger)
    {
        _windowService = windowService;
        _rules = rules;
        _logger = logger;
        _callback = OnWinEvent;
    }

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (!IsRunning)
        {
            _hookHandle = SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
    }

    public void Stop()
    {
        if (IsRunning)
        {
            UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero)
            {
                return;
            }

            Learn(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remembering the size of window {Handle} failed.", hwnd);
        }
    }

    private void Learn(IntPtr hwnd)
    {
        if (!_windowService.IsEligibleForActions(hwnd) || !_windowService.HasTitleBar(hwnd) || !_windowService.IsResizable(hwnd) ||
            _windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd) ||
            !_windowService.TryGetWindowRect(hwnd, out var rect) || !_windowService.TryGetMonitorWorkArea(hwnd, out var work))
        {
            return;
        }

        if (rect.Width < MinimumWidth || rect.Height < MinimumHeight || LooksSnappedOrFullscreen(rect, work))
        {
            return;
        }

        var app = _windowService.GetAppIdentity(hwnd);
        if (app is null)
        {
            return;
        }

        var dpi = _windowService.GetDpi(hwnd);
        var width96 = (int)Math.Round(rect.Width * 96.0 / dpi);
        var height96 = (int)Math.Round(rect.Height * 96.0 / dpi);

        // A mere move of the window (same size) isn't worth storing again.
        if (_rules.TryGetRememberedSize(app.Key, out var knownWidth, out var knownHeight) && knownWidth == width96 && knownHeight == height96)
        {
            return;
        }

        _rules.SetRememberedSize(app, width96, height96);
        _logger.LogInformation("Remembered size {Width}x{Height} for {App}.", width96, height96, app.Key);
    }

    internal static bool LooksSnappedOrFullscreen(WindowRect rect, WindowRect work)
    {
        bool Near(int value, int target) => Math.Abs(value - target) <= SnapTolerancePixels;

        var fullWidth = rect.Width >= work.Width - SnapTolerancePixels;
        var fullHeight = rect.Height >= work.Height - SnapTolerancePixels;
        var snapWidth = Near(rect.Width, work.Width / 2) || Near(rect.Width, work.Width / 3) || Near(rect.Width, work.Width * 2 / 3);
        var snapHeight = Near(rect.Height, work.Height / 2);

        return (fullWidth && fullHeight) ||   // the whole area
               (snapWidth && fullHeight) ||   // half/third, tall
               (fullWidth && snapHeight) ||   // half, wide
               (snapWidth && snapHeight);     // quarter
    }

    public void Dispose() => Stop();
}
