using System.Diagnostics;
using Microsoft.Win32;

namespace Centertized.Services;

/// <summary>
/// Starting the app after logon. Normal mode: the HKCU Run key (needs no admin rights, the user can
/// see it in Task Manager too). Admin mode: a scheduled task with the highest privileges - a Run key can
/// only start an unprivileged process and UAC would then show at every logon. The task is created
/// and removed only from an elevated instance (schtasks doesn't allow it otherwise).
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Centertized";
    private const string TaskName = "Centertized";

    /// <summary>
    /// Always read straight from the system (not from a cache) - so the state doesn't drift when
    /// the key/task is deleted from outside by the user or e.g. an antivirus.
    /// </summary>
    public bool IsEnabled() => IsRunKeyPresent() || IsTaskPresent();

    public void SetEnabled(bool enabled, bool useElevatedTask = false)
    {
        if (!enabled)
        {
            RemoveRunKey();
            RemoveTask();
            return;
        }

        if (useElevatedTask && ElevationService.IsElevated && CreateTask())
        {
            RemoveRunKey();
            return;
        }

        SetRunKey();
        RemoveTask();
    }

    /// <summary>Converts an existing autostart to the requested kind (Run key <-> task) when it is enabled.</summary>
    public void Migrate(bool useElevatedTask)
    {
        if (IsEnabled())
        {
            SetEnabled(true, useElevatedTask);
        }
    }

    private static bool IsRunKeyPresent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existingCommand &&
               string.Equals(existingCommand, BuildCommand(), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, BuildCommand());
    }

    private static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool IsTaskPresent() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    private static bool CreateTask() =>
        RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"{BuildCommand().Replace("\"", "\\\"")}\" /SC ONLOGON /RL HIGHEST /F") == 0;

    private static void RemoveTask()
    {
        if (IsTaskPresent())
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F"); // fails from a non-elevated instance - harmless, an unused task remains
        }
    }

    private static int RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        process!.WaitForExit();
        return process.ExitCode;
    }

    private static string BuildCommand()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
        return $"\"{exePath}\"";
    }
}
