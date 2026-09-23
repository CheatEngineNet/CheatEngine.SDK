using System.Text;

using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     Native-Lua tests for the assembler's call shape: argument count per overload, the echoed preference and
///     range-check option, rejection shapes and exact text bytes. The <c>assemble</c> stand-in records what it received;
///     it is not Cheat Engine's assembler.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class InstructionAssemblerTests
{
	private static ReadOnlySpan<byte> AssembleRecorder => """
	                                                      assemble_argument_count = -1
	                                                      assemble_line_length = -1
	                                                      assemble_address_type = 'none'
	                                                      assemble_address = 0
	                                                      assemble_preference = 'none'
	                                                      assemble_skip_range_check = 'none'
	                                                      assemble_message_reads = 0
	                                                      assemble_mode = 'bytes'
	                                                      local message = setmetatable({}, {
	                                                        __tostring = function() assemble_message_reads = assemble_message_reads + 1 return 'message' end,
	                                                        __index = function() assemble_message_reads = assemble_message_reads + 1 end,
	                                                        __len = function() assemble_message_reads = assemble_message_reads + 1 return 0 end,
	                                                      })
	                                                      function assemble(...)
	                                                        local line, address, preference, skip = ...
	                                                        assemble_argument_count = select('#', ...)
	                                                        assemble_line_length = #line
	                                                        assemble_address_type = math.type(address)
	                                                        assemble_address = address
	                                                        assemble_preference = preference == nil and 'nil' or preference
	                                                        if assemble_argument_count >= 4 then assemble_skip_range_check = skip end
	                                                        if assemble_mode == 'rejected' then return nil end
	                                                        if assemble_mode == 'rejected-with-message' then return nil, 'unknown opcode: frobnicate' end
	                                                        if assemble_mode == 'rejected-with-probe' then return nil, message end
	                                                        if assemble_mode == 'nothing' then return end
	                                                        if assemble_mode == 'string' then return 'E9' end
	                                                        return { 0xEB, 0xFE }
	                                                      end
	                                                      """u8;

	[Theory]
	[InlineData(AssemblePreference.None, false)]
	[InlineData(AssemblePreference.None, true)]
	[InlineData(AssemblePreference.Short, false)]
	[InlineData(AssemblePreference.Short, true)]
	[InlineData(AssemblePreference.Long, false)]
	[InlineData(AssemblePreference.Long, true)]
	[InlineData(AssemblePreference.Far, false)]
	[InlineData(AssemblePreference.Far, true)]
	public void assemble_with_a_preference_and_range_check_option_passes_four_arguments_and_echoes_them(
		AssemblePreference preference, bool skipRangeCheck)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		Span<byte> destination = stackalloc byte[2];
		Address origin = 0x1_0000_0000UL;

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(profile, "jmp short $", origin,
			preference, skipRangeCheck, destination, out InstructionAssembly assembly);

		Assert.Equal(InstructionOperationStatus.Success, status);
		Assert.Equal(new byte[] { 0xEB, 0xFE }, destination.ToArray());
		Assert.Equal(new InstructionAssembly(profile.Target, profile.Profile, origin, preference, skipRangeCheck, 2,
			2), assembly);
		Assert.Equal(4, ReadInteger(scope.State, "assemble_argument_count"));
		Assert.Equal((long) preference, ReadInteger(scope.State, "assemble_preference"));
		Assert.Equal(skipRangeCheck, ReadBoolean(scope.State, "assemble_skip_range_check"));
		Assert.Equal("integer", ReadString(scope.State, "assemble_address_type"));
		Assert.Equal(0x1_0000_0000L, ReadInteger(scope.State, "assemble_address"));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void legacy_assemble_overload_still_passes_exactly_two_arguments()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		Span<byte> destination = stackalloc byte[2];

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(profile, "jmp short $", 0x401000UL,
			destination, out int written, out int requiredLength);

		Assert.Equal(InstructionOperationStatus.Success, status);
		Assert.Equal(2, written);
		Assert.Equal(2, requiredLength);
		// The legacy overload omits the preference and the option (no pushed nil), so CE applies its own defaults.
		Assert.Equal(2, ReadInteger(scope.State, "assemble_argument_count"));
		Assert.Equal("nil", ReadString(scope.State, "assemble_preference"));
		Assert.Equal("none", ReadString(scope.State, "assemble_skip_range_check"));
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData((AssemblePreference) 4)]
	[InlineData((AssemblePreference) 255)]
	public void assemble_rejects_an_undefined_preference_before_entering_lua(AssemblePreference preference)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		byte[] destination = new byte[2];

		Assert.Throws<ArgumentOutOfRangeException>(() => InstructionAssembler.TryAssemble(profile, "nop", 0x401000UL,
			preference, false, destination, out _));
		Assert.Equal(-1, ReadInteger(scope.State, "assemble_argument_count"));
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("rejected", "frobnicate eax")]
	[InlineData("rejected-with-message", "frobnicate eax")]
	[InlineData("rejected", "jmp missing_symbol")]
	[InlineData("rejected-with-message", "jmp missing_symbol")]
	[InlineData("rejected-with-probe", "jmp missing_symbol")]
	public void assemble_rejection_with_or_without_a_host_message_is_instruction_rejected_without_reading_the_message(
		string mode, string instruction)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("assemble_mode = '" + mode + "'"));
		Span<byte> destination = stackalloc byte[2];
		destination.Fill(0xA5);

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(profile, instruction, 0x401000UL,
			AssemblePreference.Long, false, destination, out InstructionAssembly assembly);

		Assert.Equal(InstructionOperationStatus.InstructionRejected, status);
		Assert.Equal(0, assembly.Written);
		Assert.Equal(0, assembly.RequiredLength);
		Assert.Equal(AssemblePreference.Long, assembly.Preference);
		Assert.Equal(new byte[] { 0xA5, 0xA5 }, destination.ToArray());
		Assert.Equal(0, ReadInteger(scope.State, "assemble_message_reads"));
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("nothing")]
	[InlineData("string")]
	public void assemble_without_a_result_or_with_a_non_table_result_is_an_invalid_result(string mode)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("assemble_mode = '" + mode + "'"));
		Span<byte> destination = stackalloc byte[2];

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(profile, "nop", 0x401000UL,
			AssemblePreference.None, false, destination, out InstructionAssembly assembly);

		Assert.Equal(InstructionOperationStatus.InvalidResult, status);
		Assert.Equal(0, assembly.Written);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void assemble_with_a_short_destination_reports_the_required_length_and_echoes_the_context()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		Span<byte> destination = stackalloc byte[1];
		destination[0] = 0xA5;

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(profile, "jmp short $", 0x401000UL,
			AssemblePreference.Short, true, destination, out InstructionAssembly assembly);

		Assert.Equal(InstructionOperationStatus.DestinationTooSmall, status);
		Assert.Equal(0, assembly.Written);
		Assert.Equal(2, assembly.RequiredLength);
		Assert.True(assembly.SkipRangeCheck);
		Assert.Equal(new Address(0x401000UL), assembly.Origin);
		Assert.Equal(0xA5, destination[0]);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void assemble_keeps_embedded_nul_bytes_of_the_instruction_text()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstructionTargetProfile profile = Install(scope.State);
		const string Instruction = "db 'a\0b' é";
		Span<byte> destination = stackalloc byte[2];

		InstructionOperationStatus legacy = InstructionAssembler.TryAssemble(profile, Instruction, 0x401000UL,
			destination, out _, out _);
		long legacyLength = ReadInteger(scope.State, "assemble_line_length");
		InstructionOperationStatus detailed = InstructionAssembler.TryAssemble(profile, Instruction, 0x401000UL,
			AssemblePreference.None, false, destination, out _);

		Assert.Equal(InstructionOperationStatus.Success, legacy);
		Assert.Equal(InstructionOperationStatus.Success, detailed);
		Assert.Equal(Encoding.UTF8.GetByteCount(Instruction), legacyLength);
		Assert.Equal(Encoding.UTF8.GetByteCount(Instruction), ReadInteger(scope.State, "assemble_line_length"));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void assemble_on_an_x86_profile_refuses_an_origin_above_4_gib_before_entering_lua_and_echoes_it()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X86TargetFacts(scope.State, 21544);
		EngineTest.Run(scope.State, AssembleRecorder);
		Assert.Equal(InstructionOperationStatus.Success,
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile x86));
		Span<byte> destination = stackalloc byte[2];

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(x86, "nop", 0x1_0000_0000UL,
			AssemblePreference.Far, false, destination, out InstructionAssembly assembly);

		Assert.Equal(InstructionOperationStatus.AddressExceedsProfileWidth, status);
		Assert.Equal(InstructionProfile.X86, assembly.Profile);
		Assert.Equal(new Address(0x1_0000_0000UL), assembly.Origin);
		Assert.Equal(-1, ReadInteger(scope.State, "assemble_argument_count"));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void assemble_preference_values_equal_the_ce_constants()
	{
		// TassemblerPreference = (apNone, apShort, apLong, apFar): Assemblerunit.pas:2712 at ec45d5f; celua.txt:236-238.
		Assert.Equal(0, (byte) AssemblePreference.None);
		Assert.Equal(1, (byte) AssemblePreference.Short);
		Assert.Equal(2, (byte) AssemblePreference.Long);
		Assert.Equal(3, (byte) AssemblePreference.Far);
		Assert.Equal(4, Enum.GetValues<AssemblePreference>().Length);
	}

	private static InstructionTargetProfile Install(LuaState state)
	{
		FakeHost.InstallCe77X64TargetFacts(state, 45052);
		EngineTest.Run(state, AssembleRecorder);
		InstructionOperationStatus status = InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile profile);
		Assert.Equal(InstructionOperationStatus.Success, status);
		return profile;
	}

	private static long ReadInteger(LuaState state, string name)
	{
		using LuaFrame frame = new(state);
		Assert.True(state.TryGetGlobal(Encoding.UTF8.GetBytes(name)).IsOk);
		return EngineTest.ReadInteger(state, -1);
	}

	private static string ReadString(LuaState state, string name)
	{
		using LuaFrame frame = new(state);
		Assert.True(state.TryGetGlobal(Encoding.UTF8.GetBytes(name)).IsOk);
		return EngineTest.ReadString(state, -1);
	}

	private static bool ReadBoolean(LuaState state, string name)
	{
		using LuaFrame frame = new(state);
		Assert.True(state.TryGetGlobal(Encoding.UTF8.GetBytes(name)).IsOk);
		Assert.Equal(LuaType.Boolean, state.TypeOf(-1));
		return state.ToBoolean(-1);
	}
}
