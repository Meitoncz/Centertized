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
            // Rámec patří ApplicationFrameHost.exe, skutečná appka žije v CoreWindow uvnitř.
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

        return new AppIdentity(key, displayName);
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
            return null; // typicky proces se zvýšenými právy nebo už skončil
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
                    // Některé appky (Store Notepad) mají v popisu i příponu souboru.
                    var name = candidate.Trim();
                    return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                }
            }
        }
        catch (Exception)
        {
            // Nečitelné verzové info není důvod nefungovat - stačí název souboru.
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
