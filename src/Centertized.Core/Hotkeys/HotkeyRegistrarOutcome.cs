namespace Centertized.Core.Hotkeys;

public enum HotkeyRegistrarOutcome
{
    Success,

    /// <summary>
    /// RegisterHotKey selhal s ERROR_HOTKEY_ALREADY_REGISTERED – kombinaci má už
    /// zaregistrovanou jiný proces přes stejné Win32 API, nebo je rezervovaná
    /// přímo Windows (obojí vypadá z pohledu RegisterHotKey stejně, nedá se to
    /// od sebe rozlišit).
    /// </summary>
    AlreadyRegisteredElsewhere,

    /// <summary>Neočekávané selhání z jiného důvodu.</summary>
    Failed,
}
