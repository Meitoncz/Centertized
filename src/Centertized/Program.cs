using Velopack;

namespace Centertized;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Musí být úplně první - při instalaci/aktualizaci/odinstalaci Velopack spustí appku
        // s speciálními argumenty, provede hook a proces sám ukončí, k WPF se to nedostane.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
