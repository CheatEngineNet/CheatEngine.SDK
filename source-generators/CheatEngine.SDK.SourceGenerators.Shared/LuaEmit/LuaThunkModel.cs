namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     Everything <see cref="LuaThunkEmitter" /> needs to write one <c>lua_CFunction</c> thunk around a managed static
///     method, and <see cref="LuaRegistrationEmitter" /> needs to register it. Strings and enums only.
/// </summary>
/// <param name="LuaName">
///     The global the thunk is registered under, already validated by
///     <see cref="LuaNames.IsValidName" />.
/// </param>
/// <param name="ThunkMethodName">
///     The name of the generated thunk (<c>__LuaThunk_add</c>); unique per containing type
///     because Lua names are.
/// </param>
/// <param name="TargetMethod">The method the thunk calls, fully qualified (<c>global::Demo.Math.Add</c>).</param>
/// <param name="PassesState">Whether the target's first parameter is a <c>LuaState</c> that receives the thunk's state.</param>
/// <param name="Arguments">The Lua arguments, read at stack indices 1..n and passed after the state.</param>
/// <param name="ReturnKind">
///     The kind pushed as the single result, or <see langword="null" /> for a <see langword="void" /> target (no
///     result).
/// </param>
/// <param name="DeclaredDiagnosticIds">
///     Diagnostic IDs the target method (or a type it is nested in) declares for its own use through
///     <c>[Experimental("ID")]</c> or <c>[Obsolete(DiagnosticId = "ID")]</c>, comma-joined; empty when none. The file
///     that calls the target always disables <c>CS0612, CS0618</c> (a plain <c>[Obsolete]</c>) and additionally these
///     IDs when not empty, the same way the generated entry point protects the construction of an <c>[Obsolete]</c>
///     plugin class.
/// </param>
/// <param name="ReturnMarshaller">
///     An explicit static marshaller for the return value, or <see langword="null" /> when
///     <paramref name="ReturnKind" /> selects a built-in marshaller.
/// </param>
internal sealed record LuaThunkModel(
    string LuaName,
    string ThunkMethodName,
    string TargetMethod,
    bool PassesState,
    EquatableArray<LuaArgumentModel> Arguments,
    LuaValueKind? ReturnKind,
    string DeclaredDiagnosticIds = "",
    LuaCustomMarshallerModel? ReturnMarshaller = null)
{
    /// <summary>Initializes a thunk model with the pre-custom-marshaller binary shape.</summary>
    public LuaThunkModel(string luaName, string thunkMethodName, string targetMethod, bool passesState,
        EquatableArray<LuaArgumentModel> arguments, LuaValueKind? returnKind, string declaredDiagnosticIds)
        : this(luaName, thunkMethodName, targetMethod, passesState, arguments, returnKind, declaredDiagnosticIds, null)
    {
    }

    /// <summary>Whether the target returns one Lua value.</summary>
    public bool HasReturn => ReturnKind is not null || ReturnMarshaller is not null;

    /// <summary>The concrete static marshaller for the return value.</summary>
    public string ReturnMarshallerTypeName => ReturnMarshaller?.MarshallerTypeName ??
                                              LuaValueKinds.MarshallerTypeName(ReturnKind!.Value);

    /// <summary>The C# type spelling for the generated result local.</summary>
    public string ReturnTypeName => ReturnMarshaller?.ValueTypeName ?? LuaValueKinds.TypeName(ReturnKind!.Value, true);
    /// <summary>Prefix of every generated thunk name.</summary>
    public const string ThunkPrefix = "__LuaThunk_";

    /// <summary>
    ///     The thunk name for <paramref name="luaName" />: <see cref="ThunkPrefix" /> + the name (a Lua name is a C#
    ///     identifier).
    /// </summary>
    public static string ThunkNameFor(string luaName)
    {
        return ThunkPrefix + luaName;
    }
}
