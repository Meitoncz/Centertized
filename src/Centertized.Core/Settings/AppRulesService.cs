using Centertized.Core.WindowManagement;

namespace Centertized.Core.Settings;

public enum AppRuleChangeKind
{
    SizeRemembered,
    SizeCleared,
    ExclusionChanged,
    Removed,
}

public sealed record AppRuleChange(AppRuleChangeKind Kind, string Key, string DisplayName, int? Width, int? Height, bool Excluded);

/// <summary>
/// Per-app rules (auto-center exceptions, remembered sizes) on top of
/// <see cref="ISettingsStore"/>. Read from watcher threads (thread pool) and written from the UI,
/// so everything is under a lock and reads come from the in-memory copy, not from disk on every new window.
/// </summary>
public sealed class AppRulesService
{
    private readonly ISettingsStore _store;
    private readonly object _gate = new();
    private Dictionary<string, AppRule> _rules;

    public AppRulesService(ISettingsStore store)
    {
        _store = store;
        _rules = Clone(store.Load().AppRules);
    }

    /// <summary>Raised after every change (on the thread that made the change).</summary>
    public event Action<AppRuleChange>? Changed;

    public IReadOnlyList<KeyValuePair<string, AppRule>> All()
    {
        lock (_gate)
        {
            return Clone(_rules).OrderBy(kv => kv.Value.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>Apps excluded from auto-centering (key + rule), sorted by name.</summary>
    public IReadOnlyList<KeyValuePair<string, AppRule>> ExcludedApps() =>
        All().Where(kv => kv.Value.ExcludedFromAutoCenter).ToList();

    public bool IsExcluded(string key)
    {
        lock (_gate)
        {
            return _rules.TryGetValue(key, out var rule) && rule.ExcludedFromAutoCenter;
        }
    }

    /// <summary>Remembered size in 96-DPI units.</summary>
    public bool TryGetRememberedSize(string key, out int width, out int height)
    {
        lock (_gate)
        {
            if (_rules.TryGetValue(key, out var rule) && rule.HasRememberedSize)
            {
                width = rule.RememberedWidth!.Value;
                height = rule.RememberedHeight!.Value;
                return true;
            }
        }

        width = height = 0;
        return false;
    }

    public void SetRememberedSize(AppIdentity app, int width96, int height96)
    {
        Mutate(app, rule =>
        {
            rule.RememberedWidth = width96;
            rule.RememberedHeight = height96;
        }, new AppRuleChange(AppRuleChangeKind.SizeRemembered, app.Key, app.DisplayName, width96, height96, false));
    }

    public void ClearRememberedSize(string key)
    {
        MutateByKey(key, rule =>
        {
            rule.RememberedWidth = null;
            rule.RememberedHeight = null;
        }, AppRuleChangeKind.SizeCleared);
    }

    public void SetExcluded(AppIdentity app, bool excluded)
    {
        Mutate(app, rule => rule.ExcludedFromAutoCenter = excluded,
            new AppRuleChange(AppRuleChangeKind.ExclusionChanged, app.Key, app.DisplayName, null, null, excluded));
    }

    public void SetExcluded(string key, bool excluded)
    {
        MutateByKey(key, rule => rule.ExcludedFromAutoCenter = excluded, AppRuleChangeKind.ExclusionChanged);
    }

    /// <summary>
    /// Sets the list of exceptions at once: apps in excluded get excluded, the other currently excluded
    /// ones are included again. One save and one notification instead of dozens.
    /// </summary>
    public void SetExcludedApps(IReadOnlyCollection<(AppIdentity App, string? AccentColor)> excluded)
    {
        lock (_gate)
        {
            var wanted = excluded.ToDictionary(a => a.App.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var key in _rules.Where(kv => kv.Value.ExcludedFromAutoCenter && !wanted.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList())
            {
                _rules[key].ExcludedFromAutoCenter = false;
                if (_rules[key].IsEmpty)
                {
                    _rules.Remove(key);
                }
            }

            foreach (var (app, accentColor) in wanted.Values)
            {
                if (!_rules.TryGetValue(app.Key, out var rule))
                {
                    rule = new AppRule();
                    _rules[app.Key] = rule;
                }

                rule.DisplayName = app.DisplayName;
                rule.ExcludedFromAutoCenter = true;
                rule.AccentColor = accentColor ?? rule.AccentColor;
            }

            Persist();
        }

        Changed?.Invoke(new AppRuleChange(AppRuleChangeKind.ExclusionChanged, "", "", null, null, true));
    }

    /// <summary>Forgets all remembered sizes (auto-center exceptions stay).</summary>
    public void ClearAllRememberedSizes()
    {
        lock (_gate)
        {
            foreach (var key in _rules.Keys.ToList())
            {
                var rule = _rules[key];
                rule.RememberedWidth = null;
                rule.RememberedHeight = null;
                if (rule.IsEmpty)
                {
                    _rules.Remove(key);
                }
            }

            Persist();
        }

        Changed?.Invoke(new AppRuleChange(AppRuleChangeKind.SizeCleared, "", "", null, null, false));
    }

    public void Remove(string key)
    {
        string name;
        lock (_gate)
        {
            if (!_rules.Remove(key, out var removed))
            {
                return;
            }

            name = removed.DisplayName;
            Persist();
        }

        Changed?.Invoke(new AppRuleChange(AppRuleChangeKind.Removed, key, name, null, null, false));
    }

    private void Mutate(AppIdentity app, Action<AppRule> change, AppRuleChange notification)
    {
        lock (_gate)
        {
            if (!_rules.TryGetValue(app.Key, out var rule))
            {
                rule = new AppRule();
                _rules[app.Key] = rule;
            }

            // The display name is occasionally refined (e.g. after an app update).
            rule.DisplayName = app.DisplayName;
            change(rule);
            if (rule.IsEmpty)
            {
                _rules.Remove(app.Key);
            }

            Persist();
        }

        Changed?.Invoke(notification);
    }

    private void MutateByKey(string key, Action<AppRule> change, AppRuleChangeKind kind)
    {
        AppRuleChange notification;
        lock (_gate)
        {
            if (!_rules.TryGetValue(key, out var rule))
            {
                return;
            }

            change(rule);
            notification = new AppRuleChange(kind, key, rule.DisplayName, rule.RememberedWidth, rule.RememberedHeight, rule.ExcludedFromAutoCenter);
            if (rule.IsEmpty)
            {
                _rules.Remove(key);
            }

            Persist();
        }

        Changed?.Invoke(notification);
    }

    // Load-change-save of the whole settings: other parts of the app (the Settings window) save
    // the other items the same way, so nothing but the rules may be overwritten here.
    private void Persist()
    {
        var settings = _store.Load();
        settings.AppRules = Clone(_rules);
        _store.Save(settings);
    }

    private static Dictionary<string, AppRule> Clone(Dictionary<string, AppRule> source) =>
        source.ToDictionary(
            kv => kv.Key,
            kv => new AppRule
            {
                DisplayName = kv.Value.DisplayName,
                AccentColor = kv.Value.AccentColor,
                ExcludedFromAutoCenter = kv.Value.ExcludedFromAutoCenter,
                RememberedWidth = kv.Value.RememberedWidth,
                RememberedHeight = kv.Value.RememberedHeight,
            },
            StringComparer.OrdinalIgnoreCase);
}
