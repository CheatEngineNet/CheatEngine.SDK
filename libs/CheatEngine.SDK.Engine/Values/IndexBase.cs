using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CheatEngine.SDK.Engine.Values;

/// <summary>
///     The two index bases a plugin meets, and the conversions between them. Cheat Engine's objects count from zero
///     (<c>FoundList.getAddress(0)</c>, <c>AddressList.getMemoryRecord(0)</c>, <c>StringList[0]</c>, structure
///     elements), exactly like C# collections; the Lua tables that Cheat Engine's functions <i>return</i>
///     (<c>enumMemoryRegions()</c>, byte tables, <c>MemScan.Results</c>) are Lua sequences and count from one.
/// </summary>
/// <remarks>
///     <para>
///         <b>The rule this assembly follows.</b> Every public API in <c>CheatEngine.SDK.Engine</c> takes and returns
///         zero-based indices, whatever the Lua side counts from: an index into a Cheat Engine object is passed through
///         unchanged, an
///         index into a returned Lua sequence is converted here, in one place, by the members of this class and the
///         <see cref="LuaSequence" /> extensions built on them. Code outside this class never writes <c>+ 1</c> or
///         <c>- 1</c> to change base.
///     </para>
///     <para>
///         The conversions are checked: a negative zero-based index or a Lua key below one is a programmer error and
///         throws
///         <see cref="ArgumentOutOfRangeException" />; the <c>Try</c> form exists for keys read back from Lua, which may
///         be
///         anything.
///     </para>
/// </remarks>
public static class IndexBase
{
	/// <summary>The first index of a Cheat Engine object, and of every index this assembly exposes.</summary>
	public const int FirstObjectIndex = 0;

	/// <summary>The first key of a Lua sequence.</summary>
	public const long FirstLuaKey = 1;

	/// <summary>Converts a zero-based index into the key of the same element in a Lua sequence.</summary>
	/// <param name="zeroBasedIndex">The index as C# and Cheat Engine objects count it.</param>
	/// <returns>The one-based Lua key.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static long ToLuaKey(int zeroBasedIndex)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
		return zeroBasedIndex + FirstLuaKey;
	}

	/// <summary>Converts the key of a Lua sequence element into a zero-based index.</summary>
	/// <param name="luaKey">The one-based Lua key.</param>
	/// <returns>The zero-based index.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="luaKey" /> is below one, or the index would not fit an
	///     <see cref="int" />.
	/// </exception>
	public static int FromLuaKey(long luaKey)
	{
		if (!TryFromLuaKey(luaKey, out int zeroBasedIndex))
		{
			ThrowNotASequenceKey(luaKey);
		}

		return zeroBasedIndex;
	}

	/// <summary>Converts the key of a Lua sequence element into a zero-based index, for keys read back from Lua.</summary>
	/// <param name="luaKey">The key, as any Lua integer.</param>
	/// <param name="zeroBasedIndex">The zero-based index; 0 on failure.</param>
	/// <returns><see langword="false" /> when the key is below one or the index would not fit an <see cref="int" />.</returns>
	public static bool TryFromLuaKey(long luaKey, out int zeroBasedIndex)
	{
		if (luaKey < FirstLuaKey || luaKey > int.MaxValue + FirstLuaKey)
		{
			zeroBasedIndex = 0;
			return false;
		}

		zeroBasedIndex = (int) (luaKey - FirstLuaKey);
		return true;
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowNotASequenceKey(long luaKey)
	{
		throw new ArgumentOutOfRangeException(nameof(luaKey), luaKey,
			"A Lua sequence key is at least 1 and at most int.MaxValue + 1.");
	}
}
