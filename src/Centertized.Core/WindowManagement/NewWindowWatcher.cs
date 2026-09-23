using System.Diagnostics;
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
    private const string UwpFrameClassName = "ApplicationFrameWindow";

    // UWP/moderní appky (Nastavení, Store...) si při otevření velikost a pozici nastaví
    // až po chvíli (otevírací animace, obnovení uložené pozice) - v okamžiku SHOW mají
    // jen přechodnou geometrii (Store např. 166x47 na 0,0). Proto se okno po zobrazení
    // TrackingDuration sleduje a při samovolné změně velikosti se vycentruje znovu.
    private static readonly TimeSpan TrackingDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    // Běžná (ne-UWP) okna: původní ověřené chování - okamžité centrování, jeden rychlý
    // retry (DWM v okamžiku SHOW občas ještě nemá spočtené DWMWA_EXTENDED_FRAME_BOUNDS)
    // a jedna pozdější kontrola. Záměrně beze změny, ať se nerozbije to, co funguje.
    private static readonly TimeSpan GeometryRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan AnimationSettleDelay = TimeSpan.FromMilliseconds(450);

    // V téhle úvodní fázi (uživatel okno fyzicky nestihne chytit) se znovu centruje i při
    // změně samotné pozice, později už jen při změně velikosti - jinak by appka
    // bojovala s uživatelem, který okno hned po otevření táhne.
    private static readonly TimeSpan PositionCorrectionWindow = TimeSpan.FromMilliseconds(600);

    // UWP okno se po zavření jen schová (DWM cloak) a při znovuotevření dostane jen
    // UNCLOAKED, ne SHOW. Stejné události ale vznikají při přepnutí virtuální plochy - to
    // odkryje víc oken najednou, včetně běžných Win32 oken, která se jinak nikdy necloaknou -
    // proto se do "dávky" počítají jen ne-UWP okna (Store otevře víc UWP rámců naráz).
    private static readonly TimeSpan UncloakBurstWindow = TimeSpan.FromMilliseconds(150);

    private readonly IWin32WindowService _windowService;
    private readonly ILogger _logger;
    private readonly HashSet<IntPtr> _seenWindows = [];
    private readonly List<long> _recentUncloakTicks = [];
    // Delegát musí zůstat naživu po celou dobu, co je hook nainstalovaný - jinak by ho
    // GC mohl uvolnit a nativní volání z Windows by spadlo na uvolněný pointer.
    private readonly WinEventDelegate _callback;
    private IntPtr _hookHandle;
    private IntPtr _cloakHookHandle;

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
        _cloakHookHandle = SetWinEventHook(EVENT_OBJECT_UNCLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        UnhookWinEvent(_hookHandle);
        UnhookWinEvent(_cloakHookHandle);
        _hookHandle = IntPtr.Zero;
        _cloakHookHandle = IntPtr.Zero;
        _seenWindows.Clear();
        lock (_recentUncloakTicks)
        {
            _recentUncloakTicks.Clear();
        }
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

            switch (eventType)
            {
                case EVENT_OBJECT_DESTROY:
                case EVENT_OBJECT_HIDE:
                    // Zavřené/skryté okno zapomenout - UWP rámce se po zavření jen schovají
                    // a při dalším otevření se znovu ukážou se stejným hwnd, a Windows navíc
                    // hwnd hodnoty recykluje. Bez tohohle by se znovuotevřené okno přeskočilo
                    // jako "už viděné" a necentrovalo se.
                    _seenWindows.Remove(hwnd);
                    return;

                case EVENT_OBJECT_UNCLOAKED:
                    OnUncloaked(hwnd);
                    return;
            }

            if (!_seenWindows.Add(hwnd))
            {
                return; // tohle okno už je zobrazené a zpracované
            }

            // Automaticky se centrují jen běžná okna se záhlavím - notifikace (toasty), OSD,
            // overlaye, popupy a shellová okna (Windows.UI.Core.CoreWindow apod.) záhlaví
            // nemají a jejich pozici určuje systém/appka záměrně (typicky pravý dolní roh).
            if (_windowService.IsEligibleForActions(hwnd) && _windowService.HasTitleBar(hwnd))
            {
                Begin(hwnd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zpracování WinEvent pro okno {Handle} spadlo.", hwnd);
        }
    }

    private void OnUncloaked(IntPtr hwnd)
    {
        var now = Environment.TickCount64;

        if (!IsUwpFrame(hwnd))
        {
            // Uncloak běžného okna se záhlavím = přepnutí virtuální plochy, ne otevření
            // nové appky. Okna bez záhlaví se nepočítají - typicky jde o Windows.UI.Core.
            // CoreWindow, který se odkryje spolu se svým UWP rámcem při každém otevření.
            if (_windowService.HasTitleBar(hwnd) && _windowService.IsEligibleForActions(hwnd))
            {
                lock (_recentUncloakTicks)
                {
                    _recentUncloakTicks.RemoveAll(t => now - t > UncloakBurstWindow.TotalMilliseconds * 4);
                    _recentUncloakTicks.Add(now);
                }
            }

            return;
        }

        if (!_windowService.IsEligibleForActions(hwnd) || !_windowService.HasTitleBar(hwnd))
        {
            return;
        }

        _ = ReopenAsync(hwnd, now);
    }

    private async Task ReopenAsync(IntPtr hwnd, long uncloakTick)
    {
        try
        {
            // Chvíli počkat, jestli nepřijdou UNCLOAKED běžných oken (přepnutí plochy).
            await Task.Delay(UncloakBurstWindow).ConfigureAwait(false);
            bool desktopSwitch;
            lock (_recentUncloakTicks)
            {
                desktopSwitch = _recentUncloakTicks.Any(t => Math.Abs(t - uncloakTick) <= UncloakBurstWindow.TotalMilliseconds);
            }

            if (desktopSwitch)
            {
                return;
            }

            Begin(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-centrování znovuotevřeného okna {Handle} spadlo.", hwnd);
        }
    }

    private void Begin(IntPtr hwnd)
    {
        if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd) || CoversWholeWorkArea(hwnd))
        {
            return;
        }

        // Záměrně bez čekání - centrovat co nejdřív po zobrazení, ať uživatel okno
        // pokud možno vůbec nezahlédne na jeho původní pozici (viz komentář u třídy).
        // Debug level - narazit na cizí/přechodné okno (popup, tooltip...), kde
        // centrování nedává smysl, je tady běžné, ne varování hodné tray balonku.
        WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug);
        _ = IsUwpFrame(hwnd) ? TrackAsync(hwnd) : FollowUpAsync(hwnd);
    }

    // Okno přes celou pracovní plochu (overlay Výstřižků, celoobrazovkové appky) se
    // centrovat nemá - nedává to smysl a jen by ho posunulo mimo obrazovku.
    private bool CoversWholeWorkArea(IntPtr hwnd) =>
        _windowService.TryGetWindowRect(hwnd, out var rect) &&
        _windowService.TryGetMonitorWorkArea(hwnd, out var work) &&
        rect.Width >= work.Width && rect.Height >= work.Height;

    private bool IsUwpFrame(IntPtr hwnd) => _windowService.GetWindowClassName(hwnd) == UwpFrameClassName;

    private async Task FollowUpAsync(IntPtr hwnd)
    {
        try
        {
            await Task.Delay(GeometryRetryDelay).ConfigureAwait(false);
            if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd))
            {
                return;
            }

            WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug);

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

    private async Task TrackAsync(IntPtr hwnd)
    {
        try
        {
            _windowService.TryGetWindowRect(hwnd, out var applied);
            var previous = applied;
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < TrackingDuration)
            {
                await Task.Delay(PollInterval).ConfigureAwait(false);

                if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd) ||
                    !_windowService.TryGetWindowRect(hwnd, out var current))
                {
                    return; // okno zmizelo / uživatel s ním něco udělal
                }

                var sizeChanged = current.Width != applied.Width || current.Height != applied.Height;
                var positionChanged = current.Left != applied.Left || current.Top != applied.Top;
                var needsCentering = sizeChanged || (positionChanged && stopwatch.Elapsed < PositionCorrectionWindow);

                // Centrovat až ve chvíli, kdy se okno na jeden poll přestane měnit - jinak
                // by se honilo za probíhající animací.
                if (needsCentering && current == previous)
                {
                    WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug);
                    if (_windowService.TryGetWindowRect(hwnd, out var after))
                    {
                        applied = after;
                        current = after;
                    }
                }

                previous = current;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sledování nového okna {Handle} spadlo.", hwnd);
        }
    }

    public void Dispose() => Stop();
}
