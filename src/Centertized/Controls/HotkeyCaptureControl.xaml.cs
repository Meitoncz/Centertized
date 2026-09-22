using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        new PropertyMetadata("Click here, then press a shortcut"));

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public event EventHandler<Hotkey>? HotkeyCaptured;

    public HotkeyCaptureControl()
    {
        InitializeComponent();
        PreviewMouseLeftButtonDown += (_, _) => Focus();
        PreviewKeyDown += OnPreviewKeyDown;
        GotKeyboardFocus += (_, _) => RootBorder.BorderBrush = Brushes.DodgerBlue;
        LostKeyboardFocus += (_, _) => RootBorder.BorderBrush = Brushes.Gray;
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
            DisplayText = "Hold at least one of Ctrl / Alt / Shift / Win";
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

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }
}
