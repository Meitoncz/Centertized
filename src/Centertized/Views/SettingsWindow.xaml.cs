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

        // ApplicationThemeManager.Apply() on its own only switches the color resource
        // dictionaries - the actual DWM Mica rendering is controlled by the
        // WindowBackdropType property on the window itself (FluentWindow reacts to it via
        // OnBackdropTypeChanged). Without this Mica wouldn't run at all.
        WindowBackdropType = WindowBackdropType.Mica;
        ThemeService.Apply(settings.ThemeMode);

        // Live re-theming used to be broken (lepoco/wpfui#1639), but that bug is
        // specific to NavigationView - this window has none (one scrollable
        // page instead of a sidebar, see SettingsWindow.xaml), so SystemThemeWatcher
        // can follow changes live for the "System" theme without closing the window.
        if (settings.ThemeMode == ThemePreference.System)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }

        _isInitializing = true;
        StartWithWindowsToggle.IsChecked = _autostartService.IsEnabled();
        StartMinimizedToggle.IsChecked = settings.StartMinimized;
        ThemeModeCombo.SelectedIndex = (int)settings.ThemeMode;
        PopulateLanguageCombo(settings.Language);
        AutoCenterNewWindowsToggle.IsChecked = settings.AutoCenterNewWindows;
        RememberSizesToggle.IsChecked = settings.RememberWindowSizes;
        // The toggle shows the real state: on = the app is actually running elevated right now.
        RunAsAdminToggle.IsChecked = settings.RunAsAdministrator && ElevationService.IsElevated;
        AutoUpdateToggle.IsChecked = settings.CheckForUpdatesAutomatically;
        _isInitializing = false;

        RefreshLocalizedContent();
        RefreshUpdateUi();

        App.AppRules.Changed += OnAppRulesChanged;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    // The shortcut and app lists are built from data (not static XAML), so after a language
    // or rule change they are simply built again.
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

    // Automatically learned sizes change on every window drag - the exceptions list is not rebuilt because of that.
    private void OnAppRulesChanged(AppRuleChange change)
    {
        if (change.Kind is AppRuleChangeKind.ExclusionChanged or AppRuleChangeKind.Removed)
        {
            Dispatcher.BeginInvoke(RefreshApps);
        }
    }

    private void OnLanguageChanged() => Dispatcher.BeginInvoke(() =>
    {
        RefreshLocalizedContent();
        RefreshUpdateUi();
    });

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

        // Unhook any old watching and set it up again according to the new choice - otherwise
        // the app could keep listening to the system theme even after switching to a
        // hard-coded Light/Dark.
        SystemThemeWatcher.UnWatch(this);
        ThemeService.Apply(mode);
        if (mode == ThemePreference.System)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }
    }

    // Item 0 is "System" (localized in XAML), the rest are the shipped languages by their own names.
    private void PopulateLanguageCombo(AppLanguage selected)
    {
        foreach (var language in Loc.Languages)
        {
            LanguageCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = language.NativeName });
        }

        var index = Loc.Languages.ToList().FindIndex(l => l.Language == selected);
        LanguageCombo.SelectedIndex = selected == AppLanguage.System || index < 0 ? 0 : index + 1;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || LanguageCombo.SelectedIndex < 0)
        {
            return;
        }

        var language = LanguageCombo.SelectedIndex == 0 ? AppLanguage.System : Loc.Languages[LanguageCombo.SelectedIndex - 1].Language;
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

        _autostartService.SetEnabled(StartWithWindowsToggle.IsChecked == true, useElevatedTask: ElevationService.IsElevated);
    }

    private void RunAsAdminToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var enable = RunAsAdminToggle.IsChecked == true;
        if (enable == ElevationService.IsElevated)
        {
            return; // the state already matches (e.g. after a restart)
        }

        var settings = App.SettingsStore.Load();
        settings.RunAsAdministrator = enable;
        App.SettingsStore.Save(settings);

        if (!enable)
        {
            // The scheduled task can only be removed from an elevated instance - here, while it still runs.
            _autostartService.Migrate(useElevatedTask: false);
        }

        if (App.RestartAs(elevated: enable))
        {
            return;
        }

        // The user declined UAC - revert the toggle and the setting.
        if (enable)
        {
            settings.RunAsAdministrator = false;
            App.SettingsStore.Save(settings);
            _isInitializing = true;
            RunAsAdminToggle.IsChecked = false;
            _isInitializing = false;
        }
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

    private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var settings = App.SettingsStore.Load();
        settings.CheckForUpdatesAutomatically = AutoUpdateToggle.IsChecked == true;
        App.SettingsStore.Save(settings);
    }

    // Update state: after a check the "Check" button becomes "Download and restart".
    private void RefreshUpdateUi()
    {
        if (!App.Updates.IsInstalled)
        {
            UpdateStatusText.Text = Loc.Get("Update.NotInstalled");
            UpdateButton.IsEnabled = false;
            return;
        }

        UpdateButton.IsEnabled = true;
        if (App.Updates.PendingVersion is { } version)
        {
            UpdateStatusText.Text = Loc.Format("Update.Available", version);
            UpdateButton.Content = Loc.Get("Update.Download");
        }
        else
        {
            UpdateStatusText.Text = AppInfo.Version is var current ? Loc.Format("About.Version", current) : "";
            UpdateButton.Content = Loc.Get("Update.Check");
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            if (App.Updates.PendingVersion is not null)
            {
                UpdateStatusText.Text = Loc.Get("Update.Downloading");
                await App.Updates.DownloadAndRestartAsync(percent => Dispatcher.BeginInvoke(() =>
                    UpdateStatusText.Text = $"{Loc.Get("Update.Downloading")} {percent} %"));
                return; // after success the app restarts itself
            }

            UpdateStatusText.Text = Loc.Get("Update.Checking");
            var available = await App.Updates.CheckAsync();
            if (!available)
            {
                UpdateStatusText.Text = Loc.Get("Update.UpToDate");
                UpdateButton.Content = Loc.Get("Update.Check");
                UpdateButton.IsEnabled = true;
                return;
            }

            RefreshUpdateUi();
        }
        catch (Exception ex)
        {
            App.LogUpdateFailure(ex);
            UpdateStatusText.Text = Loc.Get("Update.Failed");
            UpdateButton.IsEnabled = true;
        }
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
            // Short messages - the button has a fixed width (see HotkeyCaptureControl.xaml).
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

    // App.xaml.cs sets this to true right before Application.Shutdown() -
    // otherwise Shutdown() would run into the Cancel = true below when closing windows
    // and the app might not exit properly.
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

        // The window is only hidden, not closed - opening it again from the tray is then instant.
        e.Cancel = true;
        Hide();
        App.ShowTrayHintIfNeeded();
    }

    private sealed record ActionRow(string Id, string Name, string Description);

    private sealed record ExceptionRow(string Key, string DisplayName, string? AccentColor)
    {
        // Without a stored color (app without an icon) a neutral grey dot.
        public System.Windows.Media.Brush AccentBrush { get; } = new System.Windows.Media.SolidColorBrush(
            AccentColorExtractor.FromHex(AccentColor) ?? System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A));
    }
}
