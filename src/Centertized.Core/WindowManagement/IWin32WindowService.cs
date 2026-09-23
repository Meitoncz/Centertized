namespace Centertized.Core.WindowManagement;

/// <summary>
/// Fasáda nad Win32 voláními pro práci s okny/monitory. Existuje hlavně kvůli
/// testovatelnosti – <see cref="Actions.CenterActiveWindowAction"/> se dá testovat
/// s fake implementací místo skutečného okna na obrazovce.
/// </summary>
public interface IWin32WindowService
{
    IntPtr GetForegroundWindowHandle();

    /// <summary>
    /// Jestli má vůbec smysl s tímhle oknem něco dělat – vlastní okna appky, desktop
    /// (Progman/WorkerW), tool windows bez WS_EX_APPWINDOW apod. se přeskakují.
    /// </summary>
    bool IsEligibleForActions(IntPtr windowHandle);

    bool IsMinimized(IntPtr windowHandle);

    string GetWindowClassName(IntPtr windowHandle);

    /// <summary>
    /// Má okno standardní záhlaví (WS_CAPTION)? Odlišuje běžná okna aplikací od notifikací
    /// (toasty), overlayů, popupů a shellových oken, která se automaticky centrovat nemají.
    /// </summary>
    bool HasTitleBar(IntPtr windowHandle);

    /// <summary>Jde okno myší zvětšovat/zmenšovat (WS_THICKFRAME)? Jinak mu velikost nastavovat nemá smysl.</summary>
    bool IsResizable(IntPtr windowHandle);

    /// <summary>DPI monitoru, na kterém okno je (96 = 100 %).</summary>
    int GetDpi(IntPtr windowHandle);

    /// <summary>
    /// Aplikace, které okno patří - u UWP oken skutečná appka, ne ApplicationFrameHost.exe.
    /// Null, když se identitu nepodaří zjistit (např. UWP rámec, který ještě nemá obsah).
    /// </summary>
    AppIdentity? GetAppIdentity(IntPtr windowHandle);

    /// <summary>Viditelná běžná okna aplikací (se záhlavím, způsobilá pro akce) - pro výběr "spuštěné aplikace".</summary>
    IReadOnlyList<IntPtr> GetTopLevelAppWindows();

    /// <summary>Krátký textový popis okna (třída, styly, proces) pro diagnostiku v logu.</summary>
    string DescribeWindow(IntPtr windowHandle);

    bool IsMaximized(IntPtr windowHandle);

    void Restore(IntPtr windowHandle);

    void Maximize(IntPtr windowHandle);

    /// <summary>Skutečné vizuální hranice (DWM extended frame bounds), ne GetWindowRect.</summary>
    bool TryGetVisualBounds(IntPtr windowHandle, out WindowRect bounds);

    bool TryGetWindowRect(IntPtr windowHandle, out WindowRect bounds);

    /// <summary>Pracovní plocha (bez taskbaru) monitoru, na kterém okno aktuálně je.</summary>
    bool TryGetMonitorWorkArea(IntPtr windowHandle, out WindowRect workArea);

    /// <summary>Přesune okno beze změny velikosti a bez krádeže focusu; true = SetWindowPos uspěl.</summary>
    bool TrySetPosition(IntPtr windowHandle, int left, int top);

    /// <summary>Nastaví pozici i velikost najednou (GetWindowRect "jazyk"), bez krádeže focusu.</summary>
    bool TrySetBounds(IntPtr windowHandle, WindowRect bounds);
}
