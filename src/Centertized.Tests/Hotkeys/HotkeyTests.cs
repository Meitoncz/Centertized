using Centertized.Core.Hotkeys;

namespace Centertized.Tests.Hotkeys;

public class HotkeyTests
{
    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'C', "Ctrl+Alt+C")]
    [InlineData(HotkeyModifiers.Windows, (uint)'1', "Win+1")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Windows, (uint)'X', "Ctrl+Alt+Shift+Win+X")]
    public void ToString_UsesCanonicalOrder(HotkeyModifiers modifiers, uint vk, string expected)
    {
        var hotkey = new Hotkey(modifiers, vk);
        Assert.Equal(expected, hotkey.ToString());
    }

    [Fact]
    public void ToString_FunctionKey_FormatsAsF12()
    {
        var hotkey = new Hotkey(HotkeyModifiers.Control, 0x7B); // VK_F12
        Assert.Equal("Ctrl+F12", hotkey.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Alt+C")]
    [InlineData("Win+1")]
    [InlineData("Ctrl+Alt+Shift+Win+X")]
    [InlineData("Alt+F4")]
    [InlineData("ctrl+alt+c")]
    public void TryParse_RoundTripsWithToString(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var reparsed));
        Assert.Equal(hotkey, reparsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")] // žádný modifikátor
    [InlineData("Ctrl+")] // chybí klávesa
    [InlineData("Bogus+C")] // neznámý modifikátor
    [InlineData("Ctrl+NotAKey")]
    public void TryParse_RejectsInvalidInput(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'C');
        var b = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'C');
        Assert.Equal(a, b);
    }
}
