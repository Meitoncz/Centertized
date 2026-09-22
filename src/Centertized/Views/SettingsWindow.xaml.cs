using System.ComponentModel;
using Centertized.Views.Pages;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Centertized.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow()
    {
        InitializeComponent();

        // Mica pozadí + sync se světlým/tmavým/accent tématem systému naživo.
        SystemThemeWatcher.Watch(this);

        // Bez DI kontejneru – stránky se řeší jednoduše přes Activator.CreateInstance.
        RootNavigation.SetPageProviderService(new SimplePageProvider());

        // Navigate() nejde volat rovnou tady – template NavigationView se aplikuje
        // až při loadu, do té doby jsou jeho vnitřní části (content presenter) null
        // a Navigate spadne na NullReferenceException uvnitř WPF-UI.
        RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(typeof(ShortcutsPage), null);
    }

    // App.xaml.cs si tohle nastaví na true těsně před Application.Shutdown() –
    // jinak by Shutdown() při zavírání oken narazil na Cancel = true níž a appka
    // by se nemusela korektně ukončit.
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        // Okno se jen schová, ne zavře – příští otevření z tray je pak okamžité
        // a nemusí se znovu platit za vytvoření Mica kompozice a stránek.
        e.Cancel = true;
        Hide();
    }

    private sealed class SimplePageProvider : INavigationViewPageProvider
    {
        public object GetPage(Type pageType) => Activator.CreateInstance(pageType)!;
    }
}
