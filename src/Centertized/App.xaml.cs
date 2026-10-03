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
    // Fixed GUID - only distinguishes our app on the system, never change it.
    private const string SingleInstanceMutexName = "Global\\Centertized-9F1B1C2E-6C3B-4B7E-9A0E-9D6E9E7B2B10";
    private const int WM_HOTKEY = 0x0312;

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private HwndSource? _hotkeyMessageSource;
    private ILogger _logger = null!;

    /// <summary>
    /// No DI container - the app is small, so the shared instances (registry,
    /// settings store, action catalog) are simply static, available from anywhere
    /// in the UI project (Views/Controls).
    /// </summary>
    public static HotkeyActionRegistry HotkeyRegistry { get; private set; } = null!;

    public static ISettingsStore SettingsStore { get; private set; } = null!;

    public static WindowActionCatalog ActionCatalog { get; private set; } = null!;

    public static NewWindowWatcher NewWindowWatcher { get; private set; } = null!;

    public static AppRulesService AppRules { get; private set; } = null!;

    public static IWin32WindowService WindowService { get; private set; } = null!;

    /// <summary>Copy of AppSettings.RememberWindowSizes - read from the watcher threads, so not straight from disk.</summary>
    public static volatile bool RememberWindowSizes = true;

    public static WindowSizeLearner SizeLearner { get; private set; } = null!;

    public static UpdateService Updates { get; } = new();

    public static void LogUpdateFailure(Exception exception) =>
        Log.Warning(exception, "Checking for / downloading the update failed.");

    private AboutWindow? _aboutWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // After toggling admin mode the app starts a new instance while the old one is still closing -
        // such an instance (--relaunch) waits for the lock to be released, any other would exit at once.
        var relaunching = e.Args.Contains(ElevationService.RelaunchArgument);
        var createdNew = false;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // The lock is owned by an elevated instance and a non-elevated process may not open it -
            // so it is "another instance is already running" all the same.
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
                createdNew = true; // somebody "killed" the previous instance, but the lock is free
            }
        }

        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            // The app is already running - this instance has nothing to do. It never took
            // ownership of the mutex, so ReleaseMutex must not be called in OnExit.
            Shutdown();
            return;
        }

        SettingsStore = new JsonSettingsStore();
        var startupSettings = SettingsStore.Load();

        // Admin mode is on but the app runs without elevated rights (e.g. started by hand) - switch
        // to an elevated instance. If the user declines UAC, it just keeps running without it.
        if (startupSettings.RunAsAdministrator && !ElevationService.IsElevated && ElevationService.RelaunchElevated())
        {
            ReleaseSingleInstanceMutex();
            Shutdown();
            return;
        }

        // Autostart of an elevated app goes through a scheduled task (a Run key would show UAC on every
        // logon) - converted at startup if needed.
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

        // The tray notification sink needs a ready tray icon, so Serilog is configured
        // here and not at the very beginning of OnStartup.
        ConfigureLogging();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        InitializeHotkeys();

        // By default the app shows the Settings window after start (the user can turn on
        // "start minimized" in Settings). --settings additionally forces it to show even with
        // StartMinimized - handy for quick testing.
        var startMinimized = SettingsStore.Load().StartMinimized;
        if (!startMinimized || e.Args.Contains("--settings"))
        {
            ShowSettingsWindow();
        }

        // Only for manual/scripted checking of the menu's look - a right click on the tray
        // can't be captured from a script, so the menu is opened at fixed coordinates.
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
            // The native menu blocks until it is closed - hence only after startup finishes.
            Dispatcher.BeginInvoke(() => ShowTrayMenu((1000, 760)), DispatcherPriority.ApplicationIdle);
        }
    }

    // ExtractAssociatedIcon always returns just 32x32, which looks blurry in the tray at higher DPI -
    // the ICO contains several sizes, so we pick the one the tray really needs.
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

        // dispose: false - Log.Logger is closed explicitly by OnExit (Log.CloseAndFlush).
        _logger = new SerilogLoggerFactory(Log.Logger, dispose: false).CreateLogger("Centertized");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Unhandled exception on the UI thread.");
        // The app runs in the background without a console - one failed action/page shouldn't bring
        // down the whole process, the user wouldn't even know about it.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled exception outside the UI thread - the app is about to crash.");
        }

        Log.CloseAndFlush();
    }

    private void InitializeHotkeys()
    {
        // Hidden message-only window used just to receive WM_HOTKEY - HWND_MESSAGE (-3) as the
        // parent makes it visually absent (no window, no taskbar icon).
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
                continue; // a shortcut for an action that no longer exists in the app (e.g. after an update)
            }

            if (!Hotkey.TryParse(hotkeyText, out var hotkey))
            {
                _logger.LogWarning("The saved shortcut '{HotkeyText}' for action '{ActionId}' could not be parsed.", hotkeyText, actionId);
                continue;
            }

            var result = HotkeyRegistry.TryBind(actionId, hotkey);
            if (result.Outcome != HotkeyRegistrationOutcome.Success)
            {
                _logger.LogWarning(
                    "Failed to re-register the shortcut '{HotkeyText}' for action '{ActionId}': {Outcome}.",
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

    // Native Win32 menu (see NativeTrayMenu) instead of a WPF ContextMenu.
    private void TrayIcon_RightClick(object sender, RoutedEventArgs e) => ShowTrayMenu();

    // Silent check after startup (installed app only). When a new version exists a balloon announces it;
    // download and restart are always started by the user in Settings, nothing installs without their knowledge.
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
        // Glyphs from Segoe Fluent Icons: OpenInNewWindow, Info, PowerButton.
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

    // Common tray app convention - a left double-click opens the main/Settings window.
    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e) => ShowSettingsWindow();

    private void ShowSettingsWindow()
    {
        // Closing only hides the window (see SettingsWindow.OnClosing) - live theme
        // switching works now (no NavigationView, see SettingsWindow.xaml), so the same instance
        // can safely be kept for the whole lifetime of the app and
        // opening it again is instant.
        _settingsWindow ??= new SettingsWindow();

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Called by SettingsWindow when the window is hidden for the first time (closed with the X) -
    /// the app then "disappears" and it's good to tell the user once that it keeps running in the tray.
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

    /// <summary>Quits the app and starts a new one with/without elevated rights (see the "Run as administrator" setting).</summary>
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

    // The new instance must get the lock right away, not only after OnExit completes.
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
        // Without this, the Shutdown() below would run into SettingsWindow.OnClosing, which would
        // cancel the close (Cancel = true) and the app wouldn't exit properly.
        if (_settingsWindow is not null)
        {
            _settingsWindow.AllowClose = true;
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Without an explicit Dispose a "ghost" icon could stay in the tray after the app exits
        // until the mouse first hovers over its spot.
        _trayIcon?.Dispose();
        _hotkeyMessageSource?.Dispose(); // also releases all RegisterHotKey registrations on this window
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
