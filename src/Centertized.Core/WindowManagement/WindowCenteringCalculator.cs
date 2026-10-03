namespace Centertized.Core.WindowManagement;

/// <summary>
/// Pure centering geometry, separated from the Win32 calls so it can be tested on
/// made-up rectangles (1 monitor, 2 monitors with the same/different DPI) without a
/// real window on the screen.
/// </summary>
public static class WindowCenteringCalculator
{
    /// <param name="workArea">Work area of the target monitor (without the taskbar) – rcWork, not rcMonitor.</param>
    /// <param name="visualBounds">Real visual bounds of the window (DWM extended frame bounds).</param>
    /// <param name="windowRect">Window bounds per GetWindowRect – the "language" SetWindowPos speaks.</param>
    /// <returns>Coordinates for SetWindowPos so that the window's visual bounds end up at the center of workArea.</returns>
    public static (int Left, int Top) Calculate(WindowRect workArea, WindowRect visualBounds, WindowRect windowRect)
    {
        var targetVisualLeft = workArea.Left + (workArea.Width - visualBounds.Width) / 2;
        var targetVisualTop = workArea.Top + (workArea.Height - visualBounds.Height) / 2;

        // GetWindowRect is usually a few pixels larger than what is really visible
        // (invisible border for shadow/resize) – but SetWindowPos positions by
        // GetWindowRect coordinates, not by DWM extended frame bounds. The difference between
        // them is constant for a given window, so it can be computed and subtracted.
        var borderLeft = visualBounds.Left - windowRect.Left;
        var borderTop = visualBounds.Top - windowRect.Top;

        return (targetVisualLeft - borderLeft, targetVisualTop - borderTop);
    }

    /// <summary>
    /// Like <see cref="Calculate"/>, but the window is also enlarged/shrunk to the requested size
    /// (GetWindowRect dimensions) and its NEW visual area ends up centered. The invisible borders
    /// of a window are constant for that window, so the new visual area can be derived from them.
    /// The size is limited so that the visual area fits into the work area.
    /// </summary>
    /// <returns>New window bounds in SetWindowPos coordinates (GetWindowRect "language").</returns>
    public static WindowRect CalculateResized(WindowRect workArea, WindowRect visualBounds, WindowRect windowRect, int desiredWidth, int desiredHeight)
    {
        var insetLeft = visualBounds.Left - windowRect.Left;
        var insetTop = visualBounds.Top - windowRect.Top;
        var insetRight = windowRect.Right - visualBounds.Right;
        var insetBottom = windowRect.Bottom - visualBounds.Bottom;

        var width = Math.Clamp(desiredWidth, 1, workArea.Width + insetLeft + insetRight);
        var height = Math.Clamp(desiredHeight, 1, workArea.Height + insetTop + insetBottom);

        var newWindow = new WindowRect(0, 0, width, height);
        var newVisual = new WindowRect(insetLeft, insetTop, width - insetRight, height - insetBottom);
        var (left, top) = Calculate(workArea, newVisual, newWindow);
        return new WindowRect(left, top, left + width, top + height);
    }
}
