namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Controls persistence of a user-defined Cheat Engine symbol registration.</summary>
/// <remarks>
///     <see cref="DoNotSave" /> maps directly to CE's optional <c>donotsave</c> argument. It does not create a managed
///     owner: a registration changes the host-wide symbol table, and callers that need a lease must retain the name and
///     unregister it during their own qualified cleanup phase.
/// </remarks>
/// <param name="DoNotSave">Whether CE must omit the registration when saving the table.</param>
public readonly record struct SymbolRegistrationOptions(bool DoNotSave = false);
