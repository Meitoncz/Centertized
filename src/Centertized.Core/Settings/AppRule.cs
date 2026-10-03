using System.Text.Json.Serialization;

namespace Centertized.Core.Settings;

/// <summary>
/// Rules for one app (key = lowercase .exe name, see AppIdentity).
/// Dimensions are in "96 DPI units", not physical pixels - otherwise a remembered
/// size would look different on a monitor with different scaling.
/// </summary>
public sealed class AppRule
{
    public string DisplayName { get; set; } = "";

    /// <summary>New windows of this app must not be centered automatically.</summary>
    public bool ExcludedFromAutoCenter { get; set; }

    /// <summary>Dominant color of the app's icon ("#RRGGBB") for the colored dot in the exceptions list.</summary>
    public string? AccentColor { get; set; }

    public int? RememberedWidth { get; set; }

    public int? RememberedHeight { get; set; }

    [JsonIgnore]
    public bool HasRememberedSize => RememberedWidth is > 0 && RememberedHeight is > 0;

    /// <summary>A rule that sets nothing isn't worth keeping in the settings.</summary>
    [JsonIgnore]
    public bool IsEmpty => !ExcludedFromAutoCenter && !HasRememberedSize;
}
