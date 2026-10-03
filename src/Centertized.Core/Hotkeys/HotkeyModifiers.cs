namespace Centertized.Core.Hotkeys;

/// <summary>
/// Deliberately our own enum instead of the WPF <c>System.Windows.Input.ModifierKeys</c> – this
/// project must not depend on WPF (see CLAUDE.md). The values match the Win32
/// constants MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN 1:1, so they can be cast directly
/// and passed to RegisterHotKey without further translation.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}
