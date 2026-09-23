using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Centertized.Controls;
using Centertized.Core.Hotkeys;
using Centertized.Core.Settings;
using Centertized.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Centertized.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly AutostartService _autostartService = new();
    private bool _isInitializing;

    public SettingsWindow()
    {
        InitializeComponent();

        var settings = App.SettingsStore.Load();

        // ApplicationThemeManager.Apply() samo o sobě přepne jen barevné resource
        // dictionaries - skutečné DWM vykreslování Mica řídí až vlastnost
        // WindowBackdropType na samotném okně (FluentWindow na ni reaguje přes
        // OnBackdropTypeChanged). Bez tohohle by Mica vůbec neběžela.
        WindowBackdropType = WindowBackdropType.Mica;
        ApplyTheme(settings.ThemeMode);

        // Živé přebarvení bylo dřív rozbité (lepoco/wpfui#1639), ale ten bug je
        // konkrétně o NavigationView - tohle okno žádné nemá (jedna scrollovatelná
        // stránka místo sidebaru, viz SettingsWindow.xaml), takže SystemThemeWatcher
        // pro "System" motiv může sledovat změny naživo bez zavírání okna.
        if (settings.ThemeMode == ThemePreference.System)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }

        ActionsList.ItemsSource = App.ActionCatalog;

        _isInitializing = true;
        StartWithWindowsToggle.IsChecked = _autostartService.IsEnabled();
        StartMinimizedToggle.IsChecked = settings.StartMinimized;
        ThemeModeCombo.SelectedIndex = (int)settings.ThemeMode;
        _isInitializing = false;

        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version}";
    }

    private static void ApplyTheme(ThemePreference mode)
    {
        var theme = mode switch
        {
            ThemePreference.Light => ApplicationTheme.Light,
            ThemePreference.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
    }

    private void ThemeModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var mode = (ThemePreference)ThemeModeCombo.SelectedIndex;
        var settings = App.SettingsStore.Load();
        settings.ThemeMode = mode;
        App.SettingsStore.Save(settings);

        // Odhlásit případné staré sledování a znovu podle nové volby - jinak by
        // appka mohla zůstat naslouchat systémovému motivu i po přepnutí na
        // natvrdo dané Light/Dark.
        SystemThemeWatcher.UnWatch(this);
        ApplyTheme(mode);
        if (mode == ThemePreference.System)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _autostartService.SetEnabled(StartWithWindowsToggle.IsChecked == true);
    }

    private void StartMinimizedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var settings = App.SettingsStore.Load();
        settings.StartMinimized = StartMinimizedToggle.IsChecked == true;
        App.SettingsStore.Save(settings);
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
            // Krátké hlášky - tlačítko má pevnou šířku (viz HotkeyCaptureControl.xaml).
            HotkeyRegistrationOutcome.AlreadyBoundInApp => $"Used by {DisplayNameOf(result.ConflictingActionId)}",
            HotkeyRegistrationOutcome.AlreadyRegisteredExternally => "In use by another app",
            _ => control.DisplayText,
        };

        if (result.Outcome == HotkeyRegistrationOutcome.Success)
        {
            var settings = App.SettingsStore.Load();
            settings.Hotkeys[actionId] = hotkey.ToString();
            App.SettingsStore.Save(settings);
        }
    }

    private static string DisplayNameOf(string? actionId) =>
        (actionId is not null ? App.ActionCatalog.TryGetById(actionId) : null)?.DisplayName ?? actionId ?? "?";

    // App.xaml.cs si tohle nastaví na true těsně před Application.Shutdown() –
    // jinak by Shutdown() při zavírání oken narazil na Cancel = true níž a appka
    // by se nemusela korektně ukončit.
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose)
        {
            SystemThemeWatcher.UnWatch(this);
            return;
        }

        // Okno se jen schová, ne zavře – příští otevření z tray je pak okamžité.
        e.Cancel = true;
        Hide();
        App.ShowTrayHintIfNeeded();
    }
}
