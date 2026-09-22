using System.Text;
using static Centertized.Core.WindowManagement.NativeMethods;

namespace Centertized.Core.WindowManagement;

public sealed class Win32WindowService : IWin32WindowService
{
    public IntPtr GetForegroundWindowHandle() => NativeMethods.GetForegroundWindow();

    public bool IsEligibleForActions(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindowVisible(windowHandle))
        {
            return false;
        }

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return false; // vlastní okno appky (Settings apod.)
        }

        var className = GetWindowClassName(windowHandle);
        if (className is "Progman" or "WorkerW")
        {
            return false; // plocha
        }

        if (GetWindow(windowHandle, GW_OWNER) != IntPtr.Zero)
        {
            return false; // má vlastníka, není to samostatné top-level okno
        }

        var extendedStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
        var isToolWindow = (extendedStyle & WS_EX_TOOLWINDOW) != 0;
        var isAppWindow = (extendedStyle & WS_EX_APPWINDOW) != 0;
        if (isToolWindow && !isAppWindow)
        {
            return false;
        }

        return true;
    }

    public bool IsMinimized(IntPtr windowHandle) => IsIconic(windowHandle);

    public bool IsMaximized(IntPtr windowHandle) => IsZoomed(windowHandle);

    public void Restore(IntPtr windowHandle) => ShowWindow(windowHandle, SW_RESTORE);

    public void Maximize(IntPtr windowHandle) => ShowWindow(windowHandle, SW_MAXIMIZE);

    public bool TryGetVisualBounds(IntPtr windowHandle, out WindowRect bounds)
    {
        var hresult = DwmGetWindowAttribute(windowHandle, DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, System.Runtime.InteropServices.Marshal.SizeOf<RECT>());
        bounds = rect.ToWindowRect();
        return hresult == 0; // S_OK
    }

    public bool TryGetWindowRect(IntPtr windowHandle, out WindowRect bounds)
    {
        var success = GetWindowRect(windowHandle, out var rect);
        bounds = rect.ToWindowRect();
        return success;
    }

    public bool TryGetMonitorWorkArea(IntPtr windowHandle, out WindowRect workArea)
    {
        var monitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        var success = GetMonitorInfo(monitor, ref info);
        workArea = info.rcWork.ToWindowRect();
        return success;
    }

    public bool TrySetPosition(IntPtr windowHandle, int left, int top) =>
        SetWindowPos(windowHandle, IntPtr.Zero, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    public bool TrySetBounds(IntPtr windowHandle, WindowRect bounds) =>
        SetWindowPos(windowHandle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    private static string GetWindowClassName(IntPtr windowHandle)
    {
        var buffer = new StringBuilder(256);
        GetClassName(windowHandle, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
