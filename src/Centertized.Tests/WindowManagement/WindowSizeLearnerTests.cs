using Centertized.Core.WindowManagement;

namespace Centertized.Tests.WindowManagement;

public class WindowSizeLearnerTests
{
    private static readonly WindowRect Work = new(0, 0, 3840, 2100);

    [Theory]
    [InlineData(1920, 2100)] // půlka na výšku (Aero Snap doleva/doprava)
    [InlineData(1280, 2100)] // třetina
    [InlineData(3840, 1050)] // půlka na šířku
    [InlineData(1920, 1050)] // čtvrtina
    [InlineData(3840, 2100)] // přes celou plochu
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
