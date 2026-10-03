using Velopack;

namespace Centertized;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be the very first thing - on install/update/uninstall Velopack starts the app
        // with special arguments, runs the hook and ends the process itself, WPF is never reached.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
