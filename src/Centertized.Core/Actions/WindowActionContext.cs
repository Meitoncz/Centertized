namespace Centertized.Core.Actions;

/// <summary>
/// Zatím jen CancellationToken – ve Fázi 3 se sem přidá IWin32WindowService (a co dalšího
/// bude centrovací akce potřebovat), aniž by se muselo sahat na IWindowAction.ExecuteAsync
/// signaturu znovu.
/// </summary>
public sealed record WindowActionContext(CancellationToken CancellationToken);
