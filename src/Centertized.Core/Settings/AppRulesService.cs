using Centertized.Core.WindowManagement;

namespace Centertized.Core.Settings;

public enum AppRuleChangeKind
{
    SizeRemembered,
    SizeCleared,
    ExclusionChanged,
    Removed,
    /// <summary>Uživatel chtěl vrátit zapamatovanou velikost, ale pro appku žádná není.</summary>
    NothingRemembered,
    /// <summary>Akci nešlo provést, protože se nepodařilo zjistit, které appce okno patří.</summary>
    AppUnknown,
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

    /// <summary>Oznámí uživateli výsledek akce, která nic nezměnila (např. "nic zapamatováno").</summary>
    public void Announce(AppRuleChange change) => Changed?.Invoke(change);

    public IReadOnlyList<KeyValuePair<string, AppRule>> All()
    {
        lock (_gate)
        {
            return Clone(_rules).OrderBy(kv => kv.Value.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

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
                ExcludedFromAutoCenter = kv.Value.ExcludedFromAutoCenter,
                RememberedWidth = kv.Value.RememberedWidth,
                RememberedHeight = kv.Value.RememberedHeight,
            },
            StringComparer.OrdinalIgnoreCase);
}
