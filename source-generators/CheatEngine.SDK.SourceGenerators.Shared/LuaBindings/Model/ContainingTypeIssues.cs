using System;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

/// <summary>
///     Why a generated partial part cannot be added to the type that declares a <c>[LuaFunction]</c> or
///     <c>[LuaGlobal]</c> member. Shared by both bindings; the matching CESDK2xxx analyzer rules explain each flag.
/// </summary>
[Flags]
internal enum ContainingTypeIssues
{
	/// <summary>The generated part can be added.</summary>
	None = 0,

	/// <summary>
	///     The declaring type, or a type it is nested in, is not <see langword="partial" />: no second part can be
	///     declared.
	/// </summary>
	NotPartial = 1 << 0,

	/// <summary>
	///     The declaring type, or a type it is nested in, has type parameters: the part cannot be named without type
	///     arguments, and a per-instantiation cache would be wrong.
	/// </summary>
	Generic = 1 << 1,

	/// <summary>
	///     The declaring type, or a type it is nested in, is neither a class, a struct nor a record (interfaces, enums
	///     and delegates take no generated members).
	/// </summary>
	NotClassOrStruct = 1 << 2,

	/// <summary>
	///     The declaring type, or a type it is nested in, is <see langword="file" />-local: a part in another file cannot
	///     reach it.
	/// </summary>
	FileLocal = 1 << 3
}
