namespace Centertized.Core.Hotkeys;

/// <summary>
/// A thin abstraction over RegisterHotKey/UnregisterHotKey – it exists purely for the
/// testability of <see cref="HotkeyActionRegistry"/> without a real window and without
/// touching the system state in unit tests.
/// </summary>
public interface IHotkeyRegistrar
{
    HotkeyRegistrarOutcome TryRegister(IntPtr windowHandle, int id, Hotkey hotkey);

    void Unregister(IntPtr windowHandle, int id);
}
