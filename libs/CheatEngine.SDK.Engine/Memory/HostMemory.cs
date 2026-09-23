using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Lua.References;

namespace CheatEngine.SDK.Engine.Memory;

/// <summary>Typed access to Cheat Engine's own process through CE 7.7's documented <c>*Local</c> memory globals.</summary>
/// <remarks>
///     <para>
///         <b>Provenance.</b> CE 7.7.0.10621 <c>celua.txt</c> documents <c>readBytesLocal</c>, scalar
///         <c>read*Local</c>/<c>write*Local</c> functions, pointers, floating point and strings. It documents no
///         <c>readByteLocal</c> or <c>writeByteLocal</c>; this class implements its 8-bit operations only through the
///         documented local byte-table functions. The Lua names are not exposed as part of this .NET API.
///     </para>
///     <para>
///         Operations require an enabled plugin and acquire the current thread's state once. The CE catalog does not
///         specify main-thread affinity for these globals, so no unsupported affinity claim is made here. This class has
///         no object ownership: all returned values and caller-provided buffers remain managed values.
///     </para>
///     <para>
///         <b>Host, not target.</b> These methods address Cheat Engine's own process. The attached target process is
///         <see cref="TargetMemory" /> with <c>CheatEngine.SDK.Engine.Values.Address</c>; neither address type converts to
///         the other.
///     </para>
///     <para>
///         <b>Text and bytes.</b> <c>maximumLength</c> is passed unchanged to CE's local string primitive. Its unit for a
///         wide read (characters or bytes), and the terminator CE reads or writes for a wide string, are not qualified on
///         the pinned CE profile (qualification Q20, level C3). The byte forms keep the exact bytes, embedded NULs and
///         invalid UTF-8 included; <see cref="TryReadString" /> decodes invalid UTF-8 to U+FFFD. The
///         <see cref="TryReadBytes(HostAddress, Span{byte}, out int, out MemoryAccessFailure)" /> overload reports a
///         confirmed contiguous prefix with <see cref="MemoryAccessFailure.PartialRead" />.
///     </para>
/// </remarks>
[RequiresPluginEnabled]
public static class HostMemory
{
	private static readonly LuaRef SReadBytes = new();
	private static readonly LuaRef SReadSmallInteger = new();
	private static readonly LuaRef SReadInteger = new();
	private static readonly LuaRef SReadQword = new();
	private static readonly LuaRef SReadPointer = new();
	private static readonly LuaRef SReadFloat = new();
	private static readonly LuaRef SReadDouble = new();
	private static readonly LuaRef SReadString = new();
	private static readonly LuaRef SWriteBytes = new();
	private static readonly LuaRef SWriteSmallInteger = new();
	private static readonly LuaRef SWriteInteger = new();
	private static readonly LuaRef SWriteQword = new();
	private static readonly LuaRef SWritePointer = new();
	private static readonly LuaRef SWriteFloat = new();
	private static readonly LuaRef SWriteDouble = new();
	private static readonly LuaRef SWriteString = new();

	/// <summary>Reads an unsigned 8-bit value through CE's documented local byte-table operation.</summary>
	public static bool TryReadUInt8(HostAddress address, out byte value, out MemoryAccessFailure failure)
	{
		Span<byte> bytes = stackalloc byte[1];
		if (!MemoryLua.TryReadBytes(SReadBytes, "readBytesLocal"u8, address.ToInt64(), bytes, out _, out failure))
		{
			value = default;
			return false;
		}

		value = bytes[0];
		return true;
	}

	/// <summary>Reads a signed 8-bit value through CE's documented local byte-table operation.</summary>
	public static bool TryReadInt8(HostAddress address, out sbyte value, out MemoryAccessFailure failure)
	{
		if (!TryReadUInt8(address, out byte raw, out failure))
		{
			value = default;
			return false;
		}

		value = unchecked((sbyte) raw);
		return true;
	}

	/// <summary>Reads an unsigned 16-bit value from Cheat Engine's process.</summary>
	public static bool TryReadUInt16(HostAddress address, out ushort value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadSmallInteger, "readSmallIntegerLocal"u8, address.ToInt64(), false,
			    true,
			    out long raw, out failure) || raw < 0 || raw > ushort.MaxValue)
		{
			value = default;
			if (failure == MemoryAccessFailure.None)
			{
				failure = MemoryAccessFailure.InvalidResult;
			}

			return false;
		}

		value = (ushort) raw;
		return true;
	}

	/// <summary>Reads a signed 16-bit value from Cheat Engine's process.</summary>
	public static bool TryReadInt16(HostAddress address, out short value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadSmallInteger, "readSmallIntegerLocal"u8, address.ToInt64(), true,
			    true,
			    out long raw, out failure) || raw < short.MinValue || raw > short.MaxValue)
		{
			value = default;
			if (failure == MemoryAccessFailure.None)
			{
				failure = MemoryAccessFailure.InvalidResult;
			}

			return false;
		}

		value = (short) raw;
		return true;
	}

	/// <summary>Reads an unsigned 32-bit value from Cheat Engine's process.</summary>
	public static bool TryReadUInt32(HostAddress address, out uint value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadInteger, "readIntegerLocal"u8, address.ToInt64(), false,
			    true, out long raw,
			    out failure) || raw < 0 || (ulong) raw > uint.MaxValue)
		{
			value = default;
			if (failure == MemoryAccessFailure.None)
			{
				failure = MemoryAccessFailure.InvalidResult;
			}

			return false;
		}

		value = (uint) raw;
		return true;
	}

	/// <summary>Reads a signed 32-bit value from Cheat Engine's process.</summary>
	public static bool TryReadInt32(HostAddress address, out int value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadInteger, "readIntegerLocal"u8, address.ToInt64(), true,
			    true, out long raw,
			    out failure) || raw < int.MinValue || raw > int.MaxValue)
		{
			value = default;
			if (failure == MemoryAccessFailure.None)
			{
				failure = MemoryAccessFailure.InvalidResult;
			}

			return false;
		}

		value = (int) raw;
		return true;
	}

	/// <summary>Reads an unsigned 64-bit value from Cheat Engine's process without changing its bits.</summary>
	public static bool TryReadUInt64(HostAddress address, out ulong value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadQword, "readQwordLocal"u8, address.ToInt64(), false,
			    false, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		value = unchecked((ulong) raw);
		return true;
	}

	/// <summary>Reads a signed 64-bit value from Cheat Engine's process.</summary>
	public static bool TryReadInt64(HostAddress address, out long value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadInteger(SReadQword, "readQwordLocal"u8, address.ToInt64(), false,
			false, out value,
			out failure);
	}

	/// <summary>Reads a host-width pointer from Cheat Engine's process.</summary>
	public static bool TryReadPointer(HostAddress address, out HostAddress value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadPointer, "readPointerLocal"u8, address.ToInt64(), false,
			    false, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		value = HostAddress.FromInt64(raw);
		return true;
	}

	/// <summary>Reads a single-precision floating-point value from Cheat Engine's process.</summary>
	public static bool TryReadSingle(HostAddress address, out float value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadNumber(SReadFloat, "readFloatLocal"u8, address.ToInt64(), out double raw, out failure))
		{
			value = default;
			return false;
		}

		value = (float) raw;
		return true;
	}

	/// <summary>Reads a double-precision floating-point value from Cheat Engine's process.</summary>
	public static bool TryReadDouble(HostAddress address, out double value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadNumber(SReadDouble, "readDoubleLocal"u8, address.ToInt64(), out value, out failure);
	}

	/// <summary>Writes an unsigned 8-bit value through CE's ordered local byte-table operation.</summary>
	public static bool TryWriteUInt8(HostAddress address, byte value, out MemoryAccessFailure failure)
	{
		Span<byte> bytes = stackalloc byte[1];
		bytes[0] = value;
		return MemoryLua.TryWriteBytes(SWriteBytes, "writeBytesLocal"u8, address.ToInt64(), bytes, out _, out failure);
	}

	/// <summary>Writes a signed 8-bit value through CE's ordered local byte-table operation.</summary>
	public static bool TryWriteInt8(HostAddress address, sbyte value, out MemoryAccessFailure failure)
	{
		return TryWriteUInt8(address, unchecked((byte) value), out failure);
	}

	/// <summary>Writes an unsigned 16-bit value to Cheat Engine's process.</summary>
	public static bool TryWriteUInt16(HostAddress address, ushort value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteSmallInteger, "writeSmallIntegerLocal"u8, address.ToInt64(), value,
			out failure);
	}

	/// <summary>Writes a signed 16-bit value to Cheat Engine's process.</summary>
	public static bool TryWriteInt16(HostAddress address, short value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteSmallInteger, "writeSmallIntegerLocal"u8, address.ToInt64(), value,
			out failure);
	}

	/// <summary>Writes an unsigned 32-bit value to Cheat Engine's process.</summary>
	public static bool TryWriteUInt32(HostAddress address, uint value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteInteger, "writeIntegerLocal"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a signed 32-bit value to Cheat Engine's process.</summary>
	public static bool TryWriteInt32(HostAddress address, int value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteInteger, "writeIntegerLocal"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes an unsigned 64-bit value to Cheat Engine's process without changing its bits.</summary>
	public static bool TryWriteUInt64(HostAddress address, ulong value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteQword, "writeQwordLocal"u8, address.ToInt64(), unchecked((long) value),
			out failure);
	}

	/// <summary>Writes a signed 64-bit value to Cheat Engine's process.</summary>
	public static bool TryWriteInt64(HostAddress address, long value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteQword, "writeQwordLocal"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a host-width pointer to Cheat Engine's process.</summary>
	public static bool TryWritePointer(HostAddress address, HostAddress value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWritePointer, "writePointerLocal"u8, address.ToInt64(), value.ToInt64(),
			out failure);
	}

	/// <summary>Writes a single-precision floating-point value to Cheat Engine's process.</summary>
	public static bool TryWriteSingle(HostAddress address, float value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteNumber(SWriteFloat, "writeFloatLocal"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a double-precision floating-point value to Cheat Engine's process.</summary>
	public static bool TryWriteDouble(HostAddress address, double value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteNumber(SWriteDouble, "writeDoubleLocal"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Reads exactly <paramref name="destination" />.Length local bytes into caller-owned storage.</summary>
	public static bool TryReadBytes(HostAddress address, Span<byte> destination, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadBytes(SReadBytes, "readBytesLocal"u8, address.ToInt64(), destination, out failure);
	}

	/// <summary>Reads exactly <paramref name="destination" />.Length local bytes and reports the number copied.</summary>
	/// <param name="address">The CE-host address to read.</param>
	/// <param name="destination">
	///     The caller-owned storage. This overload can copy a validated contiguous prefix before it
	///     observes an incomplete or malformed table.
	/// </param>
	/// <param name="written">
	///     The verified number copied, including a confirmed contiguous prefix on
	///     <see cref="MemoryAccessFailure.PartialRead" />.
	/// </param>
	/// <param name="failure">The factual CE or buffer-contract failure.</param>
	/// <returns><see langword="true" /> only after all requested bytes have been copied.</returns>
	public static bool TryReadBytes(HostAddress address, Span<byte> destination, out int written,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadBytes(SReadBytes, "readBytesLocal"u8, address.ToInt64(), destination, out written,
			out failure);
	}

	/// <summary>Writes the caller-owned byte sequence in source order as one local Lua byte table.</summary>
	public static bool TryWriteBytes(HostAddress address, ReadOnlySpan<byte> value, out MemoryAccessFailure failure)
	{
		return TryWriteBytes(address, value, out _, out failure);
	}

	/// <summary>Writes a local byte sequence and reports the exact byte count returned by Cheat Engine.</summary>
	/// <param name="address">The CE-host address to write.</param>
	/// <param name="value">The caller-owned bytes in source order.</param>
	/// <param name="written">
	///     The CE-reported count, including a confirmed partial count on
	///     <see cref="MemoryAccessFailure.WriteFailed" />.
	/// </param>
	/// <param name="failure">The factual CE or result-contract failure.</param>
	/// <returns><see langword="true" /> only when CE reports the complete requested count.</returns>
	public static bool TryWriteBytes(HostAddress address, ReadOnlySpan<byte> value, out int written,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteBytes(SWriteBytes, "writeBytesLocal"u8, address.ToInt64(), value, out written,
			out failure);
	}

	/// <summary>Reads a local UTF-8 Lua string into caller-owned storage.</summary>
	public static bool TryReadUtf8(HostAddress address, int maximumLength, Span<byte> destination, bool wideCharacter,
		out int written, out MemoryAccessFailure failure)
	{
		return TryReadUtf8(address, maximumLength, destination, wideCharacter, out written, out _, out failure);
	}

	/// <summary>Reads local UTF-8 text and reports the exact capacity required by the returned value.</summary>
	/// <param name="address">The CE-host address to read.</param>
	/// <param name="maximumLength">
	///     The maximum length passed unchanged to CE's documented string primitive; its unit for a wide read is not
	///     qualified (see the class remarks). Must not be negative.
	/// </param>
	/// <param name="destination">The caller-owned UTF-8 storage; it is unchanged when it is too small.</param>
	/// <param name="wideCharacter">Whether CE should read a wide-character string.</param>
	/// <param name="written">The copied byte count, which is zero on failure.</param>
	/// <param name="requiredLength">The returned UTF-8 byte count when CE supplied a string, including a short destination.</param>
	/// <param name="failure">The factual CE or capacity failure.</param>
	/// <returns><see langword="true" /> only after the complete UTF-8 value has been copied.</returns>
	public static bool TryReadUtf8(HostAddress address, int maximumLength, Span<byte> destination, bool wideCharacter,
		out int written, out int requiredLength, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadUtf8(SReadString, "readStringLocal"u8, address.ToInt64(), maximumLength, wideCharacter,
			destination, out written, out requiredLength, out failure);
	}

	/// <summary>Reads a local string into a managed string; use the byte-span overload on allocation-sensitive paths.</summary>
	public static bool TryReadString(HostAddress address, int maximumLength, bool wideCharacter, out string? value,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadString(SReadString, "readStringLocal"u8, address.ToInt64(), maximumLength,
			wideCharacter,
			out value, out failure);
	}

	/// <summary>Writes a UTF-8 byte sequence through CE's local <c>writeString</c> operation.</summary>
	public static bool TryWriteUtf8(HostAddress address, ReadOnlySpan<byte> value, bool wideCharacter,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteUtf8(SWriteString, "writeStringLocal"u8, address.ToInt64(), value, wideCharacter,
			out failure);
	}

	/// <summary>Writes UTF-16 text as a Lua UTF-8 string in Cheat Engine's host address space.</summary>
	public static bool TryWriteString(HostAddress address, ReadOnlySpan<char> value, bool wideCharacter,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteText(SWriteString, "writeStringLocal"u8, address.ToInt64(), value, wideCharacter,
			out failure);
	}
}
