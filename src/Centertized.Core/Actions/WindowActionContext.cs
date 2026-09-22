using Centertized.Core.WindowManagement;
using Microsoft.Extensions.Logging;

namespace Centertized.Core.Actions;

public sealed record WindowActionContext(IWin32WindowService WindowService, ILogger Logger, CancellationToken CancellationToken);
