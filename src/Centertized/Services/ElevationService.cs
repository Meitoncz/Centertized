using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Centertized.Services;

/// <summary>
/// Režim "Spustit jako administrátor": Windows (UIPI) nedovolí neprivilegovanému procesu přesouvat
/// okna procesů se zvýšenými právy, takže jediná cesta, jak centrovat třeba okna aplikací
/// spuštěných jako správce, je sám běžet se zvýšenými právy. Zvýšení vždy vyžaduje UAC souhlas.
/// </summary>
public static class ElevationService
{
    public const string RelaunchArgument = "--relaunch";

    private const int ErrorCancelled = 1223; // uživatel v UAC dialogu zvolil "Ne"

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Spustí novou instanci appky se zvýšenými právy (UAC dialog). Vrací false, když uživatel UAC
    /// odmítl - appka pak zůstane běžet, jak běžela. Nová instance si počká, než tahle skončí
    /// (<see cref="RelaunchArgument"/>), protože obě sdílejí jednu pojmenovanou "single instance" zámek.
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
    /// Spustí novou instanci BEZ zvýšených práv. Z elevated procesu obyčejný Process.Start zdědí
    /// zvýšení, proto se spuštění předá Průzkumníkovi, který běží s běžnými právy.
    /// </summary>
    public static void RelaunchNotElevated()
    {
        // Explorer neumí předat argumenty, takže nová instance nemůže čekat na uvolnění zámku sama -
        // spuštění se proto o chvíli odloží, aby tahle (zvýšená) instance stihla skončit a uvolnit
        // zámek i zkratky.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & explorer.exe \"{Environment.ProcessPath}\"")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
        });
    }
}
