using Centertized.Core.Actions;
using Microsoft.Extensions.Logging;
using static Centertized.Core.WindowManagement.NativeMethods;

namespace Centertized.Core.WindowManagement;

/// <summary>
/// Sleduje přes SetWinEventHook (EVENT_OBJECT_SHOW), kdy se nějaké okno poprvé zobrazí,
/// a rovnou ho vycentruje - základ pro "auto-centrovat každé nově otevřené okno"
/// (viz TODO.md). EVENT_OBJECT_SHOW záměrně místo EVENT_SYSTEM_FOREGROUND - nastává
/// dřív (ve chvíli ShowWindow(SW_SHOW), ne až po přebrání focusu), takže je menší šance,
/// že uživatel okno stihne zahlédnout na jeho původní pozici před přeskočením doprostřed.
/// Nejde ale o tvrdou garanci - appka reaguje asynchronně z jiného procesu, takže úplně
/// vyloučit jednosnímkové bliknutí nejde (Windows nemá API na "polohu před prvním
/// vykreslením cizího okna").
///
/// Na rozdíl od WH_KEYBOARD_LL hooku (viz CLAUDE.md - incident 2026-09-23, appka si
/// zablokovala klávesnici včetně Alt+Tab) jde čistě o OZNAMOVACÍ mechanismus - Windows
/// tímhle jen informuje, nic to nepotlačuje ani neblokuje. I kdyby callback spadl nebo
/// se s ním něco pokazilo, nemůže to ovlivnit klávesnici/systém jako minulý hook.
/// </summary>
public sealed class NewWindowWatcher : IDisposable
{
    // Krátká prodleva jen pro případ, že DWM v okamžiku SHOW ještě nemá spočtené
    // DWMWA_EXTENDED_FRAME_BOUNDS (viz WindowCenterer) - použije se jen jako jeden
    // rychlý retry, ne jako plošné čekání před každým pokusem.
    private static readonly TimeSpan GeometryRetryDelay = TimeSpan.FromMilliseconds(40);

    private readonly IWin32WindowService _windowService;
    private readonly ILogger _logger;
    private readonly HashSet<IntPtr> _seenWindows = [];
    // Delegát musí zůstat naživu po celou dobu, co je hook nainstalovaný - jinak by ho
    // GC mohl uvolnit a nativní volání z Windows by spadlo na uvolněný pointer.
    private readonly WinEventDelegate _callback;
    private IntPtr _hookHandle;

    public NewWindowWatcher(IWin32WindowService windowService, ILogger logger)
    {
        _windowService = windowService;
        _logger = logger;
        _callback = OnWinEvent;
    }

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _hookHandle = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        UnhookWinEvent(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _seenWindows.Clear();
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Callback běží volaný přímo z Windows (nativní kód) - výjimka by odsud neměla
        // kam "spadnout" bezpečně, proto se chytá tady, ne až o úroveň výš.
        try
        {
            // idObject/idChild jiné než "samotné okno" jsou podprvky (titulek,
            // scrollbar...), ty nás nezajímají.
            if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!_seenWindows.Add(hwnd))
            {
                return; // tohle okno jsme už jednou zpracovali (např. návrat přes Alt+Tab)
            }

            if (!_windowService.IsEligibleForActions(hwnd) || _windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd))
            {
                return;
            }

            // Záměrně bez čekání - centrovat co nejdřív po zobrazení, ať uživatel okno
            // pokud možno vůbec nezahlédne na jeho původní pozici (viz komentář u třídy).
            // Debug level - narazit na cizí/přechodné okno (popup, tooltip...), kde
            // centrování nedává smysl, je tady běžné, ne varování hodné tray balonku.
            if (WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug))
            {
                return;
            }

            // Selhalo - nejspíš DWM ještě nestihl spočítat DWMWA_EXTENDED_FRAME_BOUNDS
            // těsně po zobrazení okna. Jeden rychlý retry stačí, dál to nehonit.
            _ = RetryAsync(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zpracování WinEvent pro okno {Handle} spadlo.", hwnd);
        }
    }

    private async Task RetryAsync(IntPtr hwnd)
    {
        try
        {
            await Task.Delay(GeometryRetryDelay).ConfigureAwait(false);

            if (_windowService.IsMinimized(hwnd))
            {
                return;
            }

            WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-centrování nového okna {Handle} spadlo.", hwnd);
        }
    }

    public void Dispose() => Stop();
}
