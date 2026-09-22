using Centertized.Core.WindowManagement;

namespace Centertized.Core.Actions;

public sealed record WindowActionContext(IWin32WindowService WindowService, CancellationToken CancellationToken);
