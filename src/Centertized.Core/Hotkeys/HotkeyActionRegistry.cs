using System.Diagnostics;
using Centertized.Core.Actions;
using Centertized.Core.WindowManagement;

namespace Centertized.Core.Hotkeys;

/// <summary>
/// Mapuje zkratka -> akce, řeší intra-app i externí kolize a stará se o
/// RegisterHotKey/UnregisterHotKey přes <see cref="IHotkeyRegistrar"/>. Nevlastní
/// žádné okno – handle skrytého message-only okna (viz Fáze 2 v CLAUDE.md, vzniká
/// v UI projektu přes HwndSource) dostává hotově v konstruktoru.
///
/// Používat jen z jednoho vlákna (v appce: Dispatcher/UI vlákno, které zpracovává
/// i WM_HOTKEY) – bez zámků, protože žádné jiné vlákno sem nemá sahat.
/// </summary>
public sealed class HotkeyActionRegistry
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly IntPtr _windowHandle;
    private readonly WindowActionCatalog _catalog;
    private readonly IWin32WindowService _windowService;
    private readonly Dictionary<string, HotkeyBinding> _bindingsByActionId = new();
    private readonly Dictionary<int, string> _actionIdById = new();
    private int _nextId = 1;

    public HotkeyActionRegistry(IHotkeyRegistrar registrar, WindowActionCatalog catalog, IWin32WindowService windowService, IntPtr windowHandle)
    {
        _registrar = registrar;
        _catalog = catalog;
        _windowService = windowService;
        _windowHandle = windowHandle;
    }

    public IReadOnlyDictionary<string, Hotkey> CurrentBindings =>
        _bindingsByActionId.ToDictionary(kv => kv.Key, kv => kv.Value.Hotkey);

    public HotkeyRegistrationResult TryBind(string actionId, Hotkey hotkey)
    {
        if (_catalog.TryGetById(actionId) is null)
        {
            throw new ArgumentException($"Neznámé Id akce: '{actionId}'.", nameof(actionId));
        }

        var conflict = _bindingsByActionId.FirstOrDefault(kv => kv.Key != actionId && kv.Value.Hotkey == hotkey);
        if (conflict.Key is not null)
        {
            return HotkeyRegistrationResult.ConflictsWithApp(conflict.Key);
        }

        // Přebindování téže akce na jinou zkratku – nejdřív uvolnit tu starou.
        if (_bindingsByActionId.TryGetValue(actionId, out var previous))
        {
            _registrar.Unregister(_windowHandle, previous.Id);
            _actionIdById.Remove(previous.Id);
            _bindingsByActionId.Remove(actionId);
        }

        var id = _nextId++;
        var outcome = _registrar.TryRegister(_windowHandle, id, hotkey);
        if (outcome != HotkeyRegistrarOutcome.Success)
        {
            return HotkeyRegistrationResult.ConflictsExternally();
        }

        _bindingsByActionId[actionId] = new HotkeyBinding(id, hotkey);
        _actionIdById[id] = actionId;
        return HotkeyRegistrationResult.Success();
    }

    public void Unbind(string actionId)
    {
        if (!_bindingsByActionId.TryGetValue(actionId, out var binding))
        {
            return;
        }

        _registrar.Unregister(_windowHandle, binding.Id);
        _actionIdById.Remove(binding.Id);
        _bindingsByActionId.Remove(actionId);
    }

    /// <summary>Volat z WM_HOTKEY hooku v UI projektu s wParam převedeným na int.</summary>
    public void Dispatch(int registeredId)
    {
        if (!_actionIdById.TryGetValue(registeredId, out var actionId))
        {
            return;
        }

        var action = _catalog.TryGetById(actionId);
        if (action is null)
        {
            return;
        }

        _ = ExecuteSafelyAsync(action);
    }

    private async Task ExecuteSafelyAsync(IWindowAction action)
    {
        try
        {
            await action.ExecuteAsync(new WindowActionContext(_windowService, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fáze 4: přes Serilog + tray notifikaci. Zatím ať to aspoň není úplně tiché.
            Debug.WriteLine($"Akce '{action.Id}' spadla: {ex}");
        }
    }

    private readonly record struct HotkeyBinding(int Id, Hotkey Hotkey);
}
