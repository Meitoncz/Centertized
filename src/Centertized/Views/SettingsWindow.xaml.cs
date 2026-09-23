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
        ApplyTheme(settings.ThemeMode);

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
        RememberedSizesToggle.IsChecked = settings.ApplyRememberedSizes;
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
        var rows = App.AppRules.All().Select(kv => new AppRuleRow(kv.Key, kv.Value)).ToList();
        AppsList.ItemsSource = rows;
        AppsEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAppRulesChanged(AppRuleChange change) => Dispatcher.BeginInvoke(RefreshApps);

    private void OnLanguageChanged() => Dispatcher.BeginInvoke(RefreshLocalizedContent);

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

    private void RememberedSizesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var settings = App.SettingsStore.Load();
        settings.ApplyRememberedSizes = RememberedSizesToggle.IsChecked == true;
        App.SettingsStore.Save(settings);
        App.ApplyRememberedSizes = settings.ApplyRememberedSizes;
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

    private void AppAutoCenterToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleSwitch)sender;
        var key = (string)toggle.Tag;
        var excluded = toggle.IsChecked != true;

        // Toggle se při sestavení řádku sám nastaví z pravidla a vyvolá Checked/Unchecked -
        // bez téhle kontroly by se změna hned zapsala zpět a seznam se donekonečna stavěl.
        if (App.AppRules.IsExcluded(key) != excluded)
        {
            App.AppRules.SetExcluded(key, excluded);
        }
    }

    private void AppForgetSize_Click(object sender, RoutedEventArgs e) =>
        App.AppRules.ClearRememberedSize((string)((FrameworkElement)sender).Tag);

    private void AppRemove_Click(object sender, RoutedEventArgs e) =>
        App.AppRules.Remove((string)((FrameworkElement)sender).Tag);

    // Nabídka právě spuštěných aplikací (okna se záhlavím). Přidání = zapamatuje se
    // aktuální velikost jejího okna (hlavní use case), u nezvětšitelných oken se místo
    // toho aplikace vyřadí z auto-centrování.
    private void AddRunningAppButton_Click(object sender, RoutedEventArgs e)
    {
        var windowService = App.WindowService;
        var known = App.AppRules.All().Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, (AppIdentity App, IntPtr Window)>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windowService.GetTopLevelAppWindows())
        {
            var app = windowService.GetAppIdentity(window);
            if (app is null || known.Contains(app.Key) || candidates.ContainsKey(app.Key) ||
                app.Key.Equals(Environment.ProcessPath is null ? "" : System.IO.Path.GetFileName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates[app.Key] = (app, window);
        }

        RunningAppsMenu.Items.Clear();
        foreach (var (_, (app, window)) in candidates.OrderBy(c => c.Value.App.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new System.Windows.Controls.MenuItem { Header = app.DisplayName };
            item.Click += (_, _) => AddRule(app, window);
            RunningAppsMenu.Items.Add(item);
        }

        if (RunningAppsMenu.Items.Count == 0)
        {
            RunningAppsMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = Loc.Get("Apps.AddRunning.None"), IsEnabled = false });
        }

        RunningAppsMenu.PlacementTarget = AddRunningAppButton;
        RunningAppsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        RunningAppsMenu.IsOpen = true;
    }

    private static void AddRule(AppIdentity app, IntPtr window)
    {
        var windowService = App.WindowService;
        if (windowService.IsResizable(window) && !windowService.IsMinimized(window) && !windowService.IsMaximized(window) &&
            windowService.TryGetWindowRect(window, out var rect))
        {
            var dpi = windowService.GetDpi(window);
            App.AppRules.SetRememberedSize(app, (int)Math.Round(rect.Width * 96.0 / dpi), (int)Math.Round(rect.Height * 96.0 / dpi));
        }
        else
        {
            App.AppRules.SetExcluded(app, true);
        }
    }

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

    private sealed class AppRuleRow(string key, AppRule rule)
    {
        public string Key { get; } = key;

        public string DisplayName { get; } = rule.DisplayName;

        public bool HasSize { get; } = rule.HasRememberedSize;

        public bool AutoCenterEnabled { get; } = !rule.ExcludedFromAutoCenter;

        public string SizeText { get; } = rule.HasRememberedSize
            ? Loc.Format("Apps.Size.Remembered", rule.RememberedWidth!, rule.RememberedHeight!)
            : Loc.Get("Apps.Size.None");
    }
}
