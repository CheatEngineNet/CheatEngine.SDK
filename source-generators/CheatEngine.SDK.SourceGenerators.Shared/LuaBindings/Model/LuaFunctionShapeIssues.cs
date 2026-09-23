using System;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

/// <summary>
///     Why a <c>[LuaFunction]</c> method cannot be exported by a generated thunk. The generator only uses the flags to
///     stay silent; the CESDK2xxx analyzer rules link the same shape file and explain each of them.
/// </summary>
/// <remarks>
///     Containing-type problems are a separate enum, <see cref="ContainingTypeIssues" />, shared with
///     <c>[LuaGlobal]</c>.
/// </remarks>
[Flags]
internal enum LuaFunctionShapeIssues
{
	/// <summary>The method can be exported.</summary>
	None = 0,

	/// <summary>
	///     The method is not an ordinary method (an accessor, an operator, a conversion, a local function, an explicit
	///     interface implementation).
	/// </summary>
	NotOrdinaryMethod = 1 << 0,

	/// <summary>The method is an instance method: a thunk wrapping a static method has no receiver to call it on.</summary>
	NotStatic = 1 << 1,

	/// <summary>The method has type parameters.</summary>
	Generic = 1 << 2,

	/// <summary>
	///     The method is <see langword="async" />. An <see langword="async" /> <see langword="void" /> method returns
	///     <see langword="void" /> like any
	///     other, so without this flag it would pass every other check: the thunk would call it and return before its
	///     continuation runs, and an exception the continuation throws would not be the thunk's <see langword="catch" /> to
	///     catch
	///     (no managed exception may cross the native boundary uncaught).
	/// </summary>
	Async = 1 << 11,

	/// <summary>The attribute's name argument is missing, not a string, or not a Lua identifier (see <c>LuaNames</c>).</summary>
	InvalidName = 1 << 3,

	/// <summary>
	///     A parameter has a type no marshaller reads: only <see langword="int" />, <see langword="long" />,
	///     <see langword="float" />, <see langword="double" />,
	///     <see langword="bool" />, <see langword="nuint" />, <c>ReadOnlySpan&lt;byte&gt;</c> and <see langword="string" />
	///     are accepted unless an explicit annotation names a type that implements the matching
	///     <c>ILuaMarshaller&lt;T&gt;</c> contract.
	/// </summary>
	UnsupportedParameterType = 1 << 4,

	/// <summary>
	///     A parameter is <see langword="ref" />, <see langword="in" />, <see langword="out" /> or <see langword="ref" />
	///     <see langword="readonly" />: a Lua argument is a value.
	/// </summary>
	ByRefParameter = 1 << 5,

	/// <summary>A parameter is <see langword="params" />: variadic exports are not supported.</summary>
	ParamsParameter = 1 << 6,

	/// <summary>A parameter has a default value: optional Lua arguments are not supported (the thunk checks the exact count).</summary>
	OptionalParameter = 1 << 7,

	/// <summary>A <c>LuaState</c> parameter is not the first parameter: the state is passed first or not at all.</summary>
	StateParameterNotFirst = 1 << 8,

	/// <summary>
	///     The return type is neither <see langword="void" />, a built-in marshalled type nor a type with a valid
	///     explicit <c>ILuaMarshaller&lt;T&gt;</c> annotation.
	/// </summary>
	UnsupportedReturnType = 1 << 9,

	/// <summary>
	///     Another <c>[LuaFunction]</c> of the same containing type carries the same name: one registration table
	///     cannot bind a name twice. Not decided per member by this type's own inspector (<c>LuaFunctionShape.Inspect</c>
	///     is given one method at a time): <c>LuaFunctionTables.Group</c> drops every member of a duplicated name from
	///     its table, and on the analyzer side <c>LuaBindingAnalyzer</c>'s compilation-end pass
	///     (<c>LuaFunctionDuplicateState</c>) sets this flag once every sibling member has been seen.
	/// </summary>
	DuplicateName = 1 << 10,

	/// <summary>
	///     A required parameter follows a <c>LuaOptional&lt;T&gt;</c> parameter: optional Lua arguments form a trailing
	///     run, and an absent position reads as omitted (CESDK2010).
	/// </summary>
	OptionalArgumentNotTrailing = 1 << 12,

	/// <summary>
	///     A parameter or return type has the metadata name of <c>LuaOptional`1</c> but is not the
	///     <c>CheatEngine.SDK.Lua</c> type (CESDK2012).
	/// </summary>
	LookAlikeContractType = 1 << 13,

	/// <summary>
	///     <c>LuaOptional&lt;T&gt;</c> is used where it is not supported: as the return type, with a <c>T</c> that is
	///     <c>string?</c>, custom-marshalled, unmarshalled or another <c>LuaOptional</c>, or with an explicit
	///     <c>[LuaMarshaller]</c> (CESDK2013).
	/// </summary>
	OptionalNotSupportedHere = 1 << 14
}
