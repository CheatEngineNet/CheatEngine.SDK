using Microsoft.CodeAnalysis;

namespace CESDK.Analyzers.Diagnostics;

/// <summary>
///     Every <see cref="DiagnosticDescriptor" /> of the assembly, in one place: identifier, category, severity, texts and
///     help link are reviewed together and stay in step with <c>AnalyzerReleases.Unshipped.md</c> and
///     <c>analyzers/docs/</c>.
/// </summary>
internal static class DiagnosticDescriptors
{
    // One markdown page per rule, named after the identifier.
    private const string HelpLinkBase = "https://github.com/ShadowNineX/CESDK/blob/main/analyzers/docs/";

    /// <summary>CESDK0001. Message arguments: the class name, then the sentence fragment describing the problem.</summary>
    public static readonly DiagnosticDescriptor InvalidPluginClass = new(
        DiagnosticIds.InvalidPluginClass,
        "Plugin class cannot be constructed by the generated entry point",
        "Plugin class '{0}' {1}",
        DiagnosticCategories.Plugin,
        DiagnosticSeverity.Error,
        true,
        "The entry point generated into a plugin assembly creates the plugin with 'new' from a top-level type of the same assembly. "
        + "The class marked [CheatEnginePlugin] must therefore be a non-static, non-abstract, non-generic class that is not nested in a generic type, "
        + "derives from CESDK.Hosting.Plugin.CheatEnginePlugin, is at least internal at every nesting level, is not file-local, does not take the reserved type name 'CESDK.CESDK', "
        + "has a public or internal parameterless constructor that 'new' can call without an object initializer (no required members left unset, no [Obsolete] error) "
        + "and carries a non-empty display name. When any of this is violated the generator emits nothing, or emits code that does not compile, and Cheat Engine cannot load the plugin.",
        HelpLinkBase + DiagnosticIds.InvalidPluginClass + ".md");

    /// <summary>CESDK0002 (compilation end). Message arguments: the class name, then the number of plugin classes found.</summary>
    public static readonly DiagnosticDescriptor MultiplePluginClasses = new(
        DiagnosticIds.MultiplePluginClasses,
        "More than one plugin class in the assembly",
        "'{0}' is one of {1} classes marked [CheatEnginePlugin]; a plugin assembly must contain exactly one, because Cheat Engine calls a single entry point per assembly",
        DiagnosticCategories.Plugin,
        DiagnosticSeverity.Error,
        true,
        "Cheat Engine loads one plugin per assembly through one fixed entry point. With several classes marked [CheatEnginePlugin] the generator cannot choose, "
        + "emits nothing, and Cheat Engine cannot load the plugin. Keep the attribute on exactly one class, or move the other plugins to their own assemblies.",
        HelpLinkBase + DiagnosticIds.MultiplePluginClasses + ".md",
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>CESDK0004 (compilation end). Message argument: the declared namespace.</summary>
    public static readonly DiagnosticDescriptor ReservedNamespace = new(
        DiagnosticIds.ReservedNamespace,
        "Plugin assembly declares a namespace under 'CESDK'",
        "Namespace '{0}' is 'CESDK' or nested under it in a plugin assembly. Cheat Engine forces a type named 'CESDK.CESDK' into every plugin assembly, "
        + "so inside that namespace the simple name 'CESDK' binds to the type instead of the SDK namespaces. Use a different root namespace.",
        DiagnosticCategories.Plugin,
        DiagnosticSeverity.Warning,
        true,
        "Cheat Engine looks up the hard-coded type 'CESDK.CESDK' inside the plugin assembly, so the entry point generator has to emit a class named 'CESDK' into namespace 'CESDK'. "
        + "Name lookup walks the enclosing namespaces before the global namespace: in code placed in 'CESDK' or 'CESDK.Something', the simple name 'CESDK' finds that class first, "
        + "and 'CESDK.Hosting.Plugin.CheatEnginePlugin' or 'using CESDK.Lua.State;' written inside the namespace stop compiling (CS0426). Plugin code belongs in a namespace that does not start with 'CESDK'.",
        HelpLinkBase + DiagnosticIds.ReservedNamespace + ".md",
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>CESDK1004. Message argument: the method name.</summary>
    public static readonly DiagnosticDescriptor UnguardedUnmanagedCallersOnly = new(
        DiagnosticIds.UnguardedUnmanagedCallersOnly,
        "Exception can escape an [UnmanagedCallersOnly] method",
        "An exception can escape '{0}', which native code calls directly: the whole body must be one try statement whose catch-all clause returns a failure value",
        DiagnosticCategories.Usage,
        DiagnosticSeverity.Warning,
        true,
        "A managed exception that unwinds out of an [UnmanagedCallersOnly] method terminates the host process (Cheat Engine). "
        + "The rule is structural: every top-level statement of the body must be a try statement with a 'catch' or 'catch (System.Exception)' clause without a filter, "
        + "a local declaration or return whose value cannot throw, a local function declaration, an empty statement, or a nested block of such statements. "
        + "No catch or finally block of such a try statement may contain a throw or a call of a [DoesNotReturn] method other than Environment.FailFast and Environment.Exit.",
        HelpLinkBase + DiagnosticIds.UnguardedUnmanagedCallersOnly + ".md");

    /// <summary>CESDK2001. Message argument: the member name.</summary>
    public static readonly DiagnosticDescriptor UnsafeBlocksRequired = new(
        DiagnosticIds.UnsafeBlocksRequired,
        "Lua binding needs AllowUnsafeBlocks",
        "'{0}' is a Lua binding, but this compilation does not allow unsafe code (AllowUnsafeBlocks); the generator emits nothing for any [LuaFunction] or [LuaGlobal] member until it is enabled",
        DiagnosticCategories.Generation,
        DiagnosticSeverity.Error,
        true,
        "The generated registration table takes the address of the [UnmanagedCallersOnly] thunks the LuaBindings generator emits, which needs unsafe code. "
        + "When the compiling project does not set <AllowUnsafeBlocks>true</AllowUnsafeBlocks>, the generator reads that from the compilation and emits nothing at all for either [LuaFunction] or [LuaGlobal], "
        + "for every member of every type, valid shapes included. The CESDK package sets it for consumers by default (build/CESDK.props); a project that consumes the analyzers without that prop sets it itself.",
        HelpLinkBase + DiagnosticIds.UnsafeBlocksRequired + ".md");

    /// <summary>CESDK2002. Message arguments: the member name, then the sentence fragment describing the problem.</summary>
    public static readonly DiagnosticDescriptor InvalidLuaBindingContainingType = new(
        DiagnosticIds.InvalidLuaBindingContainingType,
        "Type cannot receive a generated Lua binding part",
        "'{0}' is a Lua binding, but its containing type {1}",
        DiagnosticCategories.Generation,
        DiagnosticSeverity.Error,
        true,
        "The LuaBindings generator adds a generated part to the type that declares a [LuaFunction] or [LuaGlobal] member. "
        + "That type, and every type it is nested in, must be a partial, non-generic class or struct that is not a file-local type. "
        + "When any of this is violated the generator emits nothing for the member, and this rule names the cause.",
        HelpLinkBase + DiagnosticIds.InvalidLuaBindingContainingType + ".md");

    /// <summary>
    ///     CESDK2003. Message arguments: the method name, then the sentence fragment describing the problem. Reported
    ///     from both a symbol action (eleven of its twelve <c>LuaFunctionShapeIssues</c> flags) and a compilation-end
    ///     action (<c>DuplicateName</c>, which needs every sibling member of the containing type); RS1037 requires the
    ///     <c>CompilationEnd</c> tag whenever any report of an ID comes from a compilation-end action, so it is present
    ///     here although most reports of this ID are still live.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidLuaFunction = new(
        DiagnosticIds.InvalidLuaFunction,
        "[LuaFunction] method cannot be exported by a generated thunk",
        "Lua function '{0}' {1}",
        DiagnosticCategories.Generation,
        DiagnosticSeverity.Error,
        true,
        "A method marked [LuaFunction] must be an ordinary, static, non-generic, non-async method with a valid Lua name; parameters are passed by value, none optional or params, "
        + "each of a marshalled kind (int, long, float, double, bool, nuint, ReadOnlySpan<byte>, string) except an optional leading LuaState; the return type is void or one of the same marshalled kinds. "
        + "No other [LuaFunction] of the same containing type may register the same name. When any of this is violated the generator emits no thunk for the method, and this rule names the cause.",
        HelpLinkBase + DiagnosticIds.InvalidLuaFunction + ".md",
        WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>CESDK2004. Message arguments: the method name, then the sentence fragment describing the problem.</summary>
    public static readonly DiagnosticDescriptor InvalidLuaGlobal = new(
        DiagnosticIds.InvalidLuaGlobal,
        "[LuaGlobal] method cannot receive a generated body",
        "Lua global binding '{0}' {1}",
        DiagnosticCategories.Generation,
        DiagnosticSeverity.Error,
        true,
        "A method marked [LuaGlobal] must be the defining declaration of an ordinary, static, non-generic, non-async partial method with a valid Lua name and no implementing part yet. "
        + "Parameters, in order: an optional leading LuaState, then by-value arguments of a marshalled kind, then the results (out parameters of a marshalled kind other than ReadOnlySpan<byte>, "
        + "or a copy-out pair Span<byte> destination, out int written). Any out result makes it the Try form, which must return bool; no result makes it the throwing form, "
        + "whose return type is void or a marshalled kind other than ReadOnlySpan<byte>. When any of this is violated the generator emits no body for the method, and this rule names the cause.",
        HelpLinkBase + DiagnosticIds.InvalidLuaGlobal + ".md");
}
