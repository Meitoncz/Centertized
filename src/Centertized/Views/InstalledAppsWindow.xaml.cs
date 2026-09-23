using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Centertized.Core.WindowManagement;
using Centertized.Services;
using Wpf.Ui.Controls;

namespace Centertized.Views;

/// <summary>Výběr aplikací (i hromadně přes checkboxy) pro seznam výjimek z auto-centrování.</summary>
public partial class InstalledAppsWindow : FluentWindow
{
    private readonly List<SelectableApp> _apps = [];
    private ICollectionView? _view;

    public InstalledAppsWindow()
    {
        InitializeComponent();
        WindowBackdropType = WindowBackdropType.Mica;
        Loaded += async (_, _) => await LoadAppsAsync();
    }

    private async Task LoadAppsAsync()
    {
        var entries = await InstalledAppsService.GetAsync(App.WindowService);
        var excluded = App.AppRules.ExcludedApps().ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            _apps.Add(new SelectableApp(entry.Identity, entry.Icon) { IsSelected = excluded.ContainsKey(entry.Identity.Key) });
            excluded.Remove(entry.Identity.Key);
        }

        // Vyřazená aplikace, která už není nainstalovaná/spuštěná, se v seznamu musí objevit taky -
        // jinak by se při potvrzení tiše vrátila do auto-centrování.
        foreach (var (key, name) in excluded)
        {
            _apps.Add(new SelectableApp(new AppIdentity(key, string.IsNullOrEmpty(name) ? key : name), null) { IsSelected = true });
        }

        _apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        _view = CollectionViewSource.GetDefaultView(_apps);
        AppsListBox.ItemsSource = _view;
        LoadingRing.Visibility = Visibility.Collapsed;
        AppsListBox.Visibility = Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_view is null)
        {
            return;
        }

        var text = SearchBox.Text.Trim();
        _view.Filter = string.IsNullOrEmpty(text)
            ? null
            : item => ((SelectableApp)item).Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        // Barva puntíku v seznamu výjimek = dominantní barva ikony aplikace. Bez ikony (nenalezená
        // appka) se předá null a zůstane dříve uložená barva, pokud nějaká je.
        var selected = _apps.Where(a => a.IsSelected)
            .Select(a => (a.Identity, AccentOf(a.Icon)))
            .ToList();
        App.AppRules.SetExcludedApps(selected);
        Close();
    }

    private static string? AccentOf(ImageSource? icon) =>
        icon is BitmapSource bitmap && AccentColorExtractor.FromBitmap(bitmap) is { } color
            ? AccentColorExtractor.ToHex(color)
            : null;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class SelectableApp(AppIdentity identity, ImageSource? icon)
    {
        public AppIdentity Identity { get; } = identity;

        public string Name => Identity.DisplayName;

        public ImageSource? Icon { get; } = icon;

        public Visibility NoIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSelected { get; set; }
    }
}
