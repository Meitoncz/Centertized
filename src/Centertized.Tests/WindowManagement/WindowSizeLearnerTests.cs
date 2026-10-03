using Centertized.Core.WindowManagement;

namespace Centertized.Tests.WindowManagement;

public class WindowSizeLearnerTests
{
    private static readonly WindowRect Work = new(0, 0, 3840, 2100);

    [Theory]
    [InlineData(1920, 2100)] // half, tall (Aero Snap left/right)
    [InlineData(1280, 2100)] // third
    [InlineData(3840, 1050)] // half, wide
    [InlineData(1920, 1050)] // quarter
    [InlineData(3840, 2100)] // the whole area
    public void SnappedOrFullscreenSizes_AreNotLearned(int width, int height)
    {
        var rect = new WindowRect(0, 0, width, height);

        Assert.True(WindowSizeLearner.LooksSnappedOrFullscreen(rect, Work));
    }

    [Theory]
    [InlineData(1500, 1100)]
    [InlineData(900, 700)]
    [InlineData(2400, 1400)]
    public void OrdinaryUserSizes_AreLearned(int width, int height)
    {
        var rect = new WindowRect(100, 100, 100 + width, 100 + height);

        Assert.False(WindowSizeLearner.LooksSnappedOrFullscreen(rect, Work));
    }
}
