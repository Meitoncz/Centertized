using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Centertized.Core.WindowManagement;

namespace Centertized.Services;

/// <summary>
/// Seznam aplikací pro výběr výjimek: Win32 aplikace ze zástupců ve Start menu, nainstalované
/// UWP/Store aplikace (z manifestu balíčku) a aplikace, které právě mají okno. Klíč je vždy
/// název .exe malými písmeny - stejný, jaký pro okno vrací <see cref="IWin32WindowService.GetAppIdentity"/>.
/// </summary>
public static class InstalledAppsService
{
    public sealed record Entry(AppIdentity Identity, ImageSource? Icon);

    public static Task<IReadOnlyList<Entry>> GetAsync(IWin32WindowService windowService) =>
        Task.Run<IReadOnlyList<Entry>>(() =>
        {
            var apps = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in ReadStartMenuApps())
            {
                apps.TryAdd(entry.Identity.Key, entry);
            }

            foreach (var entry in ReadPackagedApps())
            {
                apps.TryAdd(entry.Identity.Key, entry);
            }

            foreach (var window in windowService.GetTopLevelAppWindows())
            {
                var identity = windowService.GetAppIdentity(window);
                if (identity is not null)
                {
                    apps.TryAdd(identity.Key, new Entry(identity, identity.ExecutablePath is null ? null : LoadIcon(identity.ExecutablePath)));
                }
            }

            return apps.Values.OrderBy(e => e.Identity.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        });

    private static IEnumerable<Entry> ReadStartMenuApps()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            yield break;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList();
            }
            catch (Exception)
            {
                continue; // část složky nečitelná - vezmeme, co jde
            }

            foreach (var shortcut in shortcuts)
            {
                string? target;
                try
                {
                    target = shell.CreateShortcut(shortcut).TargetPath as string;
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(shortcut);
                if (IsUninstaller(name, target))
                {
                    continue;
                }

                yield return new Entry(new AppIdentity(Path.GetFileName(target).ToLowerInvariant(), name), LoadIcon(target));
            }
        }
    }

    private static bool IsUninstaller(string name, string target) =>
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("odinstal", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(target).StartsWith("unins", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Entry> ReadPackagedApps()
    {
        IReadOnlyList<Windows.ApplicationModel.Package> packages;
        try
        {
            packages = new Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty).ToList();
        }
        catch (Exception)
        {
            yield break; // UWP seznam je bonus - bez něj se výběr obejde
        }

        foreach (var package in packages)
        {
            if (package.IsFramework || package.IsResourcePackage)
            {
                continue;
            }

            IReadOnlyList<Windows.ApplicationModel.Core.AppListEntry> entries;
            Dictionary<string, string> executables;
            try
            {
                // Jen aplikace, které se opravdu ukazují v nabídce Start - ostatní jsou systémové hostitele.
                entries = package.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult().ToList();
                if (entries.Count == 0)
                {
                    continue;
                }

                executables = ReadExecutables(package.InstalledLocation.Path);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                // AppUserModelId má tvar "PackageFamilyName!ApplicationId".
                var applicationId = entry.AppUserModelId[(entry.AppUserModelId.IndexOf('!') + 1)..];
                if (executables.TryGetValue(applicationId, out var executable))
                {
                    yield return new Entry(new AppIdentity(Path.GetFileName(executable).ToLowerInvariant(), entry.DisplayInfo.DisplayName), LoadPackagedLogo(entry));
                }
            }
        }
    }

    private static Dictionary<string, string> ReadExecutables(string installLocation)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manifest = XDocument.Load(Path.Combine(installLocation, "AppxManifest.xml"));
        foreach (var application in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            var id = application.Attribute("Id")?.Value;
            var executable = application.Attribute("Executable")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(executable))
            {
                result[id] = executable;
            }
        }

        return result;
    }

    private static ImageSource? LoadPackagedLogo(Windows.ApplicationModel.Core.AppListEntry entry)
    {
        try
        {
            var reference = entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var randomAccess = reference.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            using var stream = randomAccess.AsStreamForRead();
            var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ImageSource? LoadIcon(string executablePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
