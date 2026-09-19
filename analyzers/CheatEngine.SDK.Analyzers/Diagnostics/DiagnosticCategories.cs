namespace CheatEngine.SDK.Analyzers.Diagnostics;

/// <summary>The category names of the CheatEngine.SDK diagnostics.</summary>
internal static class DiagnosticCategories
{
    /// <summary>Plugin shape and bootstrap rules (<c>CESDK0xxx</c>).</summary>
    public const string Plugin = "CheatEngine.SDK.Plugin";

    /// <summary>Runtime-safety usage rules (<c>CESDK1xxx</c>).</summary>
    public const string Usage = "CheatEngine.SDK.Usage";

    /// <summary>Generator-input rules (<c>CESDK2xxx</c>): shapes the LuaBindings generator silently skips.</summary>
    public const string Generation = "CheatEngine.SDK.Generation";
}
