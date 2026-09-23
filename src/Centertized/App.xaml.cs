using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Centertized.Core.Actions;
using Centertized.Core.Hotkeys;
using Centertized.Core.Settings;
using Centertized.Core.WindowManagement;
using Centertized.Services;
using Centertized.Views;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

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
    private ILogger _logger = null!;

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
        // Ikona appky je zapsaná jako <ApplicationIcon> v csproj (jde tedy i do .exe
        // resource), tady se prostě znovu použije - žádná zvlášť kopírovaná kopie.
        _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location)
            ?? System.Drawing.SystemIcons.Application;
        _trayIcon.ForceCreate();

        // Tray notification sink potřebuje hotovou tray ikonu, proto se Serilog
        // konfiguruje až tady, ne úplně na začátku OnStartup.
        ConfigureLogging();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        InitializeHotkeys();

        // Výchozí chování appky je ukázat Settings okno po startu (uživatel si
        // "spustit minimalizované" může zapnout v Nastavení). --settings navíc
        // vynutí zobrazení i přes StartMinimized - užitečné pro rychlé testování.
        var startMinimized = SettingsStore.Load().StartMinimized;
        if (!startMinimized || e.Args.Contains("--settings"))
        {
            ShowSettingsWindow();
        }
    }

    private void ConfigureLogging()
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Centertized", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "centertized-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .WriteTo.Sink(new TrayNotificationSink(_trayIcon!))
            .CreateLogger();

        // dispose: false - o ukončení Log.Logger se stará explicitně OnExit (Log.CloseAndFlush).
        _logger = new SerilogLoggerFactory(Log.Logger, dispose: false).CreateLogger("Centertized");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Neošetřená výjimka na UI vlákně.");
        // Appka běží na pozadí bez konzole - jedna spadlá akce/stránka nemá shodit
        // celý proces, uživatel by o tom ani nevěděl.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger.LogCritical(ex, "Neošetřená výjimka mimo UI vlákno - appka teď spadne.");
        }

        Log.CloseAndFlush();
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

        ActionCatalog = new WindowActionCatalog([new CenterActiveWindowAction(), new ToggleMaximizeAction()]);
        HotkeyRegistry = new HotkeyActionRegistry(new Win32HotkeyRegistrar(), ActionCatalog, new Win32WindowService(), _logger, _hotkeyMessageSource.Handle);
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
                _logger.LogWarning("Uložená zkratka '{HotkeyText}' pro akci '{ActionId}' se nepodařila naparsovat.", hotkeyText, actionId);
                continue;
            }

            var result = HotkeyRegistry.TryBind(actionId, hotkey);
            if (result.Outcome != HotkeyRegistrationOutcome.Success)
            {
                _logger.LogWarning(
                    "Nepodařilo se znovu zaregistrovat zkratku '{HotkeyText}' pro akci '{ActionId}': {Outcome}.",
                    hotkeyText, actionId, result.Outcome);
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

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => ShowSettingsWindow();

    // Běžná konvence tray appek - dvojklik levým tlačítkem otevře hlavní/Settings okno.
    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e) => ShowSettingsWindow();

    private void ShowSettingsWindow()
    {
        // Zavřením se okno jen schová (viz SettingsWindow.OnClosing) - živé
        // přebarvení teď funguje (žádné NavigationView, viz SettingsWindow.xaml),
        // takže se stejná instance dá bezpečně držet po celou dobu běhu appky a
        // další otevření je okamžité.
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
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
