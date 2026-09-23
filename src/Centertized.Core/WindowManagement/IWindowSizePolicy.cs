namespace Centertized.Core.WindowManagement;

/// <summary>
/// Rozhoduje, jestli se okně při centrování nastaví i konkrétní velikost (zapamatovaná
/// velikost aplikace). Odděleno od centrovací logiky, ať se dá vypnout/testovat zvlášť.
/// </summary>
public interface IWindowSizePolicy
{
    /// <returns>True a fyzické rozměry okna (GetWindowRect, v pixelech), pokud se má velikost nastavit.</returns>
    bool TryGetTargetSize(IntPtr windowHandle, out int widthPixels, out int heightPixels);
}
