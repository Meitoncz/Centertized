using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

/// <summary>
/// Fáze 5 – druhá skutečná akce (nápad z IDEAS.md), která ověřuje, že přidání
/// featury opravdu stačí jako nová třída + jeden řádek v katalogu (viz CLAUDE.md).
/// Maximalizuje aktivní okno; při opětovném spuštění na okně, které takhle
/// maximalizovala samotná appka, ho vrátí na předchozí pozici a velikost.
/// </summary>
public sealed class ToggleMaximizeAction : IWindowAction
{
    public const string ActionId = "toggle-maximize-active-window";

    // Klíčováno podle HWND – appka běží jen jako jedna instance a stav nepřežívá
    // restart, takže jednoduchý in-memory slovník stačí (žádná perzistence).
    private readonly Dictionary<IntPtr, WindowRect> _boundsBeforeMaximize = new();

    public string Id => ActionId;

    public string DisplayName => "Toggle maximize active window";

    public string Description => "Maximizes the active window; pressing it again restores the previous size and position.";

    public Task ExecuteAsync(WindowActionContext context)
    {
        var windowService = context.WindowService;
        var hwnd = windowService.GetForegroundWindowHandle();

        if (!windowService.IsEligibleForActions(hwnd))
        {
            context.Logger.LogDebug("Přeskočeno – okno {Handle} není způsobilé.", hwnd);
            return Task.CompletedTask;
        }

        if (windowService.IsMaximized(hwnd))
        {
            windowService.Restore(hwnd);

            if (_boundsBeforeMaximize.Remove(hwnd, out var previousBounds))
            {
                windowService.TrySetBounds(hwnd, previousBounds);
            }
            // Pokud v mapě není (appka maximalizaci "neviděla", např. proběhla jinak
            // než touhle akcí), necháme okno v jeho výchozí obnovené velikosti/pozici.

            context.Logger.LogInformation("Okno {Handle} obnoveno na předchozí pozici/velikost.", hwnd);
        }
        else
        {
            if (windowService.TryGetWindowRect(hwnd, out var currentBounds))
            {
                _boundsBeforeMaximize[hwnd] = currentBounds;
            }

            windowService.Maximize(hwnd);
            context.Logger.LogInformation("Okno {Handle} maximalizováno.", hwnd);
        }

        return Task.CompletedTask;
    }
}
