using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.Analyzers.WellKnown;

/// <summary>
///     Metadata names of the types the rules are driven by. They are resolved once per compilation, in the
///     compilation-start action of each analyzer; an analyzer whose types are absent registers nothing.
/// </summary>
internal static class WellKnownTypeNames
{
    /// <summary>The marker attribute of a plugin class.</summary>
    public const string CheatEnginePluginAttribute = AnnotationsMetadataNames.CheatEnginePluginAttribute;

    /// <summary>The base class of every plugin.</summary>
    public const string CheatEnginePluginBase = AnnotationsMetadataNames.CheatEnginePluginBase;

    /// <summary>The attribute that turns a static method into a native-callable entry.</summary>
    public const string UnmanagedCallersOnlyAttribute = "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute";

    /// <summary>The root of the exception hierarchy: the type a catch-all clause names.</summary>
    public const string Exception = "System.Exception";

    /// <summary>
    ///     Marks a method that never returns normally; in a catch or finally block CESDK1004 reads a call of it as a
    ///     throw.
    /// </summary>
    public const string DoesNotReturnAttribute = "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute";

    /// <summary>
    ///     Declares <c>FailFast</c> and <c>Exit</c>: marked <c>[DoesNotReturn]</c>, but they end the process instead of
    ///     throwing, so CESDK1004 does not read them as a throw.
    /// </summary>
    public const string Environment = "System.Environment";

    /// <summary>
    ///     On a constructor: it sets every <see langword="required" /> member, so <c>new T()</c> needs no object
    ///     initializer (CESDK0001).
    /// </summary>
    public const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    /// <summary>With <c>error: true</c>, naming the marked symbol is a compiler error that no pragma silences (CESDK0001).</summary>
    public const string ObsoleteAttribute = "System.ObsoleteAttribute";

    /// <summary>Exports a managed static method to Lua as a global C function (CESDK2xxx).</summary>
    public const string LuaFunctionAttribute = AnnotationsMetadataNames.LuaFunctionAttribute;

    /// <summary>Binds a partial member to a Lua global (CESDK2xxx).</summary>
    public const string LuaGlobalAttribute = AnnotationsMetadataNames.LuaGlobalAttribute;
}
