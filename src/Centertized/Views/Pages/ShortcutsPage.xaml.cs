using System.Windows;
using System.Windows.Controls;
using Centertized.Controls;
using Centertized.Core.Hotkeys;

namespace Centertized.Views.Pages;

public partial class ShortcutsPage : Page
{
    public ShortcutsPage()
    {
        InitializeComponent();
        ActionsList.ItemsSource = App.ActionCatalog;
    }

    private void OnCaptureControlLoaded(object sender, RoutedEventArgs e)
    {
        var control = (HotkeyCaptureControl)sender;
        var actionId = (string)control.Tag;
        if (App.HotkeyRegistry.CurrentBindings.TryGetValue(actionId, out var hotkey))
        {
            control.DisplayText = hotkey.ToString();
        }
    }

    private void OnHotkeyCaptured(object? sender, Hotkey hotkey)
    {
        var control = (HotkeyCaptureControl)sender!;
        var actionId = (string)control.Tag;

        var result = App.HotkeyRegistry.TryBind(actionId, hotkey);
        control.DisplayText = result.Outcome switch
        {
            HotkeyRegistrationOutcome.Success => hotkey.ToString(),
            HotkeyRegistrationOutcome.AlreadyBoundInApp => $"Already used by \"{DisplayNameOf(result.ConflictingActionId)}\"",
            HotkeyRegistrationOutcome.AlreadyRegisteredExternally => "Already in use by another application",
            _ => control.DisplayText,
        };

        if (result.Outcome == HotkeyRegistrationOutcome.Success)
        {
            PersistHotkey(actionId, hotkey);
        }
    }

    private static string DisplayNameOf(string? actionId) =>
        (actionId is not null ? App.ActionCatalog.TryGetById(actionId) : null)?.DisplayName ?? actionId ?? "?";

    private static void PersistHotkey(string actionId, Hotkey hotkey)
    {
        // Write-through hned po úspěšné registraci, žádné tlačítko Save/Cancel.
        var settings = App.SettingsStore.Load();
        settings.Hotkeys[actionId] = hotkey.ToString();
        App.SettingsStore.Save(settings);
    }
}
