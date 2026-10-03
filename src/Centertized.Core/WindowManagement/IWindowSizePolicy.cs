namespace Centertized.Core.WindowManagement;

/// <summary>
/// Decides whether a window also gets a specific size when centered (the app's remembered
/// size). Separated from the centering logic so it can be turned off/tested on its own.
/// </summary>
public interface IWindowSizePolicy
{
    /// <returns>True and the physical window dimensions (GetWindowRect, in pixels) if the size should be set.</returns>
    bool TryGetTargetSize(IntPtr windowHandle, out int widthPixels, out int heightPixels);
}
