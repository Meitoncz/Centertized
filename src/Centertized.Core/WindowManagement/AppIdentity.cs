namespace Centertized.Core.WindowManagement;

/// <summary>
/// Identity of the app a window belongs to. <paramref name="Key"/> is the lowercase .exe name
/// (for UWP apps the real app's .exe, not ApplicationFrameHost.exe, which only hosts the window),
/// <paramref name="DisplayName"/> is a readable name for the UI, <paramref name="ExecutablePath"/> the path to the .exe (when known).
/// </summary>
public sealed record AppIdentity(string Key, string DisplayName, string? ExecutablePath = null);
