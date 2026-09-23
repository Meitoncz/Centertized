using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Centertized.Services;

public sealed record NativeMenuItem(string IconGlyph, string Text, Action Invoke);

/// <summary>
/// Skutečné nativní Windows menu (Win32 popup menu, TrackPopupMenu) - totéž, co používá Explorer a
/// ostatní tray aplikace. Ve Windows 11 má samo zaoblené rohy, ostrý text a nativní hover; žádný
/// WPF ContextMenu (ten je průhledné okno = rozmazaný text, vlastní focus rámeček...).
/// Tmavý režim menu se zapíná neveřejným (ale všude používaným) uxtheme API, viz <see cref="ApplyMenuTheme"/>.
/// </summary>
public static class NativeTrayMenu
{
    private const uint MIIM_ID = 0x002;
    private const uint MIIM_STRING = 0x040;
    private const uint MIIM_BITMAP = 0x080;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTALIGN = 0x0008;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_NULL = 0x0000;

    private static HwndSource? _owner;

    public static void Show(IReadOnlyList<NativeMenuItem> items, bool dark, (int X, int Y)? at = null)
    {
        _owner ??= new HwndSource(new HwndSourceParameters("CentertizedMenuOwner") { Width = 0, Height = 0, WindowStyle = 0 });
        var owner = _owner.Handle;

        ApplyMenuTheme(dark);

        var menu = CreatePopupMenu();
        var bitmaps = new List<IntPtr>();
        var strings = new List<IntPtr>();
        try
        {
            var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window()).DpiScaleX;
            for (var i = 0; i < items.Count; i++)
            {
                var bitmap = CreateGlyphBitmap(items[i].IconGlyph, dpi, dark);
                bitmaps.Add(bitmap);
                var text = Marshal.StringToHGlobalUni(items[i].Text);
                strings.Add(text);

                var info = new MENUITEMINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                    fMask = MIIM_ID | MIIM_STRING | MIIM_BITMAP,
                    wID = (uint)(i + 1),
                    dwTypeData = text,
                    hbmpItem = bitmap,
                };
                InsertMenuItem(menu, (uint)i, true, ref info);
            }

            GetCursorPos(out var cursor);
            var x = at?.X ?? cursor.X;
            var y = at?.Y ?? cursor.Y;

            // Bez SetForegroundWindow by se menu po kliknutí mimo nezavřelo (známý tray quirk).
            SetForegroundWindow(owner);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RIGHTALIGN, x, y, owner, IntPtr.Zero);
            PostMessage(owner, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (command > 0 && command <= items.Count)
            {
                // Až po zavření menu - položka typicky otevírá okno, které si bere fokus.
                var chosen = items[command - 1];
                Application.Current.Dispatcher.BeginInvoke(chosen.Invoke);
            }
        }
        finally
        {
            DestroyMenu(menu);
            bitmaps.ForEach(b => DeleteObject(b));
            strings.ForEach(Marshal.FreeHGlobal);
        }
    }

    // uxtheme #135 SetPreferredAppMode / #136 FlushMenuThemes - neveřejné ordinály, které pro tmavá
    // Win32 menu používá např. Notepad++ nebo Windows Terminal. Při selhání zůstane světlé menu.
    private static void ApplyMenuTheme(bool dark)
    {
        try
        {
            SetPreferredAppMode(dark ? 2 : 3); // ForceDark / ForceLight
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Starší Windows bez těchhle ordinálů.
        }
    }

    // Ikona položky: glyph z Segoe Fluent Icons vykreslený do 32bit ARGB bitmapy v barvě textu menu.
    private static IntPtr CreateGlyphBitmap(string glyph, double dpi, bool dark)
    {
        var size = (int)Math.Round(16 * dpi);
        var brush = dark ? Brushes.White : Brushes.Black;
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe Fluent Icons"), size * 0.9, brush, 1.0);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[size * size * 4];
        target.CopyPixels(pixels, size * 4, 0);

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool InsertMenuItem(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO info);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
