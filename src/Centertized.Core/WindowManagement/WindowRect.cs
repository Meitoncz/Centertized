namespace Centertized.Core.WindowManagement;

/// <summary>Vlastní obdoba Win32 RECT – žádná závislost na WPF/WinForms typech.</summary>
public readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
