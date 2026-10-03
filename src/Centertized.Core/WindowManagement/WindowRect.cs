namespace Centertized.Core.WindowManagement;

/// <summary>Our own counterpart of the Win32 RECT – no dependency on WPF/WinForms types.</summary>
public readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
