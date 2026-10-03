namespace Centertized.Core.WindowManagement;

/// <summary>
/// Facade over the Win32 calls for working with windows/monitors. It exists mainly for
/// testability – <see cref="Actions.CenterActiveWindowAction"/> can be tested
/// with a fake implementation instead of a real window on the screen.
/// </summary>
public interface IWin32WindowService
{
    IntPtr GetForegroundWindowHandle();

    /// <summary>
    /// Whether it makes any sense to do anything with this window – the app's own windows, the desktop
    /// (Progman/WorkerW), tool windows without WS_EX_APPWINDOW etc. are skipped.
    /// </summary>
    bool IsEligibleForActions(IntPtr windowHandle);

    bool IsMinimized(IntPtr windowHandle);

    string GetWindowClassName(IntPtr windowHandle);

    /// <summary>
    /// Does the window have a standard title bar (WS_CAPTION)? Tells regular app windows apart from notifications
    /// (toasts), overlays, popups and shell windows, which should not be centered automatically.
    /// </summary>
    bool HasTitleBar(IntPtr windowHandle);

    /// <summary>Can the window be resized with the mouse (WS_THICKFRAME)? Otherwise setting its size makes no sense.</summary>
    bool IsResizable(IntPtr windowHandle);

    /// <summary>DPI of the monitor the window is on (96 = 100 %).</summary>
    int GetDpi(IntPtr windowHandle);

    /// <summary>
    /// The app the window belongs to - for UWP windows the real app, not ApplicationFrameHost.exe.
    /// Null when the identity can't be determined (e.g. a UWP frame that has no content yet).
    /// </summary>
    AppIdentity? GetAppIdentity(IntPtr windowHandle);

    /// <summary>Visible regular app windows (with a title bar, eligible for actions) - for picking a "running app".</summary>
    IReadOnlyList<IntPtr> GetTopLevelAppWindows();

    /// <summary>Short text description of a window (class, styles, process) for diagnostics in the log.</summary>
    string DescribeWindow(IntPtr windowHandle);

    bool IsMaximized(IntPtr windowHandle);

    void Restore(IntPtr windowHandle);

    void Maximize(IntPtr windowHandle);

    /// <summary>Real visual bounds (DWM extended frame bounds), not GetWindowRect.</summary>
    bool TryGetVisualBounds(IntPtr windowHandle, out WindowRect bounds);

    bool TryGetWindowRect(IntPtr windowHandle, out WindowRect bounds);

    /// <summary>Work area (without the taskbar) of the monitor the window is currently on.</summary>
    bool TryGetMonitorWorkArea(IntPtr windowHandle, out WindowRect workArea);

    /// <summary>Moves the window without changing its size and without stealing focus; true = SetWindowPos succeeded.</summary>
    bool TrySetPosition(IntPtr windowHandle, int left, int top);

    /// <summary>Sets position and size at once (GetWindowRect "language"), without stealing focus.</summary>
    bool TrySetBounds(IntPtr windowHandle, WindowRect bounds);
}
