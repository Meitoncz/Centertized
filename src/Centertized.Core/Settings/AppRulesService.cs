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
/// Pravidla podle aplikace (výjimky z auto-centrování, zapamatované velikosti) nad
/// <see cref="ISettingsStore"/>. Čte se z watcheru na vláknech z thread poolu a zapisuje z UI,
/// proto je vše za zámkem a čtení jdou z paměťové kopie, ne z disku při každém novém okně.
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

    /// <summary>Vyvolá se po každé změně (na vlákně, které změnu provedlo).</summary>
    public event Action<AppRuleChange>? Changed;

    public IReadOnlyList<KeyValuePair<string, AppRule>> All()
    {
        lock (_gate)
        {
            return Clone(_rules).OrderBy(kv => kv.Value.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>Aplikace vyřazené z auto-centrování (klíč + pravidlo), řazené podle názvu.</summary>
    public IReadOnlyList<KeyValuePair<string, AppRule>> ExcludedApps() =>
        All().Where(kv => kv.Value.ExcludedFromAutoCenter).ToList();

    public bool IsExcluded(string key)
    {
        lock (_gate)
        {
            return _rules.TryGetValue(key, out var rule) && rule.ExcludedFromAutoCenter;
        }
    }

    /// <summary>Zapamatovaná velikost v 96 DPI jednotkách.</summary>
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
    /// Nastaví seznam výjimek najednou: aplikace v excluded se vyřadí, ostatní dosud vyřazené
    /// se vrátí. Jedno uložení a jedno oznámení místo desítek.
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

    /// <summary>Zapomene všechny zapamatované velikosti (výjimky z auto-centrování zůstanou).</summary>
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

            // Zobrazované jméno se občas zpřesní (např. po aktualizaci appky).
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

    // Načíst-změnit-uložit celé nastavení: jiné části appky (Nastavení okno) ukládají
    // ostatní položky stejným způsobem, takže se tu nesmí přepsat nic jiného než pravidla.
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
