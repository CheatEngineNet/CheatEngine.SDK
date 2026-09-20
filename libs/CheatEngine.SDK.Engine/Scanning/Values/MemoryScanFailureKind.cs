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
}
