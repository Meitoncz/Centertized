using System.IO;
using System.Windows;
using System.Windows.Threading;
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

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

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
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
