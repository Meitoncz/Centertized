using System.Windows;
using System.Windows.Controls;
using Centertized.Services;

namespace Centertized.Views.Pages;

public partial class GeneralPage : Page
{
    private readonly AutostartService _autostartService = new();
    private bool _isInitializing;

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        StartWithWindowsToggle.IsChecked = _autostartService.IsEnabled();
        _isInitializing = false;
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _autostartService.SetEnabled(StartWithWindowsToggle.IsChecked == true);
    }
}
