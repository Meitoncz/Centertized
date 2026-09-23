using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Centertized.Controls;
using Centertized.Core.Actions;
using Centertized.Core.Hotkeys;
using Centertized.Core.Settings;
using Centertized.Core.WindowManagement;
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
        ThemeService.Apply(settings.ThemeMode);

        // Živé přebarvení bylo dřív rozbité (lepoco/wpfui#1639), ale ten bug je
        // konkrétně o NavigationView - tohle okno žádné nemá (jedna scrollovatelná
        // stránka místo sidebaru, viz SettingsWindow.xaml), takže SystemThemeWatcher
        // pro "System" motiv může sledovat změny naživo bez zavírání okna.
        if (settings.ThemeMode == ThemePreference.System)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }

        _isInitializing = true;
        StartWithWindowsToggle.IsChecked = _autostartService.IsEnabled();
        StartMinimizedToggle.IsChecked = settings.StartMinimized;
        ThemeModeCombo.SelectedIndex = (int)settings.ThemeMode;
        LanguageCombo.SelectedIndex = (int)settings.Language;
        AutoCenterNewWindowsToggle.IsChecked = settings.AutoCenterNewWindows;
        RememberSizesToggle.IsChecked = settings.RememberWindowSizes;
        _isInitializing = false;

        RefreshLocalizedContent();

        App.AppRules.Changed += OnAppRulesChanged;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    // Seznam zkratek i aplikací se staví z dat (ne ze statického XAML), takže po změně
    // jazyka nebo pravidel se prostě postaví znovu.
    private void RefreshLocalizedContent()
    {
        ActionsList.ItemsSource = App.ActionCatalog
            .Select(a => new ActionRow(
                a.Id,
                LocalizedOr($"Action.{a.Id}.Name", a.DisplayName),
                LocalizedOr($"Action.{a.Id}.Description", a.Description)))
            .ToList();

        VersionText.Text = Loc.Format("About.Version", AppInfo.Version);
        RefreshApps();
    }

    private static string LocalizedOr(string key, string fallback)
    {
        var text = Loc.Get(key);
        return text == key ? fallback : text;
    }

    private void RefreshApps()
    {
        var rows = App.AppRules.ExcludedApps().Select(kv => new ExceptionRow(kv.Key, kv.Value.DisplayName, kv.Value.AccentColor)).ToList();
        ExceptionsList.ItemsSource = rows;
        ExceptionsEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Automaticky učené velikosti se mění při každém tažení okna - seznam výjimek se kvůli tomu nepřestavuje.
    private void OnAppRulesChanged(AppRuleChange change)
    {
        if (change.Kind is AppRuleChangeKind.ExclusionChanged or AppRuleChangeKind.Removed)
        {
            Dispatcher.BeginInvoke(RefreshApps);
        }
    }

    private void OnLanguageChanged() => Dispatcher.BeginInvoke(RefreshLocalizedContent);

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
        ThemeService.Apply(mode);
        if (mode == ThemePreference.System)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || LanguageCombo.SelectedIndex < 0)
        {
            return;
        }

        var language = (AppLanguage)LanguageCombo.SelectedIndex;
        var settings = App.SettingsStore.Load();
        settings.Language = language;
        App.SettingsStore.Save(settings);

        Loc.Apply(language);
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

    private void AutoCenterNewWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var enabled = AutoCenterNewWindowsToggle.IsChecked == true;
        var settings = App.SettingsStore.Load();
        settings.AutoCenterNewWindows = enabled;
        App.SettingsStore.Save(settings);

        if (enabled)
        {
            App.NewWindowWatcher.Start();
        }
        else
        {
            App.NewWindowWatcher.Stop();
        }
    }

    private void RememberSizesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var enabled = RememberSizesToggle.IsChecked == true;
        var settings = App.SettingsStore.Load();
        settings.RememberWindowSizes = enabled;
        App.SettingsStore.Save(settings);
        App.RememberWindowSizes = enabled;

        if (enabled)
        {
            App.SizeLearner.Start();
        }
        else
        {
            App.SizeLearner.Stop();
        }
    }

    private void ForgetAllSizesButton_Click(object sender, RoutedEventArgs e) => App.AppRules.ClearAllRememberedSizes();

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
            HotkeyRegistrationOutcome.AlreadyBoundInApp => Loc.Format("Hotkey.UsedBy", DisplayNameOf(result.ConflictingActionId)),
            HotkeyRegistrationOutcome.AlreadyRegisteredExternally => Loc.Get("Hotkey.UsedElsewhere"),
            _ => control.DisplayText,
        };

        if (result.Outcome == HotkeyRegistrationOutcome.Success)
        {
            var settings = App.SettingsStore.Load();
            settings.Hotkeys[actionId] = hotkey.ToString();
            App.SettingsStore.Save(settings);
        }
    }

    private static string DisplayNameOf(string? actionId)
    {
        var action = actionId is not null ? App.ActionCatalog.TryGetById(actionId) : null;
        return actionId is null ? "?" : LocalizedOr($"Action.{actionId}.Name", action?.DisplayName ?? actionId);
    }

    private void ChooseAppsButton_Click(object sender, RoutedEventArgs e) =>
        new InstalledAppsWindow { Owner = this }.ShowDialog();

    private void ExceptionRemove_Click(object sender, RoutedEventArgs e) =>
        App.AppRules.SetExcluded((string)((FrameworkElement)sender).Tag, false);

    // App.xaml.cs si tohle nastaví na true těsně před Application.Shutdown() –
    // jinak by Shutdown() při zavírání oken narazil na Cancel = true níž a appka
    // by se nemusela korektně ukončit.
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose)
        {
            SystemThemeWatcher.UnWatch(this);
            App.AppRules.Changed -= OnAppRulesChanged;
            Loc.LanguageChanged -= OnLanguageChanged;
            return;
        }

        // Okno se jen schová, ne zavře – příští otevření z tray je pak okamžité.
        e.Cancel = true;
        Hide();
        App.ShowTrayHintIfNeeded();
    }

    private sealed record ActionRow(string Id, string Name, string Description);

    private sealed record ExceptionRow(string Key, string DisplayName, string? AccentColor)
    {
        // Bez uložené barvy (aplikace bez ikony) neutrální šedý puntík.
        public System.Windows.Media.Brush AccentBrush { get; } = new System.Windows.Media.SolidColorBrush(
            AccentColorExtractor.FromHex(AccentColor) ?? System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A));
    }
}
