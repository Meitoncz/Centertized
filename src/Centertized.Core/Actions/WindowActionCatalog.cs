using System.Collections;

namespace Centertized.Core.Actions;

/// <summary>
/// The only place where new actions are registered. The Shortcuts page in Settings is
/// generated straight from this list – a new action shows up there without touching XAML.
/// </summary>
public sealed class WindowActionCatalog : IReadOnlyList<IWindowAction>
{
    private readonly List<IWindowAction> _actions;

    public WindowActionCatalog(IEnumerable<IWindowAction> actions)
    {
        _actions = actions.ToList();

        var duplicateId = _actions.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException($"Duplicate action Id: '{duplicateId}'.", nameof(actions));
        }
    }

    public int Count => _actions.Count;

    public IWindowAction this[int index] => _actions[index];

    public IWindowAction? TryGetById(string id) => _actions.FirstOrDefault(a => a.Id == id);

    public IEnumerator<IWindowAction> GetEnumerator() => _actions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
