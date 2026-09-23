namespace Centertized.Core.Settings;

/// <summary>
/// Pravidla pro jednu aplikaci (klíč = název .exe malými písmeny, viz AppIdentity).
/// Rozměry jsou v "96 DPI jednotkách", ne ve fyzických pixelech - jinak by se zapamatovaná
/// velikost na monitoru s jiným škálováním zobrazila jinak velká.
/// </summary>
public sealed class AppRule
{
    public string DisplayName { get; set; } = "";

    /// <summary>Nové okno téhle appky se nemá automaticky centrovat.</summary>
    public bool ExcludedFromAutoCenter { get; set; }

    public int? RememberedWidth { get; set; }

    public int? RememberedHeight { get; set; }

    public bool HasRememberedSize => RememberedWidth is > 0 && RememberedHeight is > 0;

    /// <summary>Pravidlo, které nic nenastavuje, nemá smysl držet v nastavení.</summary>
    public bool IsEmpty => !ExcludedFromAutoCenter && !HasRememberedSize;
}
