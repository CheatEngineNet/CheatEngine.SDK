using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.References;

namespace CheatEngine.SDK.Engine.Memory;

/// <summary>Typed access to the attached target process through Cheat Engine 7.7's documented memory Lua globals.</summary>
/// <remarks>
///     <para>
///         <b>Provenance.</b> Names, argument order, scalar widths and the <c>signed</c> flag come from the CE
///         7.7.0.10621 <c>celua.txt</c> memory catalog (the entries headed <c>readBytes</c> through
///         <c>writeString</c>). The exact Lua identifiers remain implementation details here. <c>readPointer</c> and
///         <c>writePointer</c> are target-aware: CE selects a 32-bit or 64-bit pointer from the attached target.
///     </para>
///     <para>
///         <b>Lifecycle and threading.</b> Every operation acquires the current thread's Lua state once and requires an
///         enabled plugin; it throws <see cref="InvalidOperationException" /> while detached. CE's catalog does not
///         establish a main-thread-only rule for these globals, so this API does not claim one. The hosting layer remains
///         responsible for serializing a host that requires it. These static operations own no CE object, pointer, Lua
///         reference or buffer.
///     </para>
///     <para>
///         A <c>Try</c> method restores the Lua stack on every path. It reports an expected memory failure, unavailable
///         global, protected Lua error, or malformed result through the <c>failure</c> out parameter. A disabled runtime
///         is a
///         lifecycle violation, not a CE read failure, and is therefore never folded into that result.
///     </para>
/// </remarks>
[RequiresPluginEnabled]
public static class TargetMemory
{
	private static readonly LuaRef SReadByte = new();
	private static readonly LuaRef SReadSmallInteger = new();
	private static readonly LuaRef SReadInteger = new();
	private static readonly LuaRef SReadQword = new();
	private static readonly LuaRef SReadPointer = new();
	private static readonly LuaRef SReadFloat = new();
	private static readonly LuaRef SReadDouble = new();
	private static readonly LuaRef SReadBytes = new();
	private static readonly LuaRef SReadString = new();
	private static readonly LuaRef SWriteByte = new();
	private static readonly LuaRef SWriteSmallInteger = new();
	private static readonly LuaRef SWriteInteger = new();
	private static readonly LuaRef SWriteQword = new();
	private static readonly LuaRef SWritePointer = new();
	private static readonly LuaRef SWriteFloat = new();
	private static readonly LuaRef SWriteDouble = new();
	private static readonly LuaRef SWriteBytes = new();
	private static readonly LuaRef SWriteString = new();

	/// <summary>Reads an unsigned 8-bit integer from the target.</summary>
	public static bool TryReadUInt8(Address address, out byte value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadByte, "readByte"u8, address.ToInt64(), false,
			    false, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		return TryUnsigned(raw, byte.MaxValue, out value, out failure);
	}

	/// <summary>Reads a signed 8-bit integer from the target.</summary>
	public static bool TryReadInt8(Address address, out sbyte value, out MemoryAccessFailure failure)
	{
		if (!TryReadUInt8(address, out byte raw, out failure))
		{
			value = default;
			return false;
		}

		value = unchecked((sbyte) raw);
		return true;
	}

	/// <summary>Reads an unsigned 16-bit integer from the target.</summary>
	public static bool TryReadUInt16(Address address, out ushort value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadSmallInteger, "readSmallInteger"u8, address.ToInt64(), false,
			    true,
			    out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		return TryUnsigned(raw, ushort.MaxValue, out value, out failure);
	}

	/// <summary>Reads a signed 16-bit integer from the target.</summary>
	public static bool TryReadInt16(Address address, out short value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadSmallInteger, "readSmallInteger"u8, address.ToInt64(), true,
			    true,
			    out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		return TrySigned(raw, short.MinValue, short.MaxValue, out value, out failure);
	}

	/// <summary>Reads an unsigned 32-bit integer from the target.</summary>
	public static bool TryReadUInt32(Address address, out uint value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadInteger, "readInteger"u8, address.ToInt64(), false,
			    true, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		return TryUnsigned(raw, uint.MaxValue, out value, out failure);
	}

	/// <summary>Reads a signed 32-bit integer from the target.</summary>
	public static bool TryReadInt32(Address address, out int value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadInteger, "readInteger"u8, address.ToInt64(), true,
			    true, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		return TrySigned(raw, int.MinValue, int.MaxValue, out value, out failure);
	}

	/// <summary>Reads an unsigned 64-bit integer from the target, preserving all Lua integer bits.</summary>
	public static bool TryReadUInt64(Address address, out ulong value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadQword, "readQword"u8, address.ToInt64(), false,
			    false, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		value = unchecked((ulong) raw);
		return true;
	}

	/// <summary>Reads a signed 64-bit integer from the target.</summary>
	public static bool TryReadInt64(Address address, out long value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadInteger(SReadQword, "readQword"u8, address.ToInt64(), false,
			false, out value, out failure);
	}

	/// <summary>Reads a pointer whose width Cheat Engine selects from the attached target architecture.</summary>
	public static bool TryReadPointer(Address address, out Address value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadInteger(SReadPointer, "readPointer"u8, address.ToInt64(), false,
			    false, out long raw,
			    out failure))
		{
			value = default;
			return false;
		}

		value = Address.FromInt64(raw);
		return true;
	}

	/// <summary>Reads a pointer and verifies that it fits the explicitly observed target pointer width.</summary>
	/// <param name="address">The target address of the pointer value.</param>
	/// <param name="pointerSize">The observed target pointer width; <see cref="PointerSize.Unknown" /> is refused.</param>
	/// <param name="value">The pointer address, or zero when this method returns <see langword="false" />.</param>
	/// <param name="failure">The factual CE or target-width failure.</param>
	/// <returns><see langword="true" /> when CE returned a pointer that fits <paramref name="pointerSize" />.</returns>
	/// <remarks>
	///     This overload qualifies an ambient CE <c>readPointer</c> result with a target fact supplied by the caller.
	///     It deliberately never uses <see cref="IntPtr.Size" />: the x64 plugin host can inspect an x86 target.
	/// </remarks>
	public static bool TryReadPointer(Address address, PointerSize pointerSize, out Address value,
		out MemoryAccessFailure failure)
	{
		if (!pointerSize.IsKnown)
		{
			value = default;
			failure = MemoryAccessFailure.PointerWidthUnknown;
			return false;
		}

		if (!TryReadPointer(address, out value, out failure))
		{
			return false;
		}

		if (pointerSize == PointerSize.Bit32 && value.Value > uint.MaxValue)
		{
			value = default;
			failure = MemoryAccessFailure.PointerValueExceedsTargetWidth;
			return false;
		}

		return true;
	}

	/// <summary>Reads a single-precision floating-point value from the target.</summary>
	public static bool TryReadSingle(Address address, out float value, out MemoryAccessFailure failure)
	{
		if (!MemoryLua.TryReadNumber(SReadFloat, "readFloat"u8, address.ToInt64(), out double raw, out failure))
		{
			value = default;
			return false;
		}

		value = (float) raw;
		return true;
	}

	/// <summary>Reads a double-precision floating-point value from the target.</summary>
	public static bool TryReadDouble(Address address, out double value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadNumber(SReadDouble, "readDouble"u8, address.ToInt64(), out value, out failure);
	}

	/// <summary>Writes an unsigned 8-bit integer to the target.</summary>
	public static bool TryWriteUInt8(Address address, byte value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteByte, "writeByte"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a signed 8-bit integer to the target.</summary>
	public static bool TryWriteInt8(Address address, sbyte value, out MemoryAccessFailure failure)
	{
		return TryWriteUInt8(address, unchecked((byte) value), out failure);
	}

	/// <summary>Writes an unsigned 16-bit integer to the target.</summary>
	public static bool TryWriteUInt16(Address address, ushort value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteSmallInteger, "writeSmallInteger"u8, address.ToInt64(), value,
			out failure);
	}

	/// <summary>Writes a signed 16-bit integer to the target.</summary>
	public static bool TryWriteInt16(Address address, short value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteSmallInteger, "writeSmallInteger"u8, address.ToInt64(), value,
			out failure);
	}

	/// <summary>Writes an unsigned 32-bit integer to the target.</summary>
	public static bool TryWriteUInt32(Address address, uint value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteInteger, "writeInteger"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a signed 32-bit integer to the target.</summary>
	public static bool TryWriteInt32(Address address, int value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteInteger, "writeInteger"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes an unsigned 64-bit integer to the target, preserving all bits.</summary>
	public static bool TryWriteUInt64(Address address, ulong value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteQword, "writeQword"u8, address.ToInt64(), unchecked((long) value),
			out failure);
	}

	/// <summary>Writes a signed 64-bit integer to the target.</summary>
	public static bool TryWriteInt64(Address address, long value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWriteQword, "writeQword"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a target-aware pointer value to the target without independently qualifying its target width.</summary>
	/// <remarks>
	///     Retained for compatibility. Call the overload that accepts an observed <see cref="PointerSize" /> whenever
	///     a 32-bit target could be selected: CE's legacy x86 pointer primitive truncates a value wider than 32 bits.
	/// </remarks>
	public static bool TryWritePointer(Address address, Address value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteInteger(SWritePointer, "writePointer"u8, address.ToInt64(), value.ToInt64(),
			out failure);
	}

	/// <summary>Writes a pointer after verifying that it fits the explicitly observed target pointer width.</summary>
	/// <param name="address">The target address of the pointer value.</param>
	/// <param name="value">The pointer value to write.</param>
	/// <param name="pointerSize">The observed target pointer width; <see cref="PointerSize.Unknown" /> is refused.</param>
	/// <param name="failure">The factual CE or target-width failure.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="value" /> fits <paramref name="pointerSize" /> and CE reports
	///     success.
	/// </returns>
	/// <remarks>
	///     The observation is supplied by the caller and cannot make CE's ambient target selection atomic with this
	///     write. It does prevent this SDK call from silently narrowing a 64-bit value through CE's x86 primitive.
	/// </remarks>
	public static bool TryWritePointer(Address address, Address value, PointerSize pointerSize,
		out MemoryAccessFailure failure)
	{
		if (!pointerSize.IsKnown)
		{
			failure = MemoryAccessFailure.PointerWidthUnknown;
			return false;
		}

		if (pointerSize == PointerSize.Bit32 && value.Value > uint.MaxValue)
		{
			failure = MemoryAccessFailure.PointerValueExceedsTargetWidth;
			return false;
		}

		return TryWritePointer(address, value, out failure);
	}

	/// <summary>Writes a single-precision floating-point value to the target.</summary>
	public static bool TryWriteSingle(Address address, float value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteNumber(SWriteFloat, "writeFloat"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Writes a double-precision floating-point value to the target.</summary>
	public static bool TryWriteDouble(Address address, double value, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteNumber(SWriteDouble, "writeDouble"u8, address.ToInt64(), value, out failure);
	}

	/// <summary>Reads exactly <paramref name="destination" />.Length bytes from the target into caller-owned storage.</summary>
	public static bool TryReadBytes(Address address, Span<byte> destination, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadBytes(SReadBytes, "readBytes"u8, address.ToInt64(), destination, out failure);
	}

	/// <summary>
	///     Reads exactly <paramref name="destination" />.Length bytes into caller-owned storage and reports the number
	///     copied.
	/// </summary>
	/// <param name="address">The target address to read.</param>
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
	public static bool TryReadBytes(Address address, Span<byte> destination, out int written,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadBytes(SReadBytes, "readBytes"u8, address.ToInt64(), destination, out written,
			out failure);
	}

	/// <summary>Writes the caller-owned byte sequence as one ordered Lua byte table.</summary>
	public static bool TryWriteBytes(Address address, ReadOnlySpan<byte> value, out MemoryAccessFailure failure)
	{
		return TryWriteBytes(address, value, out _, out failure);
	}

	/// <summary>Writes a caller-owned byte sequence and reports the exact byte count returned by Cheat Engine.</summary>
	/// <param name="address">The target address to write.</param>
	/// <param name="value">The caller-owned bytes in source order.</param>
	/// <param name="written">
	///     The CE-reported count, including a confirmed partial count on
	///     <see cref="MemoryAccessFailure.WriteFailed" />.
	/// </param>
	/// <param name="failure">The factual CE or result-contract failure.</param>
	/// <returns><see langword="true" /> only when CE reports the complete requested count.</returns>
	public static bool TryWriteBytes(Address address, ReadOnlySpan<byte> value, out int written,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteBytes(SWriteBytes, "writeBytes"u8, address.ToInt64(), value, out written,
			out failure);
	}

	/// <summary>Reads a UTF-8 Lua string into caller-owned storage without retaining a Lua-owned span.</summary>
	public static bool TryReadUtf8(Address address, int maximumLength, Span<byte> destination, bool wideCharacter,
		out int written, out MemoryAccessFailure failure)
	{
		return TryReadUtf8(address, maximumLength, destination, wideCharacter, out written, out _, out failure);
	}

	/// <summary>Reads UTF-8 text into caller-owned storage and reports the exact capacity required by the returned text.</summary>
	/// <param name="address">The target address to read.</param>
	/// <param name="maximumLength">The maximum character count passed to CE's documented string primitive.</param>
	/// <param name="destination">The caller-owned UTF-8 storage; it is unchanged when it is too small.</param>
	/// <param name="wideCharacter">Whether CE should read a wide-character string.</param>
	/// <param name="written">The copied byte count, which is zero on failure.</param>
	/// <param name="requiredLength">The returned UTF-8 byte count when CE supplied a string, including a short destination.</param>
	/// <param name="failure">The factual CE or capacity failure.</param>
	/// <returns><see langword="true" /> only after the complete UTF-8 value has been copied.</returns>
	public static bool TryReadUtf8(Address address, int maximumLength, Span<byte> destination, bool wideCharacter,
		out int written, out int requiredLength, out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadUtf8(SReadString, "readString"u8, address.ToInt64(), maximumLength, wideCharacter,
			destination, out written, out requiredLength, out failure);
	}

	/// <summary>Reads a UTF-8 Lua string into a managed string; use the byte-span overload on allocation-sensitive paths.</summary>
	public static bool TryReadString(Address address, int maximumLength, bool wideCharacter, out string? value,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryReadString(SReadString, "readString"u8, address.ToInt64(), maximumLength, wideCharacter,
			out value, out failure);
	}

	/// <summary>Writes a UTF-8 byte sequence through CE's <c>writeString</c> global.</summary>
	public static bool TryWriteUtf8(Address address, ReadOnlySpan<byte> value, bool wideCharacter,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteUtf8(SWriteString, "writeString"u8, address.ToInt64(), value, wideCharacter,
			out failure);
	}

	/// <summary>Writes UTF-16 text as a Lua UTF-8 string without exposing an ambiguous raw address.</summary>
	public static bool TryWriteString(Address address, ReadOnlySpan<char> value, bool wideCharacter,
		out MemoryAccessFailure failure)
	{
		return MemoryLua.TryWriteText(SWriteString, "writeString"u8, address.ToInt64(), value, wideCharacter,
			out failure);
	}

	private static bool TryUnsigned(long raw, ulong maximum, out byte value, out MemoryAccessFailure failure)
	{
		if (raw < 0 || (ulong) raw > maximum)
		{
			value = default;
			failure = MemoryAccessFailure.InvalidResult;
			return false;
		}

		value = (byte) raw;
		failure = MemoryAccessFailure.None;
		return true;
	}

	private static bool TryUnsigned(long raw, ulong maximum, out ushort value, out MemoryAccessFailure failure)
	{
		if (raw < 0 || (ulong) raw > maximum)
		{
			value = default;
			failure = MemoryAccessFailure.InvalidResult;
			return false;
		}

		value = (ushort) raw;
		failure = MemoryAccessFailure.None;
		return true;
	}

	private static bool TryUnsigned(long raw, ulong maximum, out uint value, out MemoryAccessFailure failure)
	{
		if (raw < 0 || (ulong) raw > maximum)
		{
			value = default;
			failure = MemoryAccessFailure.InvalidResult;
			return false;
		}

		value = (uint) raw;
		failure = MemoryAccessFailure.None;
		return true;
	}

	private static bool TrySigned(long raw, long minimum, long maximum, out short value,
		out MemoryAccessFailure failure)
	{
		if (raw < minimum || raw > maximum)
		{
			value = default;
			failure = MemoryAccessFailure.InvalidResult;
			return false;
		}

		value = (short) raw;
		failure = MemoryAccessFailure.None;
		return true;
	}

	private static bool TrySigned(long raw, long minimum, long maximum, out int value, out MemoryAccessFailure failure)
	{
		if (raw < minimum || raw > maximum)
		{
			value = default;
			failure = MemoryAccessFailure.InvalidResult;
			return false;
		}

		value = (int) raw;
		failure = MemoryAccessFailure.None;
		return true;
	}
}
