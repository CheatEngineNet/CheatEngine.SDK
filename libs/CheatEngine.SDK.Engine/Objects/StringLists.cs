using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Objects;

/// <summary>Creates plugin-owned instances of Cheat Engine's <c>Stringlist</c> class.</summary>
/// <remarks>
///     CE 7.7.0.10621 documents <c>createStringlist()</c> in <c>celua.txt</c>. The returned native object belongs to
///     the caller and is therefore represented by <see cref="Owned{T}" />. In contrast, globals such as
///     <c>getAutoAttachList()</c> hand out host-owned lists and must be modelled by a separate borrowed-return API.
///     Global resolution is cached per Lua attach epoch; calls fail cleanly when the global is absent, raises, or returns
///     nil/a non-host object. A detached runtime throws <see cref="InvalidOperationException" />.
/// </remarks>
public static class StringLists
{
	private static readonly LuaRef SCreateStringList = new();

	/// <summary>Creates one plugin-owned StringList.</summary>
	/// <param name="list">The new owner on success; <see langword="null" /> on failure.</param>
	/// <returns><see langword="true" /> when CE returned a non-null host object.</returns>
	[RequiresPluginEnabled]
	public static bool TryCreate([NotNullWhen(true)] out Owned<StringList>? list)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		if (!LuaGlobalFunctions.TryPush(state, SCreateStringList, "createStringlist"u8) ||
		    !state.TryCall(0, 1).IsOk ||
		    !CEObject.TryRead(state, -1, out CEObject handle))
		{
			list = null;
			return false;
		}

		list = new Owned<StringList>(StringList.FromHandle(handle));
		return true;
	}
}
