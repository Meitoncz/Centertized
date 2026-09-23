using Centertized.Core.WindowManagement;

namespace Centertized.Tests.WindowManagement;

public class ResizedCenteringTests
{
    private static readonly WindowRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void CalculateResized_NoBorder_CentersNewSize()
    {
        var current = new WindowRect(100, 100, 900, 700);

        var bounds = WindowCenteringCalculator.CalculateResized(WorkArea, current, current, 1000, 500);

        Assert.Equal(1000, bounds.Width);
        Assert.Equal(500, bounds.Height);
        Assert.Equal(460, bounds.Left); // (1920-1000)/2
        Assert.Equal(270, bounds.Top);  // (1040-500)/2
    }

    [Fact]
    public void CalculateResized_WithInvisibleBorder_CentersTheVisibleArea()
    {
        // Viditelné okno 800x600, GetWindowRect je o 8 px větší na každé straně.
        var visual = new WindowRect(108, 108, 908, 708);
        var rect = new WindowRect(100, 100, 916, 716);

        var bounds = WindowCenteringCalculator.CalculateResized(WorkArea, visual, rect, 1016, 616); // vizuálně 1000x600

        // Vizuální plocha = bounds zmenšené o 8 px okraj; ta se musí trefit na střed.
        var visualLeft = bounds.Left + 8;
        var visualRight = bounds.Right - 8;
        var visualTop = bounds.Top + 8;
        var visualBottom = bounds.Bottom - 8;
        Assert.Equal(1920 - visualRight, visualLeft);
        Assert.Equal(1040 - visualBottom, visualTop);
    }

    [Fact]
    public void CalculateResized_LargerThanWorkArea_IsClampedToFit()
    {
        var current = new WindowRect(0, 0, 800, 600);

        var bounds = WindowCenteringCalculator.CalculateResized(WorkArea, current, current, 5000, 5000);

        Assert.Equal(1920, bounds.Width);
        Assert.Equal(1040, bounds.Height);
        Assert.Equal(0, bounds.Left);
        Assert.Equal(0, bounds.Top);
    }

    [Fact]
    public void CalculateResized_OnSecondaryMonitor_CentersOnThatMonitor()
    {
        var secondMonitor = new WindowRect(1920, 0, 3840, 1040);
        var current = new WindowRect(2000, 100, 2800, 700);

        var bounds = WindowCenteringCalculator.CalculateResized(secondMonitor, current, current, 800, 600);

        Assert.Equal(1920 + 560, bounds.Left);
        Assert.Equal(220, bounds.Top);
    }
}
