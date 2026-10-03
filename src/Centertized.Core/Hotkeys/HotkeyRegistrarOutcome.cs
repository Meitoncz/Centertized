namespace Centertized.Core.Hotkeys;

public enum HotkeyRegistrarOutcome
{
    Success,

    /// <summary>
    /// RegisterHotKey failed with ERROR_HOTKEY_ALREADY_REGISTERED – the combination is already
    /// registered by another process through the same Win32 API, or is reserved
    /// by Windows itself (both look the same from RegisterHotKey's point of view, they can't be
    /// told apart).
    /// </summary>
    AlreadyRegisteredElsewhere,

    /// <summary>Unexpected failure for another reason.</summary>
    Failed,
}
