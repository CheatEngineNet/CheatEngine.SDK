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
/// <param name="ReturnMarshaller">
///     An explicit static marshaller for the throwing-form return value, or <see langword="null" /> when
///     <paramref name="ReturnKind" /> selects a built-in marshaller.
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
	string? CacheFieldAccess = null,
	LuaCustomMarshallerModel? ReturnMarshaller = null)
{
	/// <summary>Prefix of the cache field a file emitter declares for a global.</summary>
	public const string CacheFieldPrefix = "s_luaGlobal_";

	/// <summary>Initializes a call model with the pre-custom-marshaller binary shape.</summary>
	public LuaGlobalCallModel(string globalName, string cacheFieldName, string modifiers, string methodName,
		string stateParameterName, EquatableArray<LuaArgumentModel> arguments, LuaCallForm form,
		EquatableArray<LuaResultModel> results, LuaValueKind? returnKind, bool returnIsNullable,
		bool isExtensionMethod, string? cacheFieldAccess)
		: this(globalName, cacheFieldName, modifiers, methodName, stateParameterName, arguments, form, results,
			returnKind, returnIsNullable, isExtensionMethod, cacheFieldAccess, null)
	{
	}

	/// <summary>
	///     Number of results the protected call keeps: the result count of either non-throwing form, 0 or 1 for the
	///     throwing form.
	/// </summary>
	public int ResultCount => IsTryLike ? Results.Length : ReturnKind is null && ReturnMarshaller is null ? 0 : 1;

	/// <summary>
	///     Whether an argument is a <c>LuaOptional&lt;T&gt;</c>: the body then computes the pushed argument count before
	///     acquiring the state and calls with it instead of a constant.
	/// </summary>
	public bool HasOptionalArguments
	{
		get
		{
			foreach (LuaArgumentModel argument in Arguments)
			{
				if (argument.IsOptional)
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>
	///     Whether a result is optional or variadic: the body then calls with <c>LUA_MULTRET</c>, reads the factual result
	///     count and addresses results by absolute index.
	/// </summary>
	public bool HasDynamicResults
	{
		get
		{
			foreach (LuaResultModel result in Results)
			{
				if (result.IsDynamic)
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>The number of leading results that Lua must return: the value and copy-out results.</summary>
	public int RequiredResultCount
	{
		get
		{
			int count = 0;
			foreach (LuaResultModel result in Results)
			{
				if (!result.IsDynamic)
				{
					count++;
				}
			}

			return count;
		}
	}

	/// <summary>Gets whether this shape returns its Lua values through <see langword="out" /> parameters.</summary>
	public bool IsTryLike => Form is LuaCallForm.Try or LuaCallForm.Outcome;

	/// <summary>Gets whether this is the opt-in detailed non-throwing form.</summary>
	public bool IsOutcome => Form == LuaCallForm.Outcome;

	/// <summary>Whether the throwing form returns one Lua value.</summary>
	public bool HasReturn => ReturnKind is not null || ReturnMarshaller is not null;

	/// <summary>The concrete static marshaller for the throwing-form return value.</summary>
	public string ReturnMarshallerTypeName => ReturnMarshaller?.MarshallerTypeName ??
	                                          LuaValueKinds.MarshallerTypeName(ReturnKind!.Value);

	/// <summary>The C# type spelling for the generated return and result local.</summary>
	public string ReturnTypeName => ReturnMarshaller?.ValueTypeName ??
	                                LuaValueKinds.TypeName(ReturnKind!.Value, ReturnIsNullable);

	/// <summary>The Lua-facing expected type for a throwing-form result failure.</summary>
	public string ExpectedReturnTypeName => ReturnMarshaller?.ExpectedTypeName ??
	                                        LuaValueKinds.ExpectedResult(ReturnKind!.Value);

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
