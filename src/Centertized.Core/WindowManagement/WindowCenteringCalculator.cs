namespace Centertized.Core.WindowManagement;

/// <summary>
/// Čistá geometrie centrování, oddělená od Win32 volání, ať se dá otestovat na
/// vymyšlených obdélnících (1 monitor, 2 monitory se stejným/různým DPI) bez
/// reálného okna na obrazovce.
/// </summary>
public static class WindowCenteringCalculator
{
    /// <param name="workArea">Pracovní plocha cílového monitoru (bez taskbaru) – rcWork, ne rcMonitor.</param>
    /// <param name="visualBounds">Skutečné vizuální hranice okna (DWM extended frame bounds).</param>
    /// <param name="windowRect">Hranice okna podle GetWindowRect – tímhle "jazykem" mluví SetWindowPos.</param>
    /// <returns>Souřadnice pro SetWindowPos, aby vizuální hranice okna vyšly na střed workArea.</returns>
    public static (int Left, int Top) Calculate(WindowRect workArea, WindowRect visualBounds, WindowRect windowRect)
    {
        var targetVisualLeft = workArea.Left + (workArea.Width - visualBounds.Width) / 2;
        var targetVisualTop = workArea.Top + (workArea.Height - visualBounds.Height) / 2;

        // GetWindowRect bývá o pár pixelů větší než to, co je opravdu vidět
        // (neviditelný okraj pro stín/resize) – SetWindowPos ale pozicuje podle
        // GetWindowRect souřadnic, ne podle DWM extended frame bounds. Rozdíl mezi
        // nimi je pro dané okno konstantní, takže se dá spočítat a odečíst.
        var borderLeft = visualBounds.Left - windowRect.Left;
        var borderTop = visualBounds.Top - windowRect.Top;

        return (targetVisualLeft - borderLeft, targetVisualTop - borderTop);
    }

    /// <summary>
    /// Jako <see cref="Calculate"/>, ale okno se zároveň zvětší/zmenší na požadovanou velikost
    /// (GetWindowRect rozměry) a na střed vyjde jeho NOVÁ vizuální plocha. Neviditelné okraje
    /// okna jsou pro dané okno konstantní, takže se z nich dá odvodit nová vizuální plocha.
    /// Velikost se omezí tak, aby se vizuální plocha vešla do pracovní plochy.
    /// </summary>
    /// <returns>Nové hranice okna v souřadnicích pro SetWindowPos (GetWindowRect "jazyk").</returns>
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
