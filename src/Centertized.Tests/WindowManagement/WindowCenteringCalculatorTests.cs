using Centertized.Core.WindowManagement;

namespace Centertized.Tests.WindowManagement;

public class WindowCenteringCalculatorTests
{
    [Fact]
    public void Calculate_NoBorderPadding_CentersExactlyInWorkArea()
    {
        var workArea = new WindowRect(0, 0, 1920, 1040);
        // visualBounds == windowRect => no invisible border.
        var windowRect = new WindowRect(100, 100, 900, 700); // 800x600

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(560, left); // (1920-800)/2
        Assert.Equal(220, top); // (1040-600)/2
    }

    [Fact]
    public void Calculate_WithInvisibleBorderPadding_CompensatesForIt()
    {
        var workArea = new WindowRect(0, 0, 1920, 1040);
        var visualBounds = new WindowRect(100, 100, 900, 700); // 800x600 visible
        var windowRect = new WindowRect(92, 92, 908, 708); // 8px larger on each side

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, visualBounds, windowRect);

        // The goal is for the VISUAL bounds to end up at (560, 220) - but SetWindowPos
        // positions by the windowRect "language", so the 8px border has to be subtracted.
        Assert.Equal(552, left);
        Assert.Equal(212, top);
    }

    [Fact]
    public void Calculate_SecondMonitorWithNegativeOrigin_HandlesNegativeCoordinates()
    {
        // A monitor to the left of the primary one has negative X coordinates in the virtual desktop.
        var workArea = new WindowRect(-1920, 0, 0, 1080);
        var windowRect = new WindowRect(-1800, 50, -1000, 650); // 800x600

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(-1360, left); // -1920 + (1920-800)/2
        Assert.Equal(240, top); // 0 + (1080-600)/2
    }

    [Fact]
    public void Calculate_DifferentWorkAreaSize_MimicsDifferentDpiScaling()
    {
        // A smaller effective work area, as if a monitor with a higher DPI scale reported
        // less available space in (aware) coordinates.
        var workArea = new WindowRect(2000, 0, 2000 + 1280, 800);
        var windowRect = new WindowRect(2100, 100, 2500, 400); // 400x300

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(2000 + (1280 - 400) / 2, left);
        Assert.Equal(0 + (800 - 300) / 2, top);
    }

    [Fact]
    public void Calculate_WindowLargerThanWorkArea_ProducesOffsetBeyondEdge()
    {
        // A window larger than the work area - the centering comes out off the monitor, which is
        // fine/expected (SetWindowPos with NOSIZE doesn't shrink it).
        var workArea = new WindowRect(0, 0, 800, 600);
        var windowRect = new WindowRect(0, 0, 1000, 700);

        var (left, top) = WindowCenteringCalculator.Calculate(workArea, windowRect, windowRect);

        Assert.Equal(-100, left);
        Assert.Equal(-50, top);
    }
}
