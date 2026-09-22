using System.Diagnostics;
using Microsoft.Win32;

namespace Centertized.Services;

/// <summary>
/// Spouštění appky po přihlášení přes HKCU Run klíč - nevyžaduje admin práva
/// (na rozdíl od Task Scheduleru), uživatel si to může zkontrolovat i v
/// Task Manageru (záložka Po spuštění).
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Centertized";

    /// <summary>
    /// Čte se vždy přímo z registru (ne z cache) - stav se tak nerozjede, když
    /// klíč zvenku smaže uživatel nebo třeba antivirus.
    /// </summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existingCommand &&
               string.Equals(existingCommand, BuildCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand());
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildCommand()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
        return $"\"{exePath}\"";
    }
}
