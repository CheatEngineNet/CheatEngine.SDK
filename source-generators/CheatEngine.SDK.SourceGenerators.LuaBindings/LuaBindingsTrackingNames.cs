using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings;

/// <summary>
///     Tracking names of every step of the two pipelines, in pipeline order. The cacheability tests look the steps up
///     by these constants and fail on a CheatEngine.SDK-named step missing from <see cref="All" />.
/// </summary>
internal static class LuaBindingsTrackingNames
{
    /// <summary><c>CompilationProvider.Select</c>: the compilation facts (unsafe allowed?), reduced to a value.</summary>
    public const string Facts = Prefix + "Facts";

    /// <summary><c>ForAttributeWithMetadataName</c> transform: one <c>LuaFunctionModel</c> per attributed method.</summary>
    public const string LuaFunction = Prefix + "LuaFunction";

    /// <summary><c>Collect</c>: all <c>[LuaFunction]</c> models as an <c>ImmutableArray</c>.</summary>
    public const string CollectedLuaFunctions = Prefix + "CollectedLuaFunctions";

    /// <summary>The models grouped by containing type into an <c>EquatableArray</c> of tables.</summary>
    public const string LuaFunctionTables = Prefix + "LuaFunctionTables";

    /// <summary><c>SelectMany</c>: one <c>LuaFunctionTableModel</c> per containing type.</summary>
    public const string LuaFunctionTable = Prefix + "LuaFunctionTable";

    /// <summary><c>Combine</c> of <see cref="LuaFunctionTable" /> and <see cref="Facts" />.</summary>
    public const string LuaFunctionTableAndFacts = Prefix + "LuaFunctionTableAndFacts";

    /// <summary><c>Where</c>: the pairs whose compilation allows unsafe code.</summary>
    public const string LuaFunctionTableAllowed = Prefix + "LuaFunctionTableAllowed";

    /// <summary>The final <c>LuaFunctionTableModel</c>: the only input of the functions source output.</summary>
    public const string LuaFunctionOutput = Prefix + "LuaFunctionOutput";

    /// <summary><c>ForAttributeWithMetadataName</c> transform: one <c>LuaGlobalModel</c> per attributed method.</summary>
    public const string LuaGlobal = Prefix + "LuaGlobal";

    /// <summary><c>Collect</c>: all <c>[LuaGlobal]</c> models as an <c>ImmutableArray</c>.</summary>
    public const string CollectedLuaGlobals = Prefix + "CollectedLuaGlobals";

    /// <summary>The models grouped by containing type into an <c>EquatableArray</c> of tables.</summary>
    public const string LuaGlobalTables = Prefix + "LuaGlobalTables";

    /// <summary><c>SelectMany</c>: one <c>LuaGlobalTableModel</c> per containing type.</summary>
    public const string LuaGlobalTable = Prefix + "LuaGlobalTable";

    /// <summary><c>Combine</c> of <see cref="LuaGlobalTable" /> and <see cref="Facts" />.</summary>
    public const string LuaGlobalTableAndFacts = Prefix + "LuaGlobalTableAndFacts";

    /// <summary><c>Where</c>: the pairs whose compilation allows unsafe code.</summary>
    public const string LuaGlobalTableAllowed = Prefix + "LuaGlobalTableAllowed";

    /// <summary>The final <c>LuaGlobalTableModel</c>: the only input of the globals source output.</summary>
    public const string LuaGlobalOutput = Prefix + "LuaGlobalOutput";

    private const string Prefix = TrackingNames.Prefix + "LuaBindings.";

    /// <summary>All of the above, for tests that must not miss a step.</summary>
    public static readonly ImmutableArray<string> All =
    [
        Facts,
        LuaFunction, CollectedLuaFunctions, LuaFunctionTables, LuaFunctionTable, LuaFunctionTableAndFacts,
        LuaFunctionTableAllowed, LuaFunctionOutput,
        LuaGlobal, CollectedLuaGlobals, LuaGlobalTables, LuaGlobalTable, LuaGlobalTableAndFacts, LuaGlobalTableAllowed,
        LuaGlobalOutput
    ];
}
