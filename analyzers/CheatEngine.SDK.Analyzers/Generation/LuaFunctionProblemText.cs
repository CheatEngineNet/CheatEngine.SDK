using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

namespace CheatEngine.SDK.Analyzers.Generation;

/// <summary>
///     The sentence fragment that completes "Lua function 'X' ..." in the CESDK2003 message, one per
///     <see cref="LuaFunctionShapeIssues" /> flag (from <c>CheatEngine.SDK.SourceGenerators.Shared</c>).
///     <see cref="LuaFunctionShapeIssues.DuplicateName" /> is a group rule (two or more <c>[LuaFunction]</c> members
///     of the same type sharing a name): <see cref="LuaBindingAnalyzer" />'s per-symbol pass never sets it (it inspects
///     one method at a time), so it is reported by a separate compilation-end pass (
///     <see cref="LuaFunctionDuplicateState" />)
///     that shares this same text.
/// </summary>
internal static class LuaFunctionProblemText
{
	/// <summary>The flags in the order they are reported for one method.</summary>
	public static readonly ImmutableArray<LuaFunctionShapeIssues> ReportOrder =
	[
		LuaFunctionShapeIssues.NotOrdinaryMethod,
		LuaFunctionShapeIssues.NotStatic,
		LuaFunctionShapeIssues.Generic,
		LuaFunctionShapeIssues.Async,
		LuaFunctionShapeIssues.InvalidName,
		LuaFunctionShapeIssues.ByRefParameter,
		LuaFunctionShapeIssues.ParamsParameter,
		LuaFunctionShapeIssues.OptionalParameter,
		LuaFunctionShapeIssues.StateParameterNotFirst,
		LuaFunctionShapeIssues.UnsupportedParameterType,
		LuaFunctionShapeIssues.UnsupportedReturnType,
		LuaFunctionShapeIssues.DuplicateName
	];

	/// <summary>Returns the message fragment of a single flag.</summary>
	public static string Describe(LuaFunctionShapeIssues problem)
	{
		return problem switch
		{
			LuaFunctionShapeIssues.NotOrdinaryMethod =>
				"must be an ordinary method: not an accessor, operator, conversion, local function or explicit interface implementation",
			LuaFunctionShapeIssues.NotStatic => "must be static: the generated thunk has no receiver to call it on",
			LuaFunctionShapeIssues.Generic => "must not be generic",
			LuaFunctionShapeIssues.Async =>
				"must not be async: the thunk calls it synchronously and could not catch what its continuation throws",
			LuaFunctionShapeIssues.InvalidName =>
				"must be given a Lua identifier in [LuaFunction] that is not a reserved word (Lua 5.3 manual, section 3.1)",
			LuaFunctionShapeIssues.ByRefParameter =>
				"must take its parameters by value: 'ref', 'in', 'out' and 'ref readonly' are not supported, a Lua argument is a value",
			LuaFunctionShapeIssues.ParamsParameter =>
				"must not have a 'params' parameter: variadic exports are not supported",
			LuaFunctionShapeIssues.OptionalParameter =>
				"must not have a parameter with a default value: the thunk checks the exact argument count",
			LuaFunctionShapeIssues.StateParameterNotFirst =>
				"must take its 'CheatEngine.SDK.Lua.State.LuaState' parameter, if any, first: the state is passed first or not at all",
			LuaFunctionShapeIssues.UnsupportedParameterType =>
				"must use only parameter types a marshaller reads: int, long, float, double, bool, nuint, ReadOnlySpan<byte> or string",
			LuaFunctionShapeIssues.UnsupportedReturnType =>
				"must return void or a type a marshaller pushes: int, long, float, double, bool, nuint, ReadOnlySpan<byte> or string",
			LuaFunctionShapeIssues.DuplicateName =>
				"must not share its Lua name with another [LuaFunction] of the same containing type: one registration table cannot bind a name twice",
			_ => "cannot be exported by a generated thunk"
		};
	}
}
