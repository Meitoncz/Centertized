using Centertized.Core.Actions;
using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Hotkeys;

/// <summary>
/// Maps shortcut -> action, handles intra-app and external conflicts and takes care of
/// RegisterHotKey/UnregisterHotKey via <see cref="IHotkeyRegistrar"/>. It owns no
/// window – it receives the handle of the hidden message-only window (see Phase 2 in CLAUDE.md, created
/// in the UI project via HwndSource) ready-made in the constructor.
///
/// Use from a single thread only (in the app: the Dispatcher/UI thread, which also processes
/// WM_HOTKEY) – no locks, because no other thread should touch this.
/// </summary>
public sealed class HotkeyActionRegistry
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly IntPtr _windowHandle;
    private readonly WindowActionCatalog _catalog;
    private readonly IWin32WindowService _windowService;
    private readonly ILogger _logger;
    private readonly Dictionary<string, HotkeyBinding> _bindingsByActionId = new();
    private readonly Dictionary<int, string> _actionIdById = new();
    private int _nextId = 1;

    public HotkeyActionRegistry(IHotkeyRegistrar registrar, WindowActionCatalog catalog, IWin32WindowService windowService, ILogger logger, IntPtr windowHandle)
    {
        _registrar = registrar;
        _catalog = catalog;
        _windowService = windowService;
        _logger = logger;
        _windowHandle = windowHandle;
    }

    public IReadOnlyDictionary<string, Hotkey> CurrentBindings =>
        _bindingsByActionId.ToDictionary(kv => kv.Key, kv => kv.Value.Hotkey);

    public HotkeyRegistrationResult TryBind(string actionId, Hotkey hotkey)
    {
        if (_catalog.TryGetById(actionId) is null)
        {
            throw new ArgumentException($"Unknown action Id: '{actionId}'.", nameof(actionId));
        }

        var conflict = _bindingsByActionId.FirstOrDefault(kv => kv.Key != actionId && kv.Value.Hotkey == hotkey);
        if (conflict.Key is not null)
        {
            return HotkeyRegistrationResult.ConflictsWithApp(conflict.Key);
        }

        // Rebinding the same action to a different shortcut – release the old one first.
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

    /// <summary>Call from the WM_HOTKEY hook in the UI project with wParam converted to int.</summary>
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
            await action.ExecuteAsync(new WindowActionContext(_windowService, _logger, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Warning, not Error/Critical – the app keeps running, just this one action
            // failed (and via TrayNotificationSink it also shows up as a tray toast).
            _logger.LogWarning(ex, "Action '{ActionId}' failed.", action.Id);
        }
    }

    private readonly record struct HotkeyBinding(int Id, Hotkey Hotkey);
}
