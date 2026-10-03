namespace Centertized.Core.Hotkeys;

public enum HotkeyRegistrationOutcome
{
    Success,

    /// <summary>Another action in our app already has the combination.</summary>
    AlreadyBoundInApp,

    /// <summary>Another running application has the combination registered (or Windows reserves it).</summary>
    AlreadyRegisteredExternally,
}

/// <summary>
/// Result of <see cref="HotkeyActionRegistry.TryBind"/>. <see cref="ConflictingActionId"/>
/// is filled in only for <see cref="HotkeyRegistrationOutcome.AlreadyBoundInApp"/>.
/// </summary>
public sealed record HotkeyRegistrationResult(HotkeyRegistrationOutcome Outcome, string? ConflictingActionId = null)
{
    public static HotkeyRegistrationResult Success() => new(HotkeyRegistrationOutcome.Success);

    public static HotkeyRegistrationResult ConflictsWithApp(string conflictingActionId) =>
        new(HotkeyRegistrationOutcome.AlreadyBoundInApp, conflictingActionId);

    public static HotkeyRegistrationResult ConflictsExternally() =>
        new(HotkeyRegistrationOutcome.AlreadyRegisteredExternally);
}
