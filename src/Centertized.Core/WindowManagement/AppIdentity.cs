namespace Centertized.Core.WindowManagement;

/// <summary>
/// Identita aplikace, které okno patří. <paramref name="Key"/> je název .exe malými písmeny
/// (u UWP appek skutečné .exe appky, ne ApplicationFrameHost.exe, který okno jen hostuje),
/// <paramref name="DisplayName"/> je čitelný název pro UI, <paramref name="ExecutablePath"/> cesta k .exe (když je známá).
/// </summary>
public sealed record AppIdentity(string Key, string DisplayName, string? ExecutablePath = null);
