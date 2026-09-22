namespace Centertized.Core.Hotkeys;

/// <summary>
/// Tenká abstrakce nad RegisterHotKey/UnregisterHotKey – existuje čistě kvůli
/// testovatelnosti <see cref="HotkeyActionRegistry"/> bez skutečného okna a bez
/// zásahu do systémového stavu v unit testech.
/// </summary>
public interface IHotkeyRegistrar
{
    HotkeyRegistrarOutcome TryRegister(IntPtr windowHandle, int id, Hotkey hotkey);

    void Unregister(IntPtr windowHandle, int id);
}
