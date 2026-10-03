using H.NotifyIcon;
using H.NotifyIcon.Core;
using Serilog.Core;
using Serilog.Events;

namespace Centertized.Services;

/// <summary>
/// A Serilog sink that additionally shows anything at Warning level and above as a tray
/// notification – typically "SetWindowPos failed (elevated window)". Thanks to this
/// CenterActiveWindowAction (in Core, with no knowledge of the tray icon) can just log
/// normally and the UI layer takes care of the rest.
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
