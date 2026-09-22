using Centertized.Core.Actions;

namespace Centertized.Tests.Hotkeys;

internal sealed class StubWindowAction : IWindowAction
{
    public int ExecuteCount { get; private set; }

    public StubWindowAction(string id) => Id = id;

    public string Id { get; }
    public string DisplayName => Id;
    public string Description => string.Empty;

    public Task ExecuteAsync(WindowActionContext context)
    {
        ExecuteCount++;
        return Task.CompletedTask;
    }
}
