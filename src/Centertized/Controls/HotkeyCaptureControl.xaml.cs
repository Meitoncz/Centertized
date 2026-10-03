using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Centertized.Core.Hotkeys;
using Centertized.Services;

namespace Centertized.Controls;

/// <summary>
/// Captures a combination of modifier(s) + key and reports it via
/// <see cref="HotkeyCaptured"/>. The actual TryBind/conflicts/saving is handled by the window
/// that uses the control – this control only "listens to the keyboard".
/// </summary>
public partial class HotkeyCaptureControl : UserControl
{
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText), typeof(string), typeof(HotkeyCaptureControl),
        new PropertyMetadata(null));

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public event EventHandler<Hotkey>? HotkeyCaptured;

    public HotkeyCaptureControl()
    {
        InitializeComponent();
        DisplayText = Loc.Get("Hotkey.NotSet");
        CaptureButton.PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt combinations arrive as Key.System with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key))
        {
            return;
        }

        var modifiers = ToHotkeyModifiers(Keyboard.Modifiers);
        if (modifiers == HotkeyModifiers.None)
        {
            // A shortcut without a modifier would collide with normal typing, so it is
            // not even tried to be registered.
            DisplayText = Loc.Get("Hotkey.NeedsModifier");
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

        // Keyboard.Modifiers is unreliable for the Win key (the shell often
        // handles it with a special low-level hook before it reaches WPF), so
        // fallback to a direct GetAsyncKeyState. NOTE: this only catches Win +
        // keys the shell doesn't claim itself (function keys such as Win+F12 work
        // reliably) - Win + letter/number is claimed by the shell at the OS level (Start
        // menu, Explorer, etc.) and a normal app never gets it at all.
        // We tried to solve it with a temporary global low-level keyboard hook
        // that would suppress it - but on an error/imperfect cleanup it risks locking the
        // keyboard system-wide (that happened during testing), so we deliberately backed
        // away from it. Win + letter/number therefore stays unsupported.
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
