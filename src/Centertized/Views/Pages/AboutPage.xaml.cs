using System.Reflection;
using System.Windows.Controls;

namespace Centertized.Views.Pages;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version}";
    }
}
