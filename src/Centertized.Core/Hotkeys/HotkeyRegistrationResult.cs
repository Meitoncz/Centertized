namespace Centertized.Core.Hotkeys;

public enum HotkeyRegistrationOutcome
{
    Success,

    /// <summary>Kombinaci má v naší appce už jiná akce.</summary>
    AlreadyBoundInApp,

    /// <summary>Kombinaci má zaregistrovanou jiná běžící aplikace (nebo ji rezervuje Windows).</summary>
    AlreadyRegisteredExternally,
}

/// <summary>
/// Výsledek <see cref="HotkeyActionRegistry.TryBind"/>. <see cref="ConflictingActionId"/>
/// je vyplněné jen pro <see cref="HotkeyRegistrationOutcome.AlreadyBoundInApp"/>.
/// </summary>
public sealed record HotkeyRegistrationResult(HotkeyRegistrationOutcome Outcome, string? ConflictingActionId = null)
{
    public static HotkeyRegistrationResult Success() => new(HotkeyRegistrationOutcome.Success);

    public static HotkeyRegistrationResult ConflictsWithApp(string conflictingActionId) =>
        new(HotkeyRegistrationOutcome.AlreadyBoundInApp, conflictingActionId);

    public static HotkeyRegistrationResult ConflictsExternally() =>
        new(HotkeyRegistrationOutcome.AlreadyRegisteredExternally);
}
