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

        // Settings okno appky je normální viditelné top-level okno jako každé jiné -
        // není důvod ho z akcí vylučovat, uživatel ho může chtít centrovat/maximalizovat
        // stejně jako cokoliv jiného. Skryté message-only okno pro hotkeys (jediné další
        // okno appky) beztak nikdy neprojde IsWindowVisible kontrolou o pár řádků výš.
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

    public string DescribeWindow(IntPtr windowHandle)
    {
        GetWindowThreadProcessId(windowHandle, out var processId);
        string processName;
        try
        {
            processName = System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception)
        {
            processName = "?";
        }

        var style = GetWindowLong(windowHandle, GWL_STYLE);
        var exStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
        return $"class={GetWindowClassName(windowHandle)} proc={processName} style=0x{style:X} exStyle=0x{exStyle:X}";
    }

    public bool HasTitleBar(IntPtr windowHandle) => (GetWindowLong(windowHandle, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;

    public string GetWindowClassName(IntPtr windowHandle)
    {
        var buffer = new StringBuilder(256);
        GetClassName(windowHandle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

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

}
