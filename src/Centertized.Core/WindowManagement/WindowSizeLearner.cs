using Centertized.Core.Settings;
using Microsoft.Extensions.Logging;
using static Centertized.Core.WindowManagement.NativeMethods;

namespace Centertized.Core.WindowManagement;

/// <summary>
/// Automaticky si pamatuje velikost, kterou uživatel oknům aplikace nastavil: při dokončení
/// přesouvání/změny velikosti (EVENT_SYSTEM_MOVESIZEEND) uloží velikost okna k jeho aplikaci.
/// Nová okna té aplikace se pak (přes <see cref="RememberedSizePolicy"/>) otevřou ve stejné velikosti.
/// Jen oznamovací hook jako <see cref="NewWindowWatcher"/> - nic neblokuje ani nepotlačuje.
/// </summary>
public sealed class WindowSizeLearner : IDisposable
{
    // Menší okno je skoro jistě dialog/popup, ne pracovní okno, jehož velikost stojí za zapamatování.
    private const int MinimumWidth = 240;
    private const int MinimumHeight = 160;

    // Při Aero Snap (půlka/třetina obrazovky) systém okno "přilepí" a MOVESIZEEND přijde taky -
    // takovou velikost si zapamatovat nechceme, byla by z ní pak "normální" velikost okna.
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
            _logger.LogWarning(ex, "Zapamatování velikosti okna {Handle} spadlo.", hwnd);
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

        // Pouhé přesunutí okna (velikost stejná) nemá smysl ukládat znovu.
        if (_rules.TryGetRememberedSize(app.Key, out var knownWidth, out var knownHeight) && knownWidth == width96 && knownHeight == height96)
        {
            return;
        }

        _rules.SetRememberedSize(app, width96, height96);
        _logger.LogInformation("Zapamatována velikost {Width}x{Height} pro {App}.", width96, height96, app.Key);
    }

    internal static bool LooksSnappedOrFullscreen(WindowRect rect, WindowRect work)
    {
        bool Near(int value, int target) => Math.Abs(value - target) <= SnapTolerancePixels;

        var fullWidth = rect.Width >= work.Width - SnapTolerancePixels;
        var fullHeight = rect.Height >= work.Height - SnapTolerancePixels;
        var snapWidth = Near(rect.Width, work.Width / 2) || Near(rect.Width, work.Width / 3) || Near(rect.Width, work.Width * 2 / 3);
        var snapHeight = Near(rect.Height, work.Height / 2);

        return (fullWidth && fullHeight) ||   // přes celou plochu
               (snapWidth && fullHeight) ||   // půlka/třetina na výšku
               (fullWidth && snapHeight) ||   // půlka na šířku
               (snapWidth && snapHeight);     // čtvrtina
    }

    public void Dispose() => Stop();
}
