using System.Windows;
using Centertized.Services;
using Wpf.Ui.Controls;

namespace Centertized.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        WindowBackdropType = WindowBackdropType.Mica;
        VersionText.Text = Loc.Format("About.Version", AppInfo.Version);
        RepositoryLink.Content = AppInfo.RepositoryDisplay;
        RepositoryLink.NavigateUri = AppInfo.RepositoryUrl;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
