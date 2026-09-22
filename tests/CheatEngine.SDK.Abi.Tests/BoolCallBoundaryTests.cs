using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Tests;

/// <summary>
///     Proves, on the architecture the tests run on, that the single-field boolean wrappers cross an unmanaged call
///     boundary exactly like the primitive they wrap. Each test deliberately calls a function through a pointer whose
///     signature differs from the callee's by that one substitution.
/// </summary>
public sealed unsafe class BoolCallBoundaryTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(-1)]
	[InlineData(0x100)]
	public void Bool32_result_of_a_callee_returning_int_carries_the_same_bits(int raw)
	{
		delegate* unmanaged[Stdcall]<int, int> callee = &ReturnInt;
		delegate* unmanaged[Stdcall]<int, Bool32> asBool32 = (delegate* unmanaged[Stdcall]<int, Bool32>) callee;

		Bool32 result = asBool32(raw);

		Assert.Equal(raw, result.RawValue);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(-1)]
	[InlineData(0x100)]
	public void Bool32_returned_by_a_callee_reads_as_the_same_int(int raw)
	{
		delegate* unmanaged[Stdcall]<int, Bool32> callee = &ReturnBool32;
		delegate* unmanaged[Stdcall]<int, int> asInt = (delegate* unmanaged[Stdcall]<int, int>) callee;

		Assert.Equal(raw, asInt(raw));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(-1)]
	public void Bool32_argument_arrives_as_the_same_int(int raw)
	{
		delegate* unmanaged[Stdcall]<long, Bool32, long, int> callee = &ReturnMiddleBool32;
		delegate* unmanaged[Stdcall]<long, int, long, int> asInt =
			(delegate* unmanaged[Stdcall]<long, int, long, int>) callee;

		Assert.Equal(raw, asInt(long.MinValue, raw, long.MaxValue));
	}

	[Fact]
	public void Bool32_written_through_a_pointer_argument_is_a_four_byte_write()
	{
		delegate* unmanaged[Stdcall]<Bool32*, void> callee = &WriteTrue;
		ulong storage = 0xAAAA_AAAA_AAAA_AAAA;

		callee((Bool32*) &storage);

		ulong expected = BitConverter.IsLittleEndian ? 0xAAAA_AAAA_0000_0001 : 0x0000_0001_AAAA_AAAA;
		Assert.Equal(expected, storage);
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(1, true)]
	[InlineData(0xFF, true)]
	public void Bool8_result_ignores_garbage_above_the_low_byte(int lowByte, bool expected)
	{
		delegate* unmanaged[Stdcall]<int, int> callee = &ReturnLowByteUnderGarbage;
		delegate* unmanaged[Stdcall]<int, Bool8> asBool8 = (delegate* unmanaged[Stdcall]<int, Bool8>) callee;

		Bool8 result = asBool8(lowByte);

		Assert.Equal(expected, result.IsTrue);
		Assert.Equal((byte) lowByte, result.RawValue);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(0xFF)]
	public void Bool8_returned_by_a_callee_reads_as_the_same_byte(byte raw)
	{
		delegate* unmanaged[Stdcall]<byte, Bool8> callee = &ReturnBool8;
		delegate* unmanaged[Stdcall]<byte, byte> asByte = (delegate* unmanaged[Stdcall]<byte, byte>) callee;

		Assert.Equal(raw, asByte(raw));
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int ReturnInt(int value)
	{
		return value;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static Bool32 ReturnBool32(int raw)
	{
		return new Bool32(raw);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int ReturnMiddleBool32(long before, Bool32 value, long after)
	{
		return before == long.MinValue && after == long.MaxValue ? value.RawValue : 0x0BAD;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void WriteTrue(Bool32* target)
	{
		*target = Bool32.True;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int ReturnLowByteUnderGarbage(int lowByte)
	{
		return unchecked((int) 0xABCDEF00) | (lowByte & 0xFF);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static Bool8 ReturnBool8(byte raw)
	{
		return new Bool8(raw);
	}
}
