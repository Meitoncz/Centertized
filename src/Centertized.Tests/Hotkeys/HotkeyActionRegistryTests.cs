using Centertized.Core.Actions;
using Centertized.Core.Hotkeys;
using Centertized.Core.WindowManagement;

namespace Centertized.Tests.Hotkeys;

public class HotkeyActionRegistryTests
{
    private static readonly Hotkey CenterHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'C');
    private static readonly Hotkey OtherHotkey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'V');

    // Testy tady neřeší window management, takže stačí reálná (nikdy nevolaná) instance.
    private static readonly IWin32WindowService WindowService = new Win32WindowService();

    private static HotkeyActionRegistry CreateRegistry(IHotkeyRegistrar registrar, params IWindowAction[] actions) =>
        new(registrar, new WindowActionCatalog(actions), WindowService, IntPtr.Zero);

    [Fact]
    public void TryBind_NewHotkey_Succeeds()
    {
        var action = new StubWindowAction("action-a");
        var registry = CreateRegistry(new FakeHotkeyRegistrar(), action);

        var result = registry.TryBind(action.Id, CenterHotkey);

        Assert.Equal(HotkeyRegistrationOutcome.Success, result.Outcome);
        Assert.Equal(CenterHotkey, registry.CurrentBindings[action.Id]);
    }

    [Fact]
    public void TryBind_UnknownActionId_Throws()
    {
        var registry = CreateRegistry(new FakeHotkeyRegistrar());

        Assert.Throws<ArgumentException>(() => registry.TryBind("neexistuje", CenterHotkey));
    }

    [Fact]
    public void TryBind_SameHotkeyOnDifferentAction_ReportsIntraAppConflict()
    {
        var actionA = new StubWindowAction("action-a");
        var actionB = new StubWindowAction("action-b");
        var registry = CreateRegistry(new FakeHotkeyRegistrar(), actionA, actionB);
        registry.TryBind(actionA.Id, CenterHotkey);

        var result = registry.TryBind(actionB.Id, CenterHotkey);

        Assert.Equal(HotkeyRegistrationOutcome.AlreadyBoundInApp, result.Outcome);
        Assert.Equal(actionA.Id, result.ConflictingActionId);
        Assert.False(registry.CurrentBindings.ContainsKey(actionB.Id));
    }

    [Fact]
    public void TryBind_HotkeyTakenByOtherProcess_ReportsExternalConflict()
    {
        var action = new StubWindowAction("action-a");
        var registrar = new FakeHotkeyRegistrar(CenterHotkey);
        var registry = CreateRegistry(registrar, action);

        var result = registry.TryBind(action.Id, CenterHotkey);

        Assert.Equal(HotkeyRegistrationOutcome.AlreadyRegisteredExternally, result.Outcome);
        Assert.False(registry.CurrentBindings.ContainsKey(action.Id));
    }

    [Fact]
    public void TryBind_Rebind_UnregistersPreviousHotkeyFirst()
    {
        var action = new StubWindowAction("action-a");
        var registrar = new FakeHotkeyRegistrar();
        var registry = CreateRegistry(registrar, action);
        registry.TryBind(action.Id, CenterHotkey);

        var result = registry.TryBind(action.Id, OtherHotkey);

        Assert.Equal(HotkeyRegistrationOutcome.Success, result.Outcome);
        Assert.Equal(OtherHotkey, registry.CurrentBindings[action.Id]);
        Assert.Single(registrar.Unregistered);
    }

    [Fact]
    public void Unbind_RemovesBindingAndUnregisters()
    {
        var action = new StubWindowAction("action-a");
        var registrar = new FakeHotkeyRegistrar();
        var registry = CreateRegistry(registrar, action);
        registry.TryBind(action.Id, CenterHotkey);

        registry.Unbind(action.Id);

        Assert.False(registry.CurrentBindings.ContainsKey(action.Id));
        Assert.Single(registrar.Unregistered);
    }

    [Fact]
    public async Task Dispatch_KnownId_ExecutesBoundAction()
    {
        var action = new StubWindowAction("action-a");
        var registrar = new FakeHotkeyRegistrar();
        var registry = CreateRegistry(registrar, action);
        registry.TryBind(action.Id, CenterHotkey);
        var boundId = registrar.Registered.Single().Id;

        registry.Dispatch(boundId);
        await Task.Delay(20); // Dispatch spouští ExecuteAsync fire-and-forget

        Assert.Equal(1, action.ExecuteCount);
    }

    [Fact]
    public void Dispatch_UnknownId_DoesNothing()
    {
        var action = new StubWindowAction("action-a");
        var registry = CreateRegistry(new FakeHotkeyRegistrar(), action);

        registry.Dispatch(999);

        Assert.Equal(0, action.ExecuteCount);
    }
}
