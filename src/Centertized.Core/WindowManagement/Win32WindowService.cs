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

        // The app's own Settings window is a normal visible top-level window like any other -
        // there's no reason to exclude it from the actions, the user may want to center/maximize it
        // just like anything else. The hidden message-only window for hotkeys (the app's only other
        // window) never passes the IsWindowVisible check a few lines above anyway.
        var className = GetWindowClassName(windowHandle);
        if (className is "Progman" or "WorkerW")
        {
            return false; // the desktop
        }

        if (GetWindow(windowHandle, GW_OWNER) != IntPtr.Zero)
        {
            return false; // it has an owner, so it isn't a standalone top-level window
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

    public bool IsResizable(IntPtr windowHandle) => (GetWindowLong(windowHandle, GWL_STYLE) & WS_THICKFRAME) != 0;

    public int GetDpi(IntPtr windowHandle)
    {
        var dpi = GetDpiForWindow(windowHandle);
        return dpi == 0 ? 96 : (int)dpi;
    }

    private readonly Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);

    public AppIdentity? GetAppIdentity(IntPtr windowHandle)
    {
        GetWindowThreadProcessId(windowHandle, out var processId);

        if (GetWindowClassName(windowHandle) == "ApplicationFrameWindow")
        {
            // The frame belongs to ApplicationFrameHost.exe, the real app lives in the CoreWindow inside.
            var appProcessId = 0u;
            EnumChildWindows(windowHandle, (child, _) =>
            {
                if (GetWindowClassName(child) == "Windows.UI.Core.CoreWindow")
                {
                    GetWindowThreadProcessId(child, out var childProcessId);
                    if (childProcessId != processId)
                    {
                        appProcessId = childProcessId;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);

            if (appProcessId == 0)
            {
                return null;
            }

            processId = appProcessId;
        }

        var path = GetProcessImagePath(processId);
        if (path is null)
        {
            return null;
        }

        var key = Path.GetFileName(path).ToLowerInvariant();
        string displayName;
        lock (_displayNames)
        {
            if (!_displayNames.TryGetValue(path, out displayName!))
            {
                displayName = ReadDisplayName(path);
                _displayNames[path] = displayName;
            }
        }

        return new AppIdentity(key, displayName, path);
    }

    public IReadOnlyList<IntPtr> GetTopLevelAppWindows()
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsEligibleForActions(hwnd) && HasTitleBar(hwnd))
            {
                result.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string? GetProcessImagePath(uint processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null; // typically a process with elevated rights, or it has already exited
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string ReadDisplayName(string path)
    {
        var fallback = Path.GetFileNameWithoutExtension(path);
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    // Some apps (Store Notepad) have the file extension in the description too.
                    var name = candidate.Trim();
                    return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable version info is no reason to fail - the file name is enough.
        }

        return fallback;
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
