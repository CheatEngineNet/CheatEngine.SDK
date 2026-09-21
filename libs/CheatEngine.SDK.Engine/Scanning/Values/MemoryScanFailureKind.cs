namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Classifies a non-state failure from a <see cref="MemoryScanSession" /> operation.</summary>
public enum MemoryScanFailureKind
{
    /// <summary>The required CE Lua global was absent or was not a function.</summary>
    MissingCapability = 0,

    /// <summary>A protected CE Lua property or method operation raised an error.</summary>
    LuaError = 1,

    /// <summary>The host returned a value that does not satisfy the documented CE Lua result shape.</summary>
    UnexpectedResult = 2,

    /// <summary>The session's persistent CE objects belong to an earlier Lua attachment or state generation.</summary>
    RuntimeInvalidated = 3,

    /// <summary>The target required by the session could not be qualified as its originally observed incarnation.</summary>
    TargetIdentityUnavailable = 4,

    /// <summary>The currently qualified target differs from the session's originally observed incarnation.</summary>
    TargetIdentityMismatch = 5,
}
