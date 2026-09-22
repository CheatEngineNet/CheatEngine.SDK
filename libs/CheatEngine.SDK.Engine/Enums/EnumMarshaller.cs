using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Enums;

/// <summary>
///     An enum as the Lua integer Cheat Engine's functions take and return for it: <see cref="VariableType" /> for
///     <c>MemoryRecord.Type</c> and the <c>vartype</c> argument of <c>firstScan</c>, <see cref="ScanOption" />,
///     <see cref="BreakpointTrigger" />, ... Works for any enum whose underlying type is an integer, with the value
///     carried exactly (an unsigned member above <see cref="int.MaxValue" /> is pushed as its positive value).
/// </summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
/// <remarks>
///     Reading follows Lua's own integer conversion (<c>lua_tointegerx</c>, as <see cref="Int64Marshaller" /> does: an
///     integral float or a convertible string qualifies) and accepts any value that fits the underlying type; whether
///     it names a defined member is not checked, because Cheat Engine can return combinations
///     (<see cref="MemoryProtection" />) or values newer than this SDK. The underlying type is determined once per
///     instantiation (<see cref="Type.GetTypeCode" />); every push and read is then one C API call, a range check and a
///     bit cast, with no boxing and no reflection.
/// </remarks>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
	Justification =
		"ILuaMarshaller<T> is a static-abstract contract: a marshaller has static members only, and generic code names the type argument once (EnumMarshaller<VariableType>.Push) exactly as it would for a non-generic marshaller.")]
[SuppressMessage("Meziantou.Analyzer", "MA0018",
	Justification =
		"ILuaMarshaller<T> is a static-abstract contract: a marshaller has static members only, and generic code names the type argument once (EnumMarshaller<VariableType>.Push) exactly as it would for a non-generic marshaller.")]
public readonly struct EnumMarshaller<TEnum> : ILuaMarshaller<TEnum>
	where TEnum : unmanaged, Enum
{
	// Type.GetTypeCode of an enum type is the code of its underlying integer type.
	private static readonly TypeCode s_underlying = Type.GetTypeCode(typeof(TEnum));

	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, TEnum value)
	{
		state.PushInteger(ToInt64(value));
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out TEnum value)
	{
		if (state.TryReadInteger(index, out long bits) && TryFromInt64(bits, out value))
		{
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>The integer value of <paramref name="value" />, as Lua carries it.</summary>
	/// <param name="value">The member.</param>
	/// <returns>The value, sign-extended for signed underlying types and zero-extended for unsigned ones.</returns>
	public static long ToInt64(TEnum value)
	{
		return s_underlying switch
		{
			TypeCode.SByte => Unsafe.BitCast<TEnum, sbyte>(value),
			TypeCode.Byte => Unsafe.BitCast<TEnum, byte>(value),
			TypeCode.Int16 => Unsafe.BitCast<TEnum, short>(value),
			TypeCode.UInt16 => Unsafe.BitCast<TEnum, ushort>(value),
			TypeCode.Int32 => Unsafe.BitCast<TEnum, int>(value),
			TypeCode.UInt32 => Unsafe.BitCast<TEnum, uint>(value),
			TypeCode.Int64 => Unsafe.BitCast<TEnum, long>(value),
			_ => unchecked((long) Unsafe.BitCast<TEnum, ulong>(value))
		};
	}

	/// <summary>The member with the integer value <paramref name="bits" />, when it fits the underlying type.</summary>
	/// <param name="bits">The Lua integer.</param>
	/// <param name="value">The member, defined or not; <see langword="default" /> on failure.</param>
	/// <returns><see langword="false" /> when the value does not fit the enum's underlying type.</returns>
	public static bool TryFromInt64(long bits, out TEnum value)
	{
		switch (s_underlying)
		{
			case TypeCode.SByte when bits is >= sbyte.MinValue and <= sbyte.MaxValue:
				value = Unsafe.BitCast<sbyte, TEnum>((sbyte) bits);
				return true;
			case TypeCode.Byte when bits is >= byte.MinValue and <= byte.MaxValue:
				value = Unsafe.BitCast<byte, TEnum>((byte) bits);
				return true;
			case TypeCode.Int16 when bits is >= short.MinValue and <= short.MaxValue:
				value = Unsafe.BitCast<short, TEnum>((short) bits);
				return true;
			case TypeCode.UInt16 when bits is >= ushort.MinValue and <= ushort.MaxValue:
				value = Unsafe.BitCast<ushort, TEnum>((ushort) bits);
				return true;
			case TypeCode.Int32 when bits is >= int.MinValue and <= int.MaxValue:
				value = Unsafe.BitCast<int, TEnum>((int) bits);
				return true;
			case TypeCode.UInt32 when bits is >= uint.MinValue and <= uint.MaxValue:
				value = Unsafe.BitCast<uint, TEnum>((uint) bits);
				return true;
			case TypeCode.Int64:
				value = Unsafe.BitCast<long, TEnum>(bits);
				return true;
			case TypeCode.UInt64:
				value = Unsafe.BitCast<ulong, TEnum>(unchecked((ulong) bits));
				return true;
			default:
				value = default;
				return false;
		}
	}
}
