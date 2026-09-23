using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Centertized.Core.Hotkeys;

namespace Centertized.Controls;

/// <summary>
/// Zachytí kombinaci modifikátor(y) + klávesa a nahlásí ji přes
/// <see cref="HotkeyCaptured"/>. Samotné TryBind/konflikty/ukládání řeší okno,
/// které control používá – tenhle control jen "poslouchá klávesnici".
///
/// Zachytávání běží přes dočasný low-level keyboard hook
/// (<c>WH_KEYBOARD_LL</c>), instalovaný jen po dobu, kdy má tlačítko fokus - ne
/// přes normální WPF PreviewKeyDown. Důvod: Win klávesa (Windows key) je na
/// úrovni OS zvlášť zpracovaná shellem (proto po jejím stisku běžně vyjede Start
/// menu) a normální WPF klávesnicové eventy ji nikdy spolehlivě nedostanou, ani
/// jako modifikátor u jiné klávesy. Hook navíc stisk potlačí (vrátí 1 =
/// "zpracováno"), takže se Start menu při zachytávání zkratky vůbec neotevře.
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

    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;

    public HotkeyCaptureControl()
    {
        InitializeComponent();
        CaptureButton.GotKeyboardFocus += (_, _) => InstallHook();
        CaptureButton.LostKeyboardFocus += (_, _) => UninstallHook();
        Unloaded += (_, _) => UninstallHook();
    }

    private void InstallHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _hookProc = HookCallback;
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(currentModule.ModuleName), 0);
    }

    private void UninstallHook()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        var messageId = wParam.ToInt32();
        if (code >= 0 && (messageId == WM_KEYDOWN || messageId == WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var virtualKeyCode = (int)data.vkCode;

            // Zpracovat asynchronně na UI vlákně - hook callback musí být rychlý
            // a bez výjimek, jinak ho Windows po pár stech ms sám odregistruje.
            Dispatcher.BeginInvoke(new Action(() => ProcessCapturedKey(virtualKeyCode)));

            // Potlačit - dál to nejde do shellu (žádné Start menu, žádné jiné
            // globální zkratky), dokud má tenhle control fokus.
            return (IntPtr)1;
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private void ProcessCapturedKey(int virtualKeyCode)
    {
        if (IsModifierOnly(virtualKeyCode))
        {
            return;
        }

        var modifiers = ReadCurrentModifiers();
        if (modifiers == HotkeyModifiers.None)
        {
            // Zkratka bez modifikátoru by kolidovala s normálním psaním, proto se
            // vůbec nezkouší zaregistrovat.
            DisplayText = "Needs Ctrl/Alt/Shift/Win";
            return;
        }

        HotkeyCaptured?.Invoke(this, new Hotkey(modifiers, (uint)virtualKeyCode));

        // Fokus pryč z tlačítka -> LostKeyboardFocus odregistruje hook. Bez tohohle
        // by hook (a tedy potlačování všech kláves v celém systému) zůstal aktivní
        // i po úspěšném zachycení, dokud by uživatel neklikl/tabnul jinam.
        Keyboard.ClearFocus();
    }

    private static bool IsModifierOnly(int virtualKeyCode) => virtualKeyCode is
        VK_LCONTROL or VK_RCONTROL or VK_CONTROL or
        VK_LMENU or VK_RMENU or VK_MENU or
        VK_LSHIFT or VK_RSHIFT or VK_SHIFT or
        VK_LWIN or VK_RWIN;

    private static HotkeyModifiers ReadCurrentModifiers()
    {
        var result = HotkeyModifiers.None;
        if (IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL))
        {
            result |= HotkeyModifiers.Control;
        }

        if (IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN))
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }

    private static bool IsKeyDown(int virtualKeyCode) => (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;

    // --- Win32 P/Invoke ---

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_CONTROL = 0x11;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
