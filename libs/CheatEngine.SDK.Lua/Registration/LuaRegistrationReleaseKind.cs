namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Classifies a completed lease release attempt.</summary>
public enum LuaRegistrationReleaseKind
{
    /// <summary>No cleanup was necessary or attempted.</summary>
    NotAttempted = 0,

    /// <summary>Every still-owned entry was restored or removed, and replacements were left alone.</summary>
    Released = 1,

    /// <summary>One or more cleanup operations failed; the named failures are available independently.</summary>
    PartiallyReleased = 2,

    /// <summary>The lease belonged to an earlier attachment or reset generation, so no Lua operation was attempted.</summary>
    Stale = 3,

    /// <summary>The lease had already been released or disposed.</summary>
    AlreadyReleased = 4,
}
