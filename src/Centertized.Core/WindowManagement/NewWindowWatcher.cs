using System.Diagnostics;
using Centertized.Core.Actions;
using Centertized.Core.Settings;
using Microsoft.Extensions.Logging;
using static Centertized.Core.WindowManagement.NativeMethods;

namespace Centertized.Core.WindowManagement;

/// <summary>
/// Uses SetWinEventHook (EVENT_OBJECT_SHOW) to notice when a window is shown for the first time
/// and centers it right away - the basis of "auto-center every newly opened window"
/// (see TODO.md). EVENT_OBJECT_SHOW on purpose instead of EVENT_SYSTEM_FOREGROUND - it fires
/// earlier (at ShowWindow(SW_SHOW), not after the window takes focus), so the user is less likely
/// to catch a glimpse of the window at its original position before it jumps to the center.
/// It is not a hard guarantee - the app reacts asynchronously from another process, so a one-frame
/// flicker can't be ruled out entirely (Windows has no API for "position a foreign window
/// before its first paint").
///
/// Unlike the WH_KEYBOARD_LL hook (see CLAUDE.md - incident 2026-09-23, the app locked the
/// keyboard including Alt+Tab) this is a purely NOTIFICATION mechanism - Windows only informs
/// us, nothing is suppressed or blocked. Even if the callback crashed or misbehaved, it can't
/// affect the keyboard/system like that earlier hook did.
/// </summary>
public sealed class NewWindowWatcher : IDisposable
{
    private const string UwpFrameClassName = "ApplicationFrameWindow";

    // UWP/modern apps (Settings, Store...) set their size and position only after a moment
    // (opening animation, restoring a saved position) - at SHOW they only have transitional
    // geometry (e.g. Store is 166x47 at 0,0). So after it is shown the window is tracked for
    // TrackingDuration and re-centered whenever it changes its own size.
    private static readonly TimeSpan TrackingDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    // Regular (non-UWP) windows: the original, verified behavior - immediate centering, one quick
    // retry (DWM sometimes hasn't computed DWMWA_EXTENDED_FRAME_BOUNDS yet at the SHOW moment)
    // and one later check. Deliberately unchanged so that what works doesn't break.
    private static readonly TimeSpan GeometryRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan AnimationSettleDelay = TimeSpan.FromMilliseconds(450);

    // In this initial phase (the user physically can't grab the window yet) the window is also
    // re-centered when only its position changes; later only when its size changes - otherwise the
    // app would fight a user who drags the window right after it opens.
    private static readonly TimeSpan PositionCorrectionWindow = TimeSpan.FromMilliseconds(600);

    // A closed UWP window is only hidden (DWM cloak) and on reopening it gets just UNCLOAKED, not
    // SHOW. The same events occur on a virtual desktop switch - that uncloaks many windows at once,
    // including regular Win32 windows that are otherwise never cloaked - so only non-UWP windows
    // count toward the "burst" (Store opens several UWP frames at once).
    private static readonly TimeSpan UncloakBurstWindow = TimeSpan.FromMilliseconds(60);

    private readonly IWin32WindowService _windowService;
    private readonly ILogger _logger;
    private readonly AppRulesService _rules;
    private readonly IWindowSizePolicy _sizePolicy;
    private readonly HashSet<IntPtr> _seenWindows = [];
    private readonly List<long> _recentUncloakTicks = [];
    // The delegate must stay alive for as long as the hook is installed - otherwise the GC could
    // collect it and the native call from Windows would hit a freed pointer.
    private readonly WinEventDelegate _callback;
    private IntPtr _hookHandle;
    private IntPtr _cloakHookHandle;

    // LOCATIONCHANGE fires system-wide extremely often (every cursor/window move), so the hook is
    // installed only while at least one UWP window is being tracked. Installing and uninstalling
    // must happen on the thread with a message loop (UI) that installed the hook - hence
    // _uiContext.
    private readonly Dictionary<IntPtr, TrackState> _tracked = [];
    private IntPtr _locationHookHandle;
    private SynchronizationContext? _uiContext;

    private sealed class TrackState(WindowRect applied)
    {
        public WindowRect Applied { get; set; } = applied;

        public Stopwatch Age { get; } = Stopwatch.StartNew();
    }

    public NewWindowWatcher(IWin32WindowService windowService, ILogger logger, AppRulesService rules, IWindowSizePolicy sizePolicy)
    {
        _windowService = windowService;
        _logger = logger;
        _rules = rules;
        _sizePolicy = sizePolicy;
        _callback = OnWinEvent;
    }

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        // The DESTROY..HIDE range also includes SHOW (0x8001-0x8003) - DESTROY/HIDE are used to
        // "forget" a window, see OnWinEvent.
        _uiContext = SynchronizationContext.Current;
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
        if (_locationHookHandle != IntPtr.Zero)
        {
            UnhookWinEvent(_locationHookHandle);
            _locationHookHandle = IntPtr.Zero;
        }

        lock (_tracked)
        {
            _tracked.Clear();
        }

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
        // The callback is invoked directly from Windows (native code) - an exception would have nowhere
        // safe to propagate, so it is caught here rather than a level up.
        try
        {
            // idObject/idChild other than "the window itself" are sub-elements (title bar,
            // scrollbar...), we don't care about those.
            if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            {
                return;
            }

            switch (eventType)
            {
                case EVENT_OBJECT_DESTROY:
                case EVENT_OBJECT_HIDE:
                    // Forget a closed/hidden window - UWP frames are only hidden when closed and shown again
                    // with the same hwnd on the next launch, and Windows also recycles hwnd values. Without
                    // this a reopened window would be skipped as "already seen" and not centered.
                    _seenWindows.Remove(hwnd);
                    return;

                case EVENT_OBJECT_UNCLOAKED:
                    OnUncloaked(hwnd);
                    return;

                case EVENT_OBJECT_LOCATIONCHANGE:
                    OnLocationChanged(hwnd);
                    return;
            }

            if (!_seenWindows.Add(hwnd))
            {
                return; // this window is already shown and handled
            }

            // Only regular windows with a title bar are auto-centered - notifications (toasts), OSD,
            // overlays, popups and shell windows (Windows.UI.Core.CoreWindow etc.) have no title bar and
            // their position is chosen deliberately by the system/app (typically the bottom-right corner).
            if (_windowService.IsEligibleForActions(hwnd) && _windowService.HasTitleBar(hwnd))
            {
                Begin(hwnd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handling the WinEvent for window {Handle} failed.", hwnd);
        }
    }

    private void OnUncloaked(IntPtr hwnd)
    {
        var now = Environment.TickCount64;

        if (!IsUwpFrame(hwnd))
        {
            // Uncloak of a regular titled window = a virtual desktop switch, not a new app opening.
            // Windows without a title bar don't count - typically Windows.UI.Core.CoreWindow, which is
            // uncloaked together with its UWP frame on every open.
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
            // Wait a moment to see whether UNCLOAKED events of regular windows arrive (desktop switch).
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
            _logger.LogWarning(ex, "Auto-centering the reopened window {Handle} failed.", hwnd);
        }
    }

    private void Begin(IntPtr hwnd)
    {
        if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd) || CoversWholeWorkArea(hwnd) || IsExcluded(hwnd))
        {
            return;
        }

        // Deliberately no waiting - center as soon as possible after it is shown so the user
        // preferably never sees the window at its original position (see the class comment).
        // Debug level - running into a foreign/transient window (popup, tooltip...) where centering
        // makes no sense is common here, not worth a warning and a tray balloon.
        Center(hwnd);
        if (IsUwpFrame(hwnd))
        {
            _ = TrackAsync(hwnd);
        }
        else
        {
            _ = FollowUpAsync(hwnd);
        }
    }

    private void Center(IntPtr hwnd) =>
        WindowCenterer.TryCenter(_windowService, _logger, hwnd, LogLevel.Debug, _sizePolicy);

    // Per-app exception. For a UWP frame that has no content yet the identity is unknown -
    // the window is then centered as usual and the exception is re-checked at the next tracking step.
    private bool IsExcluded(IntPtr hwnd)
    {
        var app = _windowService.GetAppIdentity(hwnd);
        return app is not null && _rules.IsExcluded(app.Key);
    }

    // A window covering the whole work area (Snipping Tool overlay, full-screen apps) must not be
    // centered - it makes no sense and would only push it off-screen.
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

            Center(hwnd);

            await Task.Delay(AnimationSettleDelay).ConfigureAwait(false);
            if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd))
            {
                return;
            }

            Center(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-centering the new window {Handle} failed.", hwnd);
        }
    }

    private async Task TrackAsync(IntPtr hwnd)
    {
        _windowService.TryGetWindowRect(hwnd, out var initial);
        var state = new TrackState(initial);
        lock (_tracked)
        {
            _tracked[hwnd] = state;
        }

        _uiContext?.Post(_ => EnsureLocationHook(), null);

        try
        {
            var previous = initial;

            while (state.Age.Elapsed < TrackingDuration)
            {
                await Task.Delay(PollInterval).ConfigureAwait(false);

                if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd) ||
                    !_windowService.TryGetWindowRect(hwnd, out var current))
                {
                    return; // the window disappeared / the user did something with it
                }

                // Fallback to the quick LOCATIONCHANGE reaction (see OnLocationChanged) -
                // center only once the window has stopped changing for one poll.
                if (current == previous)
                {
                    CenterIfChanged(hwnd, state, current);
                }

                previous = current;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tracking the new window {Handle} failed.", hwnd);
        }
        finally
        {
            lock (_tracked)
            {
                _tracked.Remove(hwnd);
            }

            _uiContext?.Post(_ => ReleaseLocationHook(), null);
        }
    }

    // Reacts within a few ms to a UWP window changing its own size/position - the jump to the
    // center is then barely noticeable (compared to waiting for polling).
    private void OnLocationChanged(IntPtr hwnd)
    {
        TrackState? state;
        lock (_tracked)
        {
            if (!_tracked.TryGetValue(hwnd, out state))
            {
                return;
            }
        }

        if (_windowService.IsMinimized(hwnd) || _windowService.IsMaximized(hwnd) ||
            !_windowService.TryGetWindowRect(hwnd, out var current))
        {
            return;
        }

        CenterIfChanged(hwnd, state, current);
    }

    // Centers the window if it changed on its own since we last placed it: a size change always,
    // a position-only change just in the initial phase (otherwise the app would fight a user who
    // drags the window right after it opens). Our own move triggers another LOCATIONCHANGE,
    // which is skipped because the rect already equals Applied.
    private void CenterIfChanged(IntPtr hwnd, TrackState state, WindowRect current)
    {
        lock (state)
        {
            if (current == state.Applied || IsExcluded(hwnd))
            {
                return;
            }

            var sizeChanged = current.Width != state.Applied.Width || current.Height != state.Applied.Height;
            var positionChanged = current.Left != state.Applied.Left || current.Top != state.Applied.Top;
            if (!sizeChanged && !(positionChanged && state.Age.Elapsed < PositionCorrectionWindow))
            {
                return;
            }

            Center(hwnd);
            if (_windowService.TryGetWindowRect(hwnd, out var after))
            {
                state.Applied = after;
            }
        }
    }

    private void EnsureLocationHook()
    {
        if (_locationHookHandle != IntPtr.Zero || !IsRunning)
        {
            return;
        }

        _locationHookHandle = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void ReleaseLocationHook()
    {
        bool anyTracked;
        lock (_tracked)
        {
            anyTracked = _tracked.Count > 0;
        }

        if (anyTracked || _locationHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWinEvent(_locationHookHandle);
        _locationHookHandle = IntPtr.Zero;
    }

    public void Dispose() => Stop();
}
