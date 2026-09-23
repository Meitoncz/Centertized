using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Centertized.Core.Hotkeys;

namespace Centertized.Controls;

/// <summary>
/// Zachytí kombinaci modifikátor(y) + klávesa a nahlásí ji přes
/// <see cref="HotkeyCaptured"/>. Samotné TryBind/konflikty/ukládání řeší stránka,
/// která control používá – tenhle control jen "poslouchá klávesnici".
/// </summary>
public partial class HotkeyCaptureControl : UserControl
{
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText), typeof(string), typeof(HotkeyCaptureControl),
        new PropertyMetadata("Not set"));

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public event EventHandler<Hotkey>? HotkeyCaptured;

    public HotkeyCaptureControl()
    {
        // Fokus/hover/klik řeší Button sám nativně a spolehlivě (na rozdíl od
        // dřívějšího ručně-klikacího Border, který uvnitř CardControl vůbec nešlo
        // kliknout) - PreviewKeyDown je napojený deklarativně v XAML.
        InitializeComponent();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt-kombinace přijdou jako Key.System s reálnou klávesou v SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key))
        {
            return;
        }

        var modifiers = ToHotkeyModifiers(Keyboard.Modifiers);
        if (modifiers == HotkeyModifiers.None)
        {
            // Zkratka bez modifikátoru by kolidovala s normálním psaním, proto se
            // vůbec nezkouší zaregistrovat.
            DisplayText = "Needs Ctrl/Alt/Shift/Win";
            return;
        }

        var virtualKeyCode = (uint)KeyInterop.VirtualKeyFromKey(key);
        HotkeyCaptured?.Invoke(this, new Hotkey(modifiers, virtualKeyCode));
    }

    private static bool IsModifierOnly(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        // Keyboard.Modifiers na Win klávesu není spolehlivý - shell si ji často
        // zpracovává zvláštním low-level hookem dřív, než se to k WPF vůbec dostane
        // normální cestou. GetAsyncKeyState na VK_LWIN/VK_RWIN funguje spolehlivě.
        if (modifiers.HasFlag(ModifierKeys.Windows) || IsWindowsKeyPhysicallyDown())
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }

    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsWindowsKeyPhysicallyDown() =>
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
}
