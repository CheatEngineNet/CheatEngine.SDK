using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     Reads Cheat Engine's <c>SymbolList</c> "Symbol table" (<c>modulename</c>, <c>searchkey</c>, <c>address</c>,
///     <c>symbolsize</c>) into a <see cref="SymbolInfo" /> without metamethods and without converting values in place.
/// </summary>
internal static class SymbolTableReader
{
	/// <summary>
	///     Reads the table at <paramref name="index" />; <see langword="false" /> when it is not a table or a field has the
	///     wrong type. The stack is restored.
	/// </summary>
	internal static bool TryRead(LuaState state, int index, out SymbolInfo symbol)
	{
		symbol = default;
		int table = state.AbsoluteIndex(index);
		if (!state.IsTable(table))
		{
			return false;
		}

		using LuaFrame frame = new(state);
		if (!TryReadString(state, table, "modulename"u8, out string? moduleName) ||
		    !TryReadString(state, table, "searchkey"u8, out string? searchKey))
		{
			return false;
		}

		_ = PushField(state, table, "address"u8);
		if (!Address.TryRead(state, -1, out Address address))
		{
			return false;
		}

		_ = PushField(state, table, "symbolsize"u8);
		if (!state.IsInteger(-1) || !state.TryReadInteger(-1, out long size) || size < 0)
		{
			return false;
		}

		symbol = new SymbolInfo(moduleName, searchKey, address, new MemorySize((ulong) size));
		return true;
	}

	private static bool TryReadString(LuaState state, int table, ReadOnlySpan<byte> field,
		[NotNullWhen(true)] out string? value)
	{
		_ = PushField(state, table, field);
		return state.TryReadString(-1, out value);
	}

	private static LuaType PushField(LuaState state, int table, ReadOnlySpan<byte> field)
	{
		state.PushString(field);
		return state.RawGet(table);
	}
}
