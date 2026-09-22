namespace Centertized.Core.Hotkeys;

/// <summary>
/// Záměrně vlastní enum místo WPF <c>System.Windows.Input.ModifierKeys</c> – tenhle
/// projekt nesmí mít závislost na WPF (viz CLAUDE.md). Hodnoty odpovídají 1:1 Win32
/// konstantám MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN, takže se dají přímo přetypovat
/// a poslat do RegisterHotKey bez dalšího překladu.
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
