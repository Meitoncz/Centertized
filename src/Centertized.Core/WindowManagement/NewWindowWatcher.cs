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

    // UWP/moderní appky (Nastavení, Store...) mají při otevření krátkou animaci -
    // v okamžiku EVENT_OBJECT_SHOW ještě nemají finální velikost/pozici, takže první
    // centrování se netrefí a appka se pak doanimuje jinam (ověřeno 2026-09-23 -
    // Nastavení/Store se necentrovaly, log ukazoval úspěšné SetWindowPos na pozici,
    // která seděla jen k přechodné, ne finální geometrii). Druhá kontrola po tomhle
    // čase to dorovná.
    private static readonly TimeSpan AnimationSettleDelay = TimeSpan.FromMilliseconds(450);

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

        // Rozsah DESTROY..HIDE zahrnuje i SHOW (0x8001-0x8003) - DESTROY/HIDE slouží k
        // "zapomenutí" okna, viz OnWinEvent.
        _hookHandle = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
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

            // Zavřené/skryté okno zapomenout - UWP rámce (Nastavení, Store...) se po
            // zavření jen skryjí a při dalším otevření se znovu ukážou se stejným hwnd,
            // a Windows navíc hwnd hodnoty recykluje. Bez tohohle by se znovuotevřené
            // okno přeskočilo jako "už viděné" a necentrovalo se.
            if (eventType != EVENT_OBJECT_SHOW)
            {
                _seenWindows.Remove(hwnd);
                return;
            }

            if (!_seenWindows.Add(hwnd))
            {
                return; // tohle okno už je zobrazené a zpracované
            }

            if (!_windowService.IsEligibleForActions(hwnd) || _windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd))
            {
                return;
            }

            // Záměrně bez čekání - centrovat co nejdřív po zobrazení, ať uživatel okno
            // pokud možno vůbec nezahlédne na jeho původní pozici (viz komentář u třídy).
            // Debug level - narazit na cizí/přechodné okno (popup, tooltip...), kde
            // centrování nedává smysl, je tady běžné, ne varování hodné tray balonku.
            WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug);
            _ = FollowUpAsync(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zpracování WinEvent pro okno {Handle} spadlo.", hwnd);
        }
    }

    private async Task FollowUpAsync(IntPtr hwnd)
    {
        try
        {
            // Rychlý retry - DWM občas nemá v okamžiku SHOW ještě spočtené
            // DWMWA_EXTENDED_FRAME_BOUNDS, první pokus výše pak selže.
            await Task.Delay(GeometryRetryDelay).ConfigureAwait(false);
            if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd))
            {
                return;
            }

            WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug);

            // Druhá, delší kontrola kvůli otevírací animaci u UWP/moderních appek
            // (viz komentář u AnimationSettleDelay) - přecentrovat znovu, jakmile
            // appka doanimuje na svou finální velikost/pozici.
            await Task.Delay(AnimationSettleDelay).ConfigureAwait(false);
            if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd))
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
