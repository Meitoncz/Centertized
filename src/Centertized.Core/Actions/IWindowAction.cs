namespace Centertized.Core.Actions;

/// <summary>
/// Kontrakt pro cokoliv spustitelné globální zkratkou. Nová featura = nová třída
/// implementující tohle + jeden řádek v <see cref="WindowActionCatalog"/>, beze
/// změny v hotkey registry, tray nebo Shortcuts stránce (viz CLAUDE.md).
/// </summary>
public interface IWindowAction
{
    /// <summary>Stabilní klíč, ukládá se do settings.json – nikdy neměnit po vydání.</summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>
    /// Async záměrně – běží na stejném vlákně, které zpracovává WM_HOTKEY, takže pomalá
    /// synchronní akce by na tu dobu zamrazila celou appku včetně ostatních zkratek.
    /// </summary>
    Task ExecuteAsync(WindowActionContext context);
}
