using System.Diagnostics;
using Microsoft.Win32;

namespace Centertized.Services;

/// <summary>
/// Spouštění appky po přihlášení. Běžný režim: HKCU Run klíč (nevyžaduje admin práva, uživatel to
/// vidí i v Task Manageru). Admin režim: úloha plánovače s nejvyššími oprávněními - Run klíč umí
/// spustit jen neprivilegovaný proces a UAC by se pak ukázal při každém přihlášení. Úloha se zakládá
/// a ruší jen ze zvýšené instance (schtasks to jinak nedovolí).
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Centertized";
    private const string TaskName = "Centertized";

    /// <summary>
    /// Čte se vždy přímo ze systému (ne z cache) - stav se tak nerozjede, když
    /// klíč/úlohu zvenku smaže uživatel nebo třeba antivirus.
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

    /// <summary>Převede existující autostart na požadovaný způsob (Run klíč <-> úloha), když je zapnutý.</summary>
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
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F"); // z neprivilegované instance selže - nevadí, zbude nepoužívaná úloha
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
