using System.Reflection;

namespace Centertized.Services;

public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/Meitoncz/Centertized";

    public static string RepositoryDisplay => "github.com/Meitoncz/Centertized";

    /// <summary>Verze bez případného "+commit" sufixu, který SDK přidává do InformationalVersion.</summary>
    public static string Version
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        }
    }
}
