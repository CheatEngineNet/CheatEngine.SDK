namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>One architecture that Cheat Engine 7.7's runtime probes can report.</summary>
/// <remarks>
///     Values are semantic rather than the raw Lua integers. An architecture that target probes do not establish remains
///     <see cref="Unknown" /> rather than being guessed from pointer width.
/// </remarks>
public enum CheatEngineArchitecture : byte
{
    /// <summary>No architecture fact is available.</summary>
    Unknown = 0,

    /// <summary>An Intel-compatible 32-bit process.</summary>
    X86 = 1,

    /// <summary>An Intel-compatible 64-bit process.</summary>
    X64 = 2,

    /// <summary>A 32-bit ARM process.</summary>
    Arm32 = 3,

    /// <summary>A 64-bit ARM process.</summary>
    Arm64 = 4
}
