using Velopack;
using Velopack.Sources;

namespace Centertized.Services;

/// <summary>
/// Updates via Velopack from GitHub Releases. Works only in an installed app (a release
/// from the installer) - when run from Visual Studio/dotnet run, <see cref="IsInstalled"/> is false.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager = new(new GithubSource(AppInfo.RepositoryUrl, null, false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The version available for download (after <see cref="CheckAsync"/>), otherwise null.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public async Task<bool> CheckAsync()
    {
        _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pending is not null;
    }

    /// <summary>Downloads the update and restarts the app; on success control doesn't return here.</summary>
    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        if (_pending is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pending, progress).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}
