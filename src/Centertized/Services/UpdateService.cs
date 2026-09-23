using Velopack;
using Velopack.Sources;

namespace Centertized.Services;

/// <summary>
/// Aktualizace přes Velopack z GitHub Releases. Funguje jen v nainstalované appce (vydání
/// z instalátoru) - při běhu z Visual Studia/dotnet run je <see cref="IsInstalled"/> false.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager = new(new GithubSource(AppInfo.RepositoryUrl, null, false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>Verze, která je dostupná ke stažení (po <see cref="CheckAsync"/>), jinak null.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public async Task<bool> CheckAsync()
    {
        _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pending is not null;
    }

    /// <summary>Stáhne aktualizaci a appku restartuje; po úspěchu se sem řízení nevrátí.</summary>
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
