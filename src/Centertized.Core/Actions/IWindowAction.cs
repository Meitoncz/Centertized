namespace Centertized.Core.Actions;

/// <summary>
/// Contract for anything that can be run by a global shortcut. A new feature = a new class
/// implementing this + one line in <see cref="WindowActionCatalog"/>, with no
/// change in the hotkey registry, the tray or the Shortcuts page (see CLAUDE.md).
/// </summary>
public interface IWindowAction
{
    /// <summary>Stable key, stored in settings.json – never change it after release.</summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>
    /// Async on purpose – it runs on the same thread that processes WM_HOTKEY, so a slow
    /// synchronous action would freeze the whole app, including the other shortcuts, for its duration.
    /// </summary>
    Task ExecuteAsync(WindowActionContext context);
}
