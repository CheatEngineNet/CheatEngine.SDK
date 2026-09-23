using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Values;

/// <summary>
///     The byte-array first-scan request: the exact fourteen CE positions of the spike's range scan (spike-c3 P4), the
///     refusal of an empty or inverted range before any CE call (D4.3), and the exact-integer push of 64-bit bounds.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class FirstScanRequestTests
{
	[Fact]
	[Trait("Qualification", "Q28")]
	public void FirstScanRequest_ByteArray_fills_the_fourteen_CE_positions_of_a_hexadecimal_byte_array_scan()
	{
		FirstScanRequest request = FirstScanRequest.ByteArray("55 48 89 E5", new Address(0x1_0000_0000),
			new Address(0x1_0036_7000));
		FirstScanRequest aligned = FirstScanRequest.ByteArray("48 83 EC ??", new Address(0x40_0000),
			new Address(0x70_8000), "+X", FastScanMethod.Aligned, "4");

		Assert.Equal(ScanOption.ExactValue, request.ScanOption);
		Assert.Equal(VariableType.ByteArray, request.VariableType);
		Assert.Equal(RoundingType.Rounded, request.RoundingType);
		Assert.Equal("55 48 89 E5", request.Input1);
		Assert.Equal(string.Empty, request.Input2);
		Assert.Equal(new Address(0x1_0000_0000), request.StartAddress);
		Assert.Equal(new Address(0x1_0036_7000), request.StopAddress);
		Assert.Equal(string.Empty, request.ProtectionFlags);
		Assert.Equal(FastScanMethod.NotAligned, request.FastScanMethod);
		Assert.Equal(string.Empty, request.AlignmentParameter);
		Assert.True(request.IsHexadecimalInput);
		Assert.False(request.IsNotBinaryString);
		Assert.False(request.IsUnicodeScan);
		Assert.False(request.IsCaseSensitive);
		Assert.Equal("+X", aligned.ProtectionFlags);
		Assert.Equal(FastScanMethod.Aligned, aligned.FastScanMethod);
		Assert.Equal("4", aligned.AlignmentParameter);
		Assert.Equal(VariableType.ByteArray, aligned.VariableType);
		Assert.True(aligned.IsHexadecimalInput);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(0x1000UL, 0x1000UL)]
	[InlineData(0x2000UL, 0x1000UL)]
	[InlineData(0UL, 0UL)]
	[InlineData(ulong.MaxValue, 0UL)]
	public void FirstScanRequest_ByteArray_refuses_an_empty_or_inverted_range(ulong start, ulong stop)
	{
		ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
			FirstScanRequest.ByteArray("90", new Address(start), new Address(stop)));

		Assert.Equal("stopAddress", failure.ParamName);
		Assert.Throws<ArgumentOutOfRangeException>(() => FirstScanRequest.ByteArray("90", new Address(start),
			new Address(stop), string.Empty, FastScanMethod.NotAligned, string.Empty));
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void FirstScanRequest_ByteArray_refuses_null_strings()
	{
		Address start = new(0x1000);
		Address stop = new(0x2000);

		Assert.Equal("pattern",
			Assert.Throws<ArgumentNullException>(() => FirstScanRequest.ByteArray(null!, start, stop)).ParamName);
		Assert.Equal("pattern", Assert.Throws<ArgumentNullException>(() =>
			FirstScanRequest.ByteArray(null!, start, stop, string.Empty, FastScanMethod.NotAligned,
				string.Empty)).ParamName);
		Assert.Equal("protectionFlags", Assert.Throws<ArgumentNullException>(() =>
			FirstScanRequest.ByteArray("90", start, stop, null!, FastScanMethod.NotAligned, string.Empty)).ParamName);
		Assert.Equal("alignmentParameter", Assert.Throws<ArgumentNullException>(() =>
			FirstScanRequest.ByteArray("90", start, stop, string.Empty, FastScanMethod.NotAligned, null!)).ParamName);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void MemoryScanSession_first_scan_pushes_addresses_above_4_GiB_and_above_2_pow_63_as_exact_integers()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = MemScanTestHost.CreateSession(L);

		session.StartFirstScan(FirstScanRequest.ByteArray("55 48 89 E5 00 C3", new Address(0x1_0000_0000),
			new Address(0x8000_0000_0000_0000)));

		Assert.Equal("scan.first:14", MemScanTestHost.ReadTrace(L));
		MemScanTestHost.AssertLua(L, "first_scan_args.n == 14");
		MemScanTestHost.AssertLua(L, "first_scan_args[1] == 1 and first_scan_args[2] == 8 and first_scan_args[3] == 0");
		MemScanTestHost.AssertLua(L, "first_scan_args[4] == '55 48 89 E5 00 C3' and first_scan_args[5] == ''");
		MemScanTestHost.AssertLua(L, "first_scan_start_type == 'integer' and first_scan_args[6] == 0x100000000");
		MemScanTestHost.AssertLua(L, "first_scan_stop_type == 'integer' and first_scan_args[7] == math.mininteger");
		MemScanTestHost.AssertLua(L,
			"first_scan_args[8] == '' and first_scan_args[9] == 0 and first_scan_args[10] == ''");
		MemScanTestHost.AssertLua(L, "first_scan_args[11] == true and first_scan_args[12] == false");
		MemScanTestHost.AssertLua(L, "first_scan_args[13] == false and first_scan_args[14] == false");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void MemoryScanSession_first_scan_pushes_the_largest_exclusive_stop_as_minus_one()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = MemScanTestHost.CreateSession(L);

		session.StartFirstScan(FirstScanRequest.ByteArray("90", new Address(0xFFFF_FFFF_FFFF_FFFE),
			new Address(0xFFFF_FFFF_FFFF_FFFF)));

		MemScanTestHost.AssertLua(L, "first_scan_start_type == 'integer' and first_scan_args[6] == -2");
		MemScanTestHost.AssertLua(L, "first_scan_stop_type == 'integer' and first_scan_args[7] == -1");
		Assert.Equal(0, L.Top);
	}
}
