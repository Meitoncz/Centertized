using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

    public static NewWindowWatcher NewWindowWatcher { get; private set; } = null!;

    public static AppRulesService AppRules { get; private set; } = null!;

    public static IWin32WindowService WindowService { get; private set; } = null!;

    /// <summary>Kopie AppSettings.RememberWindowSizes - čte se z vláken watcheru, proto ne přímo z disku.</summary>
    public static volatile bool RememberWindowSizes = true;

    public static WindowSizeLearner SizeLearner { get; private set; } = null!;

    public static UpdateService Updates { get; } = new();

    public static void LogUpdateFailure(Exception exception) =>
        Log.Warning(exception, "Kontrola/stažení aktualizace selhalo.");

    private AboutWindow? _aboutWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Po přepnutí admin režimu appka spouští novou instanci a stará se ještě chvíli zavírá -
        // taková instance (--relaunch) si na uvolnění zámku počká, jiná by hned skončila.
        var relaunching = e.Args.Contains(ElevationService.RelaunchArgument);
        var createdNew = false;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // Zámek vlastní instance se zvýšenými právy a ten neprivilegovaný proces otevřít nesmí -
            // je to tedy stejně "už běží jiná instance".
            _singleInstanceMutex = null;
        }

        if (!createdNew && relaunching && _singleInstanceMutex is not null)
        {
            try
            {
                createdNew = _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                createdNew = true; // předchozí instanci někdo "zabil", zámek je ale volný
            }
        }

        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            // Appka už jednou běží – tahle instance nemá co dělat. Mutex nikdy
            // nezískala do vlastnictví, takže se v OnExit nesmí volat ReleaseMutex.
            Shutdown();
            return;
        }

        SettingsStore = new JsonSettingsStore();
        var startupSettings = SettingsStore.Load();

        // Admin režim zapnutý, ale appka běží bez zvýšených práv (např. spuštěná ručně) - přepnout se
        // do zvýšené instance. Když uživatel UAC odmítne, běží se dál bez ní.
        if (startupSettings.RunAsAdministrator && !ElevationService.IsElevated && ElevationService.RelaunchElevated())
        {
            ReleaseSingleInstanceMutex();
            Shutdown();
            return;
        }

        // Spouštění s Windows u zvýšené appky jde přes úlohu plánovače (Run klíč by UAC ukázal při
        // každém přihlášení) - při startu se případně převede.
        if (ElevationService.IsElevated && startupSettings.RunAsAdministrator)
        {
            new AutostartService().Migrate(useElevatedTask: true);
        }
        RememberWindowSizes = startupSettings.RememberWindowSizes;
        Loc.Apply(startupSettings.Language);
        ThemeService.Apply(startupSettings.ThemeMode);
        ThemeService.WatchSystemTheme();

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = LoadTrayIcon();
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

        // Jen pro ruční/skriptované ověření vzhledu menu - v tray se pravým klikem
        // nedá nasnímat ze skriptu, tak se menu otevře na pevných souřadnicích.
        _ = CheckForUpdatesOnStartupAsync(startupSettings.CheckForUpdatesAutomatically);

        if (e.Args.Contains("--show-about"))
        {
            ShowAboutWindow();
        }

        if (e.Args.Contains("--show-picker"))
        {
            new InstalledAppsWindow().Show();
        }

        if (e.Args.Contains("--show-tray-menu"))
        {
            // Nativní menu blokuje, dokud se nezavře - proto až po dokončení startu.
            Dispatcher.BeginInvoke(() => ShowTrayMenu((1000, 760)), DispatcherPriority.ApplicationIdle);
        }
    }

    // ExtractAssociatedIcon vrací vždy jen 32x32, což je na vyšším DPI v tray rozmazané -
    // ICO má víc velikostí, tak si vybereme tu, kterou tray opravdu potřebuje.
    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Resources/icon.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resource.Stream;
        var dpi = VisualTreeHelper.GetDpi(new System.Windows.Controls.Control()).DpiScaleX;
        var size = (int)Math.Round(16 * Math.Max(dpi, 1.0));
        return new System.Drawing.Icon(stream, new System.Drawing.Size(size, size));
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

        var windowService = new Win32WindowService();
        WindowService = windowService;
        AppRules = new AppRulesService(SettingsStore);

        ActionCatalog = new WindowActionCatalog(
        [
            new CenterActiveWindowAction(),
            new ToggleMaximizeAction(),
        ]);
        HotkeyRegistry = new HotkeyActionRegistry(new Win32HotkeyRegistrar(), ActionCatalog, windowService, _logger, _hotkeyMessageSource.Handle);

        var sizePolicy = new RememberedSizePolicy(AppRules, windowService, () => RememberWindowSizes);
        NewWindowWatcher = new NewWindowWatcher(windowService, _logger, AppRules, sizePolicy);
        SizeLearner = new WindowSizeLearner(windowService, AppRules, _logger);

        var settings = SettingsStore.Load();
        if (settings.AutoCenterNewWindows)
        {
            NewWindowWatcher.Start();
        }

        if (settings.RememberWindowSizes)
        {
            SizeLearner.Start();
        }

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

    // Nativní Win32 menu (viz NativeTrayMenu) místo WPF ContextMenu.
    private void TrayIcon_RightClick(object sender, RoutedEventArgs e) => ShowTrayMenu();

    // Tichá kontrola po startu (jen v nainstalované appce). Když je nová verze, oznámí se balonkem;
    // stažení a restart si uživatel spustí sám v Nastavení, nic se neinstaluje bez jeho vědomí.
    private async Task CheckForUpdatesOnStartupAsync(bool enabled)
    {
        if (!enabled || !Updates.IsInstalled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (await Updates.CheckAsync())
            {
                await Dispatcher.InvokeAsync(() => _trayIcon?.ShowNotification(
                    "Centertized",
                    Loc.Format("Notice.UpdateAvailable", Updates.PendingVersion ?? ""),
                    H.NotifyIcon.Core.NotificationIcon.Info));
            }
        }
        catch (Exception ex)
        {
            LogUpdateFailure(ex);
        }
    }

    private void ShowTrayMenu((int X, int Y)? forcedPosition = null)
    {
        // Glyphy ze Segoe Fluent Icons: OpenInNewWindow, Info, PowerButton.
        var items = new List<NativeMenuItem>
        {
            new("", Loc.Get("Tray.Open"), ShowSettingsWindow),
            new("", Loc.Get("Tray.About"), ShowAboutWindow),
            new("", Loc.Get("Tray.Close"), ExitApplication),
        };

        NativeTrayMenu.Show(items, ThemeService.IsDark, forcedPosition);
    }

    private void ShowAboutWindow()
    {
        if (_aboutWindow is { IsLoaded: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

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

    /// <summary>
    /// Volá SettingsWindow při prvním schování okna (zavření křížkem) - v tu chvíli
    /// appka "zmizí" a je dobré jednou upozornit, že běží dál v tray liště.
    /// </summary>
    public static void ShowTrayHintIfNeeded()
    {
        var app = (App)Current;
        var settings = SettingsStore.Load();
        if (settings.HasShownTrayHint)
        {
            return;
        }

        settings.HasShownTrayHint = true;
        SettingsStore.Save(settings);

        app._trayIcon?.ShowNotification(
            "Centertized",
            Loc.Get("Tray.StillRunning"),
            H.NotifyIcon.Core.NotificationIcon.Info);
    }

    /// <summary>Ukončí appku a spustí novou se zvýšenými právy / bez nich (viz nastavení "Spustit jako administrátor").</summary>
    public static bool RestartAs(bool elevated)
    {
        var app = (App)Current;
        if (elevated)
        {
            if (!ElevationService.RelaunchElevated())
            {
                return false;
            }
        }
        else
        {
            ElevationService.RelaunchNotElevated();
        }

        app.ReleaseSingleInstanceMutex();
        app.ExitApplication();
        return true;
    }

    // Nová instance musí zámek dostat hned, ne až po dokončení OnExit.
    private void ReleaseSingleInstanceMutex()
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }
    }

    private void ExitApplication()
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
        NewWindowWatcher?.Dispose();
        SizeLearner?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
