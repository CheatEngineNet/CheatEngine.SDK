namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>Classifies an explicit attempt to release a coordinated symbol-registration lease.</summary>
public enum SymbolRegistrationReleaseKind
{
    /// <summary>The coordinator removed this lease's current CE registration.</summary>
    Released,
    /// <summary>A terminal cleanup outcome had already been returned for this lease.</summary>
    AlreadyReleased,
    /// <summary>A newer registration through this SDK coordinator replaced the lease, so no CE unregister was sent.</summary>
    Superseded,
    /// <summary>The Lua attach epoch or state generation changed, so no CE unregister was sent to a new runtime.</summary>
    StaleRuntime,
    /// <summary>No unregister call began because the runtime could not currently admit the operation.</summary>
    CleanupUnavailable,
    /// <summary>An unregister call began and failed, so the host-side registration is indeterminate and will not retry.</summary>
    CleanupIndeterminate,
}
