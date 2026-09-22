using Centertized.Core.WindowManagement;

namespace Centertized.Tests.WindowManagement;

public class WindowCenteringCalculatorTests
{
    [Fact]
    public void Calculate_NoBorderPadding_CentersExactlyInWorkArea()
    {
        var workArea = new WindowRect(0, 0, 1920, 1040);
        // visualBounds == windowRect => žádný neviditelný okraj.
        var windowRect = new WindowRect(100, 100, 900, 700); // 800x600

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(560, left); // (1920-800)/2
        Assert.Equal(220, top); // (1040-600)/2
    }

    [Fact]
    public void Calculate_WithInvisibleBorderPadding_CompensatesForIt()
    {
        var workArea = new WindowRect(0, 0, 1920, 1040);
        var visualBounds = new WindowRect(100, 100, 900, 700); // 800x600 viditelně
        var windowRect = new WindowRect(92, 92, 908, 708); // o 8px větší na každou stranu

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, visualBounds, windowRect);

        // Cíl je, aby VIZUÁLNÍ hranice vyšly na (560, 220) - SetWindowPos ale
        // pozicuje podle windowRect "jazyka", takže se musí odečíst 8px okraj.
        Assert.Equal(552, left);
        Assert.Equal(212, top);
    }

    [Fact]
    public void Calculate_SecondMonitorWithNegativeOrigin_HandlesNegativeCoordinates()
    {
        // Monitor nalevo od primárního má ve virtuálním desktopu záporné X souřadnice.
        var workArea = new WindowRect(-1920, 0, 0, 1080);
        var windowRect = new WindowRect(-1800, 50, -1000, 650); // 800x600

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(-1360, left); // -1920 + (1920-800)/2
        Assert.Equal(240, top); // 0 + (1080-600)/2
    }

    [Fact]
    public void Calculate_DifferentWorkAreaSize_MimicsDifferentDpiScaling()
    {
        // Menší efektivní work area, jako by monitor s vyšším DPI scale reportoval
        // menší dostupnou plochu v (aware) souřadnicích.
        var workArea = new WindowRect(2000, 0, 2000 + 1280, 800);
        var windowRect = new WindowRect(2100, 100, 2500, 400); // 400x300

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(2000 + (1280 - 400) / 2, left);
        Assert.Equal(0 + (800 - 300) / 2, top);
    }

    [Fact]
    public void Calculate_WindowLargerThanWorkArea_ProducesOffsetBeyondEdge()
    {
        // Okno větší než pracovní plocha - centrování vyjde mimo monitor, což je
        // v pořádku/očekávané (SetWindowPos s NOSIZE ho nezmenší).
        var workArea = new WindowRect(0, 0, 800, 600);
        var windowRect = new WindowRect(0, 0, 1000, 700);

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(-100, left);
        Assert.Equal(-50, top);
    }
}
