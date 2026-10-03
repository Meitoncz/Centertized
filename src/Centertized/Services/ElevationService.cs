using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Centertized.Services;

/// <summary>
/// "Run as administrator" mode: Windows (UIPI) doesn't let an unprivileged process move
/// windows of elevated processes, so the only way to center e.g. windows of apps
/// run as administrator is to run elevated ourselves. Elevation always needs UAC consent.
/// </summary>
public static class ElevationService
{
    public const string RelaunchArgument = "--relaunch";

    private const int ErrorCancelled = 1223; // the user chose "No" in the UAC dialog

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Starts a new instance of the app with elevated rights (UAC dialog). Returns false when the user
    /// declined UAC - the app then keeps running as it was. The new instance waits until this one ends
    /// (<see cref="RelaunchArgument"/>), because both share one named "single instance" lock.
    /// </summary>
    public static bool RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, RelaunchArgument)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts a new instance WITHOUT elevated rights. A plain Process.Start from an elevated process inherits
    /// the elevation, so the launch is handed to Explorer, which runs with normal rights.
    /// </summary>
    public static void RelaunchNotElevated()
    {
        // Explorer can't pass arguments, so the new instance can't wait for the lock to be released itself -
        // the launch is therefore delayed a bit so this (elevated) instance can finish and release
        // the lock and the shortcuts.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & explorer.exe \"{Environment.ProcessPath}\"")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
        });
    }
}
