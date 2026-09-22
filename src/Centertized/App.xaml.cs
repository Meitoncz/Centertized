using System.IO;
using System.Windows;
using System.Windows.Interop;
using Centertized.Core.Actions;
using Centertized.Core.Hotkeys;
using Centertized.Core.Settings;
using Centertized.Views;
using H.NotifyIcon;

namespace Centertized;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Pevně dané GUID – slouží jen k odlišení naší appky v systému, nikdy neměnit.
    private const string SingleInstanceMutexName = "Global\\Centertized-9F1B1C2E-6C3B-4B7E-9A0E-9D6E9E7B2B10";
    private const int WM_HOTKEY = 0x0312;

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private HwndSource? _hotkeyMessageSource;

    /// <summary>
    /// Bez DI kontejneru – appka je malá, takže sdílené instance (registry,
    /// settings store, katalog akcí) jsou prostě statické, dostupné odkudkoliv
    /// v UI projektu (Views/Controls).
    /// </summary>
    public static HotkeyActionRegistry HotkeyRegistry { get; private set; } = null!;

    public static ISettingsStore SettingsStore { get; private set; } = null!;

    public static WindowActionCatalog ActionCatalog { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dočasné – plnohodnotné Serilog logování přijde ve Fázi 4. Zatím aspoň
        // tohle, ať nespadlé výjimky nezmizí beze stopy (appka nemá konzoli).
        DispatcherUnhandledException += (_, args) => WriteCrashLog(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteCrashLog(ex);
            }
        };

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            // Appka už jednou běží – tahle instance nemá co dělat. Mutex nikdy
            // nezískala do vlastnictví, takže se v OnExit nesmí volat ReleaseMutex.
            Shutdown();
            return;
        }

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = System.Drawing.SystemIcons.Application; // TODO Fáze 4: nahradit vlastní ikonou appky
        _trayIcon.ForceCreate();

        InitializeHotkeys();
    }

    private void InitializeHotkeys()
    {
        // Skryté message-only okno jen pro příjem WM_HOTKEY – HWND_MESSAGE (-3) jako
        // parent zajistí, že nemá vizuální stopu (žádné okno, žádná ikona na taskbaru).
        var parameters = new HwndSourceParameters("CentertizedHotkeySink")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),
        };
        _hotkeyMessageSource = new HwndSource(parameters);
        _hotkeyMessageSource.AddHook(HotkeyWndProc);

        ActionCatalog = new WindowActionCatalog([new CenterActiveWindowAction()]);
        HotkeyRegistry = new HotkeyActionRegistry(new Win32HotkeyRegistrar(), ActionCatalog, _hotkeyMessageSource.Handle);
        SettingsStore = new JsonSettingsStore();

        var settings = SettingsStore.Load();
        foreach (var (actionId, hotkeyText) in settings.Hotkeys)
        {
            if (ActionCatalog.TryGetById(actionId) is null)
            {
                continue; // zkratka pro akci, která už v appce neexistuje (např. po update)
            }

            if (!Hotkey.TryParse(hotkeyText, out var hotkey))
            {
                WriteCrashLog(new FormatException($"Uložená zkratka '{hotkeyText}' pro akci '{actionId}' se nepodařila naparsovat."));
                continue;
            }

            var result = HotkeyRegistry.TryBind(actionId, hotkey);
            if (result.Outcome != HotkeyRegistrationOutcome.Success)
            {
                // TODO Fáze 4: místo logu tray notifikace, ať si toho uživatel všimne.
                WriteCrashLog(new InvalidOperationException($"Nepodařilo se znovu zaregistrovat zkratku '{hotkeyText}' pro akci '{actionId}': {result.Outcome}."));
            }
        }
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyRegistry.Dispatch(wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // SettingsWindow se zavřením jen schová (viz OnClosing tam), takže tahle
        // instance po prvním otevření žije po celou dobu běhu appky.
        _settingsWindow ??= new SettingsWindow();

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Bez tohohle by Shutdown() níž narazil na SettingsWindow.OnClosing, ten by
        // zavření zrušil (Cancel = true) a appka by se korektně neukončila.
        if (_settingsWindow is not null)
        {
            _settingsWindow.AllowClose = true;
        }

        Shutdown();
    }

    private static void WriteCrashLog(Exception ex)
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Centertized", "logs");
        Directory.CreateDirectory(logDir);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(logDir, "crash.log"), line);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Bez explicitního Dispose by po ukončení appky mohla v tray liště zůstat
        // "duch" ikona až do prvního najetí myší na její místo.
        _trayIcon?.Dispose();
        _hotkeyMessageSource?.Dispose(); // uvolní i všechny RegisterHotKey registrace na tomhle okně
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
