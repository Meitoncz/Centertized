using System.Runtime.InteropServices;

namespace Centertized.Core.Hotkeys;

public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    // Suppresses repeated WM_HOTKEY while the user holds the key.
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyRegistrarOutcome TryRegister(IntPtr windowHandle, int id, Hotkey hotkey)
    {
        var success = RegisterHotKey(windowHandle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKeyCode);
        if (success)
        {
            return HotkeyRegistrarOutcome.Success;
        }

        var error = Marshal.GetLastWin32Error();
        return error == ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotkeyRegistrarOutcome.AlreadyRegisteredElsewhere
            : HotkeyRegistrarOutcome.Failed;
    }

    public void Unregister(IntPtr windowHandle, int id) => UnregisterHotKey(windowHandle, id);
}
