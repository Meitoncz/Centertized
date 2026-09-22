using H.NotifyIcon;
using H.NotifyIcon.Core;
using Serilog.Core;
using Serilog.Events;

namespace Centertized.Services;

/// <summary>
/// Serilog sink, který cokoliv na úrovni Warning a výš navíc ukáže jako tray
/// notifikaci – typicky "SetWindowPos selhal (zvýšené okno)". Díky tomu
/// CenterActiveWindowAction (v Core, bez znalosti tray ikony) stačí normálně
/// logovat a UI vrstva se postará o zbytek.
/// </summary>
public sealed class TrayNotificationSink : ILogEventSink
{
    private readonly TaskbarIcon _trayIcon;

    public TrayNotificationSink(TaskbarIcon trayIcon) => _trayIcon = trayIcon;

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning)
        {
            return;
        }

        var icon = logEvent.Level >= LogEventLevel.Error ? NotificationIcon.Error : NotificationIcon.Warning;
        _trayIcon.Dispatcher.BeginInvoke(() =>
            _trayIcon.ShowNotification("Centertized", logEvent.RenderMessage(), icon));
    }
}
