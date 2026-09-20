namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     Everything <see cref="LuaGlobalCallEmitter" /> needs to write one wrapper method that calls a Lua global: the
///     signature to reproduce and the call shape. Strings and enums only, so that a generator can keep it in its
///     pipeline (value equality) and the EngineApi generator can build it from a spec file.
/// </summary>
/// <param name="GlobalName">
///     The Lua global, already validated by <see cref="LuaNames.IsValidName" />; emitted as a
///     <c>u8</c> literal and in messages.
/// </param>
/// <param name="CacheFieldName">
///     The <c>LuaRef</c> field of the containing type that caches the resolved global (
///     <c>s_luaGlobal_readInteger</c>); declared by the file emitter, one per distinct global.
/// </param>
/// <param name="Modifiers">
///     The modifiers written before the return type, for example <c>public static partial</c> (a
///     generated body for a user declaration) or <c>public static</c> (a complete wrapper). Never empty for a partial
///     method, which needs at least <c>static partial</c>.
/// </param>
/// <param name="MethodName">The method name, keyword-escaped.</param>
/// <param name="StateParameterName">
///     When not empty: the name of a leading <c>LuaState</c> parameter that the body uses
///     instead of acquiring a state.
/// </param>
/// <param name="Arguments">The values pushed, in order, after the function.</param>
/// <param name="Form">Try or throwing.</param>
/// <param name="Results">Try form: the results, at least one. Throwing form: empty.</param>
/// <param name="ReturnKind">
///     Throwing form: the kind read from the single result, or <see langword="null" /> for a
///     <see langword="void" /> method (the call keeps no result). Try form: ignored.
/// </param>
/// <param name="ReturnIsNullable">
///     Throwing form with a <see cref="LuaValueKind.String" /> return: the declaration wrote
///     <c>string?</c>.
/// </param>
/// <param name="IsExtensionMethod">
///     Whether the first parameter is the <see langword="this" /> receiver of an extension method. Generated partial
///     implementations must repeat that modifier for the declaration to compile.
/// </param>
/// <param name="CacheFieldAccess">
///     The cache-field access expression used by the generated body, or <see langword="null" /> to use
///     <paramref name="CacheFieldName" /> directly.
/// </param>
internal sealed record LuaGlobalCallModel(
    string GlobalName,
    string CacheFieldName,
    string Modifiers,
    string MethodName,
    string StateParameterName,
    EquatableArray<LuaArgumentModel> Arguments,
    LuaCallForm Form,
    EquatableArray<LuaResultModel> Results,
    LuaValueKind? ReturnKind,
    bool ReturnIsNullable,
    bool IsExtensionMethod = false,
    string? CacheFieldAccess = null)
{
    /// <summary>Prefix of the cache field a file emitter declares for a global.</summary>
    public const string CacheFieldPrefix = "s_luaGlobal_";

    /// <summary>Number of results the protected call keeps: the result count of the Try form, 0 or 1 for the throwing form.</summary>
    public int ResultCount => Form == LuaCallForm.Try ? Results.Length : ReturnKind is null ? 0 : 1;

    /// <summary>Whether the body reads the state from <see cref="StateParameterName" /> rather than from the runtime.</summary>
    public bool TakesState => StateParameterName.Length > 0;

    /// <summary>
    ///     The expression that accesses <see cref="CacheFieldName" /> from the generated method body. Bindings qualify
    ///     this with their containing type so a parameter cannot shadow the static cache; spec-generated wrappers keep
    ///     the unqualified field name.
    /// </summary>
    public string CacheFieldReference => CacheFieldAccess ?? CacheFieldName;

    /// <summary>
    ///     The cache field name for <paramref name="globalName" />: <see cref="CacheFieldPrefix" /> + the name (a Lua
    ///     name is a C# identifier).
    /// </summary>
    public static string CacheFieldFor(string globalName)
    {
        return CacheFieldPrefix + globalName;
    }
}
