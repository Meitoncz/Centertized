using Centertized.Core.Hotkeys;

namespace Centertized.Tests.Hotkeys;

/// <summary>A registrar for tests – simulates "hotkey X is already taken from outside" without Win32 calls.</summary>
internal sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    private readonly HashSet<Hotkey> _externallyTaken;
    public List<(int Id, Hotkey Hotkey)> Registered { get; } = new();
    public List<int> Unregistered { get; } = new();

    public FakeHotkeyRegistrar(params Hotkey[] externallyTaken)
    {
        _externallyTaken = externallyTaken.ToHashSet();
    }

    public HotkeyRegistrarOutcome TryRegister(IntPtr windowHandle, int id, Hotkey hotkey)
    {
        if (_externallyTaken.Contains(hotkey))
        {
            return HotkeyRegistrarOutcome.AlreadyRegisteredElsewhere;
        }

        Registered.Add((id, hotkey));
        return HotkeyRegistrarOutcome.Success;
    }

    public void Unregister(IntPtr windowHandle, int id) => Unregistered.Add(id);
}
