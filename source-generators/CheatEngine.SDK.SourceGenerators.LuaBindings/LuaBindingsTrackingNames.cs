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

    /// <summary>The final <c>LuaGlobalTableModel</c>: the only input of the globals source output.</summary>
    public const string LuaGlobalOutput = Prefix + "LuaGlobalOutput";

    /// <summary><c>ForAttributeWithMetadataName</c> transform: one <c>LuaClassModel</c> per annotated struct.</summary>
    public const string LuaClass = Prefix + "LuaClass";

    /// <summary><c>Collect</c>: all <c>[LuaClass]</c> models.</summary>
    public const string CollectedLuaClasses = Prefix + "CollectedLuaClasses";

    /// <summary>Filtered and ordered borrowed-handle models.</summary>
    public const string LuaClasses = Prefix + "LuaClasses";

    /// <summary>The input of the class-handle source output.</summary>
    public const string LuaClassOutput = Prefix + "LuaClassOutput";

    /// <summary><c>ForAttributeWithMetadataName</c> transform: one <c>LuaObjectMethodModel</c> per method.</summary>
    public const string LuaMethod = Prefix + "LuaMethod";

    /// <summary><c>Collect</c>: all <c>[LuaMethod]</c> models.</summary>
    public const string CollectedLuaMethods = Prefix + "CollectedLuaMethods";

    /// <summary><c>ForAttributeWithMetadataName</c> transform: one <c>LuaObjectPropertyModel</c> per property.</summary>
    public const string LuaProperty = Prefix + "LuaProperty";

    /// <summary><c>Collect</c>: all <c>[LuaProperty]</c> models.</summary>
    public const string CollectedLuaProperties = Prefix + "CollectedLuaProperties";

    /// <summary>The paired object-member collections.</summary>
    public const string LuaObjectMembersAndProperties = Prefix + "LuaObjectMembersAndProperties";

    /// <summary>Object members grouped by generated borrowed-handle type.</summary>
    public const string LuaObjectMembersTables = Prefix + "LuaObjectMembersTables";

    /// <summary>The input of the object-member source output.</summary>
    public const string LuaObjectMembersOutput = Prefix + "LuaObjectMembersOutput";

    private const string Prefix = TrackingNames.Prefix + "LuaBindings.";

    /// <summary>All of the above, for tests that must not miss a step.</summary>
    public static readonly ImmutableArray<string> All =
    [
        Facts,
        LuaFunction, CollectedLuaFunctions, LuaFunctionTables, LuaFunctionTable, LuaFunctionTableAndFacts,
        LuaFunctionTableAllowed, LuaFunctionOutput,
        LuaGlobal, CollectedLuaGlobals, LuaGlobalTables, LuaGlobalTable, LuaGlobalOutput,
        LuaClass, CollectedLuaClasses, LuaClasses, LuaClassOutput,
        LuaMethod, CollectedLuaMethods, LuaProperty, CollectedLuaProperties, LuaObjectMembersAndProperties,
        LuaObjectMembersTables, LuaObjectMembersOutput
    ];
}
