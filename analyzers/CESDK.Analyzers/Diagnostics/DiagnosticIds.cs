namespace CESDK.Analyzers.Diagnostics;

/// <summary>
///     The identifiers of every diagnostic this assembly reports. Identifiers are never reused or renumbered: they are
///     what users write in <c>.editorconfig</c>, <c>#pragma warning</c> and <c>NoWarn</c>.
/// </summary>
/// <remarks>
///     Ranges: <c>CESDK0xxx</c> plugin shape and bootstrap, <c>CESDK1xxx</c> runtime-safety usage, <c>CESDK2xxx</c>
///     generator-input errors (reported by the generators' companion rules).
///     Only the identifiers listed here exist.
/// </remarks>
public static class DiagnosticIds
{
    /// <summary>A class marked <c>[CheatEnginePlugin]</c> cannot be constructed by the generated entry point.</summary>
    public const string InvalidPluginClass = "CESDK0001";

    /// <summary>More than one class in the compilation is marked <c>[CheatEnginePlugin]</c>.</summary>
    public const string MultiplePluginClasses = "CESDK0002";

    /// <summary>A plugin assembly declares a namespace that is <c>CESDK</c> or nested under it.</summary>
    public const string ReservedNamespace = "CESDK0004";

    /// <summary>An exception can escape a method marked <c>[UnmanagedCallersOnly]</c>.</summary>
    public const string UnguardedUnmanagedCallersOnly = "CESDK1004";

    /// <summary>A <c>[LuaFunction]</c> or <c>[LuaGlobal]</c> binding exists but the compilation does not allow unsafe code.</summary>
    public const string UnsafeBlocksRequired = "CESDK2001";

    /// <summary>The type that declares a <c>[LuaFunction]</c> or <c>[LuaGlobal]</c> member cannot receive a generated part.</summary>
    public const string InvalidLuaBindingContainingType = "CESDK2002";

    /// <summary>A <c>[LuaFunction]</c> method cannot be exported by a generated thunk.</summary>
    public const string InvalidLuaFunction = "CESDK2003";

    /// <summary>A <c>[LuaGlobal]</c> method cannot receive a generated body.</summary>
    public const string InvalidLuaGlobal = "CESDK2004";
}
