using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     A borrowed handle to Cheat Engine's <c>FoundList</c> Lua class: an object that opens a memscan result file for
///     reading. It owns nothing; <see cref="MemoryScanSessions.TryCreate" /> is the specific SDK factory that owns a
///     created list as the child of its created scanner.
/// </summary>
/// <remarks>
///     <para>
///         CE 7.7.0.10621 documents <c>FoundList</c> in <c>celua.txt</c> lines 2661-2677. Its count, address and value
///         indices are zero-based. Addresses and values are strings; <see cref="TryGetAddress" /> parses the documented
///         hexadecimal address string while <see cref="TryGetAddressText" /> preserves it verbatim.
///     </para>
///     <para>
///         This raw object wrapper does not know whether <c>initialize()</c> already ran. Prefer
///         <see cref="MemoryScanSession" />, which permits reads only after it observed a successful
///         <c>waitTillDone</c> and <c>initialize</c> sequence.
///     </para>
///     <para>
///         CE's catalog does not establish a main-thread rule for these raw reads. They deliberately have no
///         <c>MainThreadOnly</c> metadata; the stateful session has a separate conservative runtime guard because it
///         owns the objects it coordinates.
///     </para>
/// </remarks>
[LuaClass("FoundList")]
public readonly struct FoundList : ICEObject<FoundList>, IEquatable<FoundList>
{
	private readonly CEObject _handle;

	/// <summary>Wraps a borrowed <c>FoundList</c> handle without validating its native class.</summary>
	/// <param name="handle">The native handle; a null handle gives the default value.</param>
	public FoundList(CEObject handle)
	{
		_handle = handle;
	}

	/// <inheritdoc />
	public CEObject Handle => _handle;

	/// <inheritdoc />
	public static FoundList FromHandle(CEObject handle)
	{
		return new FoundList(handle);
	}

	/// <summary>Gets a value indicating whether this is the null handle.</summary>
	public bool IsNull => _handle.IsNull;

	/// <summary>Attempts to read the CE <c>Count</c> property.</summary>
	/// <param name="count">The number of results, when the method returns <see langword="true" />.</param>
	/// <returns>
	///     <see langword="false" /> when CE raised or did not return a non-negative 64-bit Lua integer. CE stores this
	///     count as <c>UInt64</c>; values outside Lua's signed 64-bit integer range are rejected rather than wrapped.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or has no host object pusher.</exception>
	public bool TryGetCount(out ulong count)
	{
		if (_handle.TryGetProperty<Int64Marshaller, long>("Count"u8, out long signedCount) && signedCount >= 0)
		{
			count = (ulong) signedCount;
			return true;
		}

		count = default;
		return false;
	}

	/// <summary>Attempts to read the exact address text at a zero-based result index.</summary>
	/// <param name="zeroBasedIndex">The CE result index, beginning at zero.</param>
	/// <param name="address">The copied address text when the method returns <see langword="true" />.</param>
	/// <returns><see langword="false" /> when CE raised or did not return a string.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or has no host object pusher.</exception>
	public bool TryGetAddressText(int zeroBasedIndex, [NotNullWhen(true)] out string? address)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
		return TryCallString("getAddress"u8, zeroBasedIndex, out address);
	}

	/// <summary>Attempts to read and parse the address at a zero-based result index.</summary>
	/// <param name="zeroBasedIndex">The CE result index, beginning at zero.</param>
	/// <param name="address">The parsed target address when the method returns <see langword="true" />.</param>
	/// <returns>
	///     <see langword="false" /> when CE raised, did not return text, or returned text that is not a hexadecimal
	///     target address.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or has no host object pusher.</exception>
	public bool TryGetAddress(int zeroBasedIndex, out Address address)
	{
		if (TryGetAddressText(zeroBasedIndex, out string? text) && Address.TryParse(text, out address))
		{
			return true;
		}

		address = default;
		return false;
	}

	/// <summary>Attempts to read the exact value text at a zero-based result index.</summary>
	/// <param name="zeroBasedIndex">The CE result index, beginning at zero.</param>
	/// <param name="value">The copied value text when the method returns <see langword="true" />.</param>
	/// <returns><see langword="false" /> when CE raised or did not return a string.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or has no host object pusher.</exception>
	public bool TryGetValueText(int zeroBasedIndex, [NotNullWhen(true)] out string? value)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
		return TryCallString("getValue"u8, zeroBasedIndex, out value);
	}

	/// <summary>Compares two borrowed handles by native pointer value.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	/// <returns><see langword="true" /> when both handles carry the same pointer.</returns>
	public static bool operator ==(FoundList left, FoundList right)
	{
		return left.Equals(right);
	}

	/// <summary>Compares two borrowed handles by native pointer value.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	/// <returns><see langword="true" /> when the pointers differ.</returns>
	public static bool operator !=(FoundList left, FoundList right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public bool Equals(FoundList other)
	{
		return _handle.Equals(other._handle);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is FoundList other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _handle.GetHashCode();
	}

	/// <summary>Formats the underlying native handle.</summary>
	/// <returns>The underlying <see cref="CEObject" /> representation.</returns>
	public override string ToString()
	{
		return _handle.ToString();
	}

	private bool TryCallString(ReadOnlySpan<byte> method, int index, [NotNullWhen(true)] out string? value)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		state.PushInteger(index);
		if (!_handle.TryCallMethod(state, method, 1, 1).IsOk || !StringMarshaller.TryRead(state, -1, out string? text))
		{
			value = null;
			return false;
		}

		value = text;
		return true;
	}
}
