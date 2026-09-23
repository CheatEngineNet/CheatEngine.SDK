using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     Exercises the bounded instruction contracts against a Lua fixture. The fixture establishes Lua-table, target
///     probe, and text shapes only; it is not a live Cheat Engine assembler or target qualification.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class InstructionOperationsTests
{
	[Fact]
	[Trait("Qualification", "Q32.a")]
	public void observe_current_maps_the_ce_x86_family_with_64_bit_to_x64()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);

		Assert.Equal(InstructionOperationStatus.Success, observed);
		Assert.Equal(4242, targetProfile.Target.Value);
		Assert.Equal(CheatEngineArchitecture.X64, targetProfile.Profile.Architecture);
		Assert.Equal(PointerSize.Bit64, targetProfile.Profile.AddressWidth);
		Assert.True(targetProfile.Profile.IsValid);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.b")]
	public void observe_current_maps_the_ce_x86_family_without_64_bit_to_x86()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, "instruction_target_is_64bit = false"u8);

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);

		Assert.Equal(InstructionOperationStatus.Success, observed);
		Assert.Equal(CheatEngineArchitecture.X86, targetProfile.Profile.Architecture);
		Assert.Equal(PointerSize.Bit32, targetProfile.Profile.AddressWidth);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q32.d")]
	[InlineData(true)]
	[InlineData(false)]
	public void observe_current_rejects_x86_and_arm_reported_together(bool is64Bit)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"instruction_target_is_x86 = true\ninstruction_target_is_arm = true\ninstruction_target_is_64bit = " +
			LuaBoolean(is64Bit)));

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile rejectedProfile);

		Assert.Equal(InstructionOperationStatus.InvalidProfile, observed);
		Assert.Equal(default, rejectedProfile);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q32.d")]
	[InlineData(true)]
	[InlineData(false)]
	public void observe_current_rejects_a_target_reported_in_neither_family(bool is64Bit)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"instruction_target_is_x86 = false\ninstruction_target_is_arm = false\ninstruction_target_is_64bit = " +
			LuaBoolean(is64Bit)));

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile rejectedProfile);

		Assert.Equal(InstructionOperationStatus.InvalidProfile, observed);
		Assert.Equal(default, rejectedProfile);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void
		observe_current_without_a_selected_target_reports_target_not_selected_although_the_probes_look_like_x64()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		// CE 7.7 with no target: getOpenedProcessID() == 0 while the ISA probes read exactly like x64 (spike C3 D2).
		EngineTest.Run(scope.State, """
		                            function getOpenedProcessID() return 0 end
		                            function targetIs64Bit() error('targetIs64Bit must not be called without a target') end
		                            function targetIsX86() error('targetIsX86 must not be called without a target') end
		                            function targetIsArm() error('targetIsArm must not be called without a target') end
		                            """u8);

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);

		Assert.Equal(InstructionOperationStatus.TargetNotSelected, observed);
		Assert.Equal(default, targetProfile);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.c")]
	public void observe_current_reports_a_file_as_process_selection_as_an_unsupported_target_backend()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		// openFileAsProcess stores processid := $FFFFFFFF and getOpenedProcessID pushes it as a Lua integer (ec45d5f).
		EngineTest.Run(scope.State, """
		                            function getOpenedProcessID() return 4294967295 end
		                            function targetIs64Bit() error('no ISA probe for a file opened as a process') end
		                            function targetIsX86() error('no ISA probe for a file opened as a process') end
		                            function targetIsArm() error('no ISA probe for a file opened as a process') end
		                            """u8);

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);

		Assert.Equal(InstructionOperationStatus.UnsupportedTargetBackend, observed);
		Assert.Equal(default, targetProfile);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q32.d")]
	[InlineData("targetIsX86")]
	[InlineData("targetIsArm")]
	[InlineData("targetIs64Bit")]
	public void observe_current_reports_an_absent_isa_probe_as_global_unavailable(string absentGlobal)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(absentGlobal + " = nil"));

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);

		Assert.Equal(InstructionOperationStatus.GlobalUnavailable, observed);
		Assert.Equal(default, targetProfile);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("targetIsX86", "return nil")]
	[InlineData("targetIsArm", "return 1")]
	[InlineData("targetIs64Bit", "return 'true'")]
	public void observe_current_reports_a_non_boolean_isa_probe_as_invalid_result(string global, string body)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("function " + global + "() " + body + " end"));

		InstructionOperationStatus observed = InstructionProfiles.TryObserveCurrent(out _);

		Assert.Equal(InstructionOperationStatus.InvalidResult, observed);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_current_reports_a_raising_isa_probe_as_lua_failure_and_recovers_on_the_next_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, """
		                            raise_isa_probe = true
		                            function targetIsArm()
		                              if raise_isa_probe then error('fixture ISA probe failure') end
		                              return instruction_target_is_arm
		                            end
		                            """u8);

		InstructionOperationStatus failed = InstructionProfiles.TryObserveCurrent(out _);
		Assert.Equal(0, scope.State.Top);
		EngineTest.Run(scope.State, "raise_isa_probe = false"u8);
		InstructionOperationStatus recovered =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile profile);

		Assert.Equal(InstructionOperationStatus.LuaFailure, failed);
		Assert.Equal(InstructionOperationStatus.Success, recovered);
		Assert.Equal(CheatEngineArchitecture.X64, profile.Profile.Architecture);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void observe_current_reports_a_selection_change_between_the_bracketing_pid_reads_as_target_changed()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, "function targetIsArm() instruction_target_process_id = 5151 return false end"u8);

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);

		Assert.Equal(InstructionOperationStatus.TargetChanged, observed);
		Assert.Equal(default, targetProfile);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void observe_current_reads_only_the_pid_and_the_three_isa_probes()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, """
		                            function getPointerSize() error('the instruction profile never reads the configured pointer size') end
		                            function getABI() error('the instruction profile never reads the ABI') end
		                            function isConnectedToCEServer() error('the instruction profile never reads the backend') end
		                            function setAssemblerMode() error('the SDK never changes the assembler mode') end
		                            """u8);

		InstructionOperationStatus observed =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile profile);

		Assert.Equal(InstructionOperationStatus.Success, observed);
		Assert.Equal(CheatEngineArchitecture.X64, profile.Profile.Architecture);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.d")]
	public void observe_current_maps_the_arm_family_to_arm32_or_arm64_by_the_64_bit_flag_without_the_host_width()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);

		EngineTest.Run(scope.State, """
		                            instruction_target_is_x86 = false
		                            instruction_target_is_64bit = false
		                            instruction_target_is_arm = true
		                            """u8);
		InstructionTargetProfile arm32 = Observe(scope.State);

		EngineTest.Run(scope.State, "instruction_target_is_64bit = true"u8);
		InstructionTargetProfile arm64 = Observe(scope.State);

		Assert.Equal(CheatEngineArchitecture.Arm32, arm32.Profile.Architecture);
		Assert.Equal(PointerSize.Bit32, arm32.Profile.AddressWidth);
		Assert.Equal(CheatEngineArchitecture.Arm64, arm64.Profile.Architecture);
		Assert.Equal(PointerSize.Bit64, arm64.Profile.AddressWidth);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.a")]
	[Trait("Qualification", "Q32.b")]
	public void x64_and_x86_profiles_carry_their_own_address_width()
	{
		Assert.Equal(CheatEngineArchitecture.X64, InstructionProfile.X64.Architecture);
		Assert.Equal(PointerSize.Bit64, InstructionProfile.X64.AddressWidth);
		Assert.True(InstructionProfile.X64.IsValid);
		Assert.Equal(CheatEngineArchitecture.X86, InstructionProfile.X86.Architecture);
		Assert.Equal(PointerSize.Bit32, InstructionProfile.X86.AddressWidth);
		Assert.True(InstructionProfile.X86.IsValid);
		Assert.NotEqual(InstructionProfile.X64, InstructionProfile.X86);
		Assert.False(default(InstructionProfile).IsValid);
	}

	[Fact]
	[Trait("Qualification", "Q32.a")]
	public void assemble_on_an_x64_profile_accepts_an_address_above_4_gib()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);
		Span<byte> destination = stackalloc byte[5];
		// The x64 Tutorial image base observed on CE 7.7 (spike C3 D2): the first address above 4 GiB.
		Address origin = 0x1_0000_0000UL;

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(targetProfile, "jmp rel", origin,
			destination, out int written, out int requiredLength);

		Assert.Equal(InstructionOperationStatus.Success, status);
		Assert.Equal(5, written);
		Assert.Equal(5, requiredLength);
		Assert.Equal(0x1_0000_0000L, ReadInteger(scope.State, "instruction_assemble_origin"));
		Assert.Equal("integer", ReadString(scope.State, "instruction_assemble_origin_type"));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Assemble_passes_the_explicit_relative_origin_and_copies_the_complete_byte_table()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);
		Span<byte> destination = stackalloc byte[5];
		Address origin = 0x0000_0001_4000_1000UL;

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(targetProfile, "jmp rel", origin,
			destination, out int written, out int requiredLength);

		Assert.Equal(InstructionOperationStatus.Success, status);
		Assert.Equal(5, written);
		Assert.Equal(5, requiredLength);
		Assert.Equal(new byte[] { 0xE9, 0xFB, 0xFF, 0xFF, 0xFF }, destination.ToArray());
		Assert.Equal(origin.ToInt64(), ReadInteger(scope.State, "instruction_assemble_origin"));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Assemble_rejection_capacity_and_malformed_tables_do_not_publish_a_prefix()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);

		Span<byte> rejectedDestination = stackalloc byte[5];
		rejectedDestination.Fill(0xA5);
		InstructionOperationStatus rejected = InstructionAssembler.TryAssemble(targetProfile, "reject", 0x140001000UL,
			rejectedDestination, out int rejectedWritten, out int rejectedRequired);

		Assert.Equal(InstructionOperationStatus.InstructionRejected, rejected);
		Assert.Equal(0, rejectedWritten);
		Assert.Equal(0, rejectedRequired);
		Assert.Equal(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5, 0xA5 }, rejectedDestination.ToArray());

		Span<byte> tooSmallDestination = stackalloc byte[4];
		tooSmallDestination.Fill(0xA5);
		InstructionOperationStatus tooSmall = InstructionAssembler.TryAssemble(targetProfile, "jmp rel", 0x140001000UL,
			tooSmallDestination, out int tooSmallWritten, out int tooSmallRequired);

		Assert.Equal(InstructionOperationStatus.DestinationTooSmall, tooSmall);
		Assert.Equal(0, tooSmallWritten);
		Assert.Equal(5, tooSmallRequired);
		Assert.Equal(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5 }, tooSmallDestination.ToArray());

		Span<byte> malformedDestination = stackalloc byte[2];
		malformedDestination.Fill(0xA5);
		InstructionOperationStatus malformed = InstructionAssembler.TryAssemble(targetProfile, "malformed",
			0x140001000UL,
			malformedDestination, out int malformedWritten, out int malformedRequired);

		Assert.Equal(InstructionOperationStatus.InvalidResult, malformed);
		Assert.Equal(0, malformedWritten);
		Assert.Equal(0, malformedRequired);
		Assert.Equal(new byte[] { 0xA5, 0xA5 }, malformedDestination.ToArray());
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q32.b")]
	public void assemble_on_an_x86_profile_refuses_an_address_above_4_gib_before_entering_lua()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		EngineTest.Run(scope.State, "instruction_target_is_64bit = false\ninstruction_target_is_x86 = true"u8);
		InstructionTargetProfile targetProfile = Observe(scope.State);
		Span<byte> destination = stackalloc byte[5];

		InstructionOperationStatus overwide = InstructionAssembler.TryAssemble(targetProfile, "jmp rel",
			0x1_0000_0000UL,
			destination, out int written, out int requiredLength);

		Assert.Equal(InstructionOperationStatus.AddressExceedsProfileWidth, overwide);
		Assert.Equal(0, written);
		Assert.Equal(0, requiredLength);
		Assert.Equal(0, ReadInteger(scope.State, "instruction_assemble_calls"));
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(7777L)]
	[InlineData(0L)]
	[InlineData(4294967295L)]
	public void assemble_reports_a_target_change_after_the_effect_without_copying_bytes(long selectionAfterEffect)
	{
		// Another process, no target and the file-as-process sentinel all differ from the profiled selection.
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("instruction_assemble_behavior = \"target-change\"\n" +
		                                                   "instruction_assemble_next_process_id = " +
		                                                   selectionAfterEffect.ToString(CultureInfo
			                                                   .InvariantCulture)));
		Span<byte> destination = stackalloc byte[1];
		destination[0] = 0xA5;

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(targetProfile, "nop", 0x140001000UL,
			destination, out int written, out int requiredLength);

		Assert.Equal(InstructionOperationStatus.TargetChanged, status);
		Assert.Equal(0, written);
		Assert.Equal(0, requiredLength);
		Assert.Equal(0xA5, destination[0]);
		Assert.Equal(1, ReadInteger(scope.State, "instruction_assemble_calls"));
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData(0L)]
	[InlineData(4294967295L)]
	public void assemble_reports_a_selection_that_differs_before_the_call_as_target_changed_without_calling_assemble(
		long selectionBeforeCall)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(
			"instruction_target_process_id = " + selectionBeforeCall.ToString(CultureInfo.InvariantCulture)));
		Span<byte> destination = stackalloc byte[8];

		InstructionOperationStatus status = InstructionAssembler.TryAssemble(targetProfile, "nop", 0x140001000UL,
			destination, out int written, out int requiredLength);

		Assert.Equal(InstructionOperationStatus.TargetChanged, status);
		Assert.Equal(0, written);
		Assert.Equal(0, requiredLength);
		Assert.Equal(0, ReadInteger(scope.State, "instruction_assemble_calls"));
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Disassemble_parses_the_fixture_line_inside_the_SDK_and_copies_all_text()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);
		Address address = 0x0000_0001_4000_1000UL;

		InstructionOperationStatus status = InstructionDisassembler.TryDisassemble(targetProfile, address, 256,
			out InstructionDisassembly instruction, out int requiredUtf8Bytes);

		Assert.Equal(InstructionOperationStatus.Success, status);
		Assert.Equal(address, instruction.Address);
		Assert.Equal("0000000140001000", instruction.AddressText);
		Assert.Equal("E9 FB FF FF FF", instruction.Bytes);
		Assert.Equal("jmp", instruction.Opcode);
		Assert.Equal("0000000140001000", instruction.Extra);
		Assert.Equal(49, instruction.Utf8ByteLength);
		Assert.Equal(55, requiredUtf8Bytes);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void disassembly_result_remains_valid_after_the_runtime_is_detached()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		InstructionDisassembly instruction;
		Address address = 0x0000_0001_4000_1000UL;
		using (HostScope scope = new(state))
		{
			InstallInstructionGlobals(scope.State);
			InstructionTargetProfile targetProfile = Observe(scope.State);
			Assert.Equal(InstructionOperationStatus.Success,
				InstructionDisassembler.TryDisassemble(targetProfile, address, 256, out instruction, out _));
		}

		// The runtime is detached and the Lua state closed below: every field is a copied managed value.
		LuaRuntime.Detach();
		state.Dispose();

		Assert.Equal(address, instruction.Address);
		Assert.Equal("0000000140001000", instruction.AddressText);
		Assert.Equal("E9 FB FF FF FF", instruction.Bytes);
		Assert.Equal("jmp", instruction.Opcode);
		Assert.Equal("0000000140001000", instruction.Extra);
		Assert.Equal(49, instruction.Utf8ByteLength);
	}

	[Fact]
	public void Disassemble_enforces_the_raw_text_bound_and_rejects_malformed_split_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);

		InstructionOperationStatus bounded = InstructionDisassembler.TryDisassemble(targetProfile, 0x140001000UL, 4,
			out InstructionDisassembly boundedInstruction, out int requiredUtf8Bytes);
		InstructionOperationStatus malformed = InstructionDisassembler.TryDisassemble(targetProfile, 0xBADUL, 256,
			out InstructionDisassembly malformedInstruction, out int malformedRequiredUtf8Bytes);
		InstructionOperationStatus oversizedFields = InstructionDisassembler.TryDisassemble(targetProfile, 0xBADDUL, 10,
			out InstructionDisassembly oversizedInstruction, out int oversizedRequiredUtf8Bytes);

		Assert.Equal(InstructionOperationStatus.OutputTooLong, bounded);
		Assert.Equal(default, boundedInstruction);
		Assert.Equal(55, requiredUtf8Bytes);
		Assert.Equal(InstructionOperationStatus.InvalidResult, malformed);
		Assert.Equal(default, malformedInstruction);
		Assert.Equal(9, malformedRequiredUtf8Bytes);
		Assert.Equal(InstructionOperationStatus.OutputTooLong, oversizedFields);
		Assert.Equal(default, oversizedInstruction);
		Assert.Equal(10, oversizedRequiredUtf8Bytes);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Missing_and_raising_instruction_globals_have_distinct_structured_outcomes()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using (HostScope missingScope = new(state))
		{
			InstallProfileGlobals(missingScope.State);
			InstructionTargetProfile targetProfile = Observe(missingScope.State);
			InstructionOperationStatus unavailable = InstructionDisassembler.TryDisassemble(targetProfile,
				0x140001000UL,
				256, out _, out _);

			Assert.Equal(InstructionOperationStatus.GlobalUnavailable, unavailable);
			Assert.Equal(0, missingScope.State.Top);
		}

		using HostScope raisingScope = new(state);
		InstallInstructionGlobals(raisingScope.State);
		InstructionTargetProfile raisingProfile = Observe(raisingScope.State);
		InstructionOperationStatus failure = InstructionDisassembler.TryDisassemble(raisingProfile, 0xDEADUL,
			256, out _, out _);

		Assert.Equal(InstructionOperationStatus.LuaFailure, failure);
		Assert.Equal(0, raisingScope.State.Top);
	}

	[Fact]
	public void Navigation_returns_a_positive_length_and_an_explicitly_estimated_previous_address()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile targetProfile = Observe(scope.State);

		InstructionOperationStatus lengthStatus = InstructionNavigator.TryGetLength(targetProfile, 0x140001000UL,
			out int length);
		InstructionOperationStatus previousStatus = InstructionNavigator.TryGetPrevious(targetProfile, 0x140001000UL,
			out Address previous);

		Assert.Equal(InstructionOperationStatus.Success, lengthStatus);
		Assert.Equal(5, length);
		Assert.Equal(InstructionOperationStatus.Success, previousStatus);
		Assert.Equal(0x140000FFBUL, previous.Value);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Navigation_rejects_nonpositive_or_string_lengths_and_addresses_wider_than_the_profile()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallInstructionGlobals(scope.State);
		InstructionTargetProfile x64Profile = Observe(scope.State);

		InstructionOperationStatus invalidLength =
			InstructionNavigator.TryGetLength(x64Profile, 0UL, out int zeroLength);
		InstructionOperationStatus stringLength =
			InstructionNavigator.TryGetLength(x64Profile, 1UL, out int coercedLength);

		EngineTest.Run(scope.State, "instruction_target_is_64bit = false\ninstruction_target_is_x86 = true"u8);
		InstructionTargetProfile x86Profile = Observe(scope.State);
		InstructionOperationStatus overwidePrevious = InstructionNavigator.TryGetPrevious(x86Profile, 0x1000UL,
			out Address previous);

		Assert.Equal(InstructionOperationStatus.InvalidResult, invalidLength);
		Assert.Equal(0, zeroLength);
		Assert.Equal(InstructionOperationStatus.InvalidResult, stringLength);
		Assert.Equal(0, coercedLength);
		Assert.Equal(InstructionOperationStatus.AddressExceedsProfileWidth, overwidePrevious);
		Assert.Equal(Address.Zero, previous);
		Assert.Equal(0, scope.State.Top);
	}

	private static InstructionTargetProfile Observe(LuaState state)
	{
		InstructionOperationStatus status =
			InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile targetProfile);
		Assert.Equal(InstructionOperationStatus.Success, status);
		Assert.Equal(0, state.Top);
		return targetProfile;
	}

	private static void InstallInstructionGlobals(LuaState state)
	{
		InstallProfileGlobals(state);
		EngineTest.Run(state, """
		                      instruction_assemble_calls = 0
		                      instruction_assemble_origin = 0
		                      instruction_assemble_origin_type = "none"
		                      instruction_assemble_behavior = "normal"
		                      instruction_assemble_next_process_id = 7777

		                      assemble = function(line, address)
		                        instruction_assemble_calls = instruction_assemble_calls + 1
		                        instruction_assemble_origin = address
		                        instruction_assemble_origin_type = math.type(address)
		                        if instruction_assemble_behavior == "target-change" then
		                          instruction_target_process_id = instruction_assemble_next_process_id
		                          return { 0x90 }, nil
		                        end
		                        if line == "reject" then return nil, "instruction rejected" end
		                        if line == "malformed" then return { 0x90, 300 }, nil end
		                        if line == "raise" then error("assembly failure") end
		                        return { 0xE9, 0xFB, 0xFF, 0xFF, 0xFF }, nil
		                      end

		                      disassemble = function(address)
		                        if address == 0xDEAD then error("disassembly failure") end
		                        if address == 0xBAD then return "bad-split" end
		                        if address == 0xBADD then return "wide-split" end
		                        return "0000000140001000  E9 FB FF FF FF  jmp  0000000140001000"
		                      end

		                      splitDisassembledString = function(line)
		                        if line == "bad-split" then return "only-one" end
		                        if line == "wide-split" then return "aaaa", "bbbb", "cccc", "dddd" end
		                        return "0000000140001000", "E9 FB FF FF FF", "jmp", "0000000140001000"
		                      end

		                      getInstructionSize = function(address)
		                        if address == 0 then return 0 end
		                        if address == 1 then return "5" end
		                        return 5
		                      end

		                      getPreviousOpcode = function(address)
		                        if address == 0x1000 then return 0x100000000 end
		                        return address - 5
		                      end
		                      """u8);
	}

	private static void InstallProfileGlobals(LuaState state)
	{
		EngineTest.Run(state, """
		                      -- The CE-faithful x64 target (spike C3 D2): x86 family and 64-bit.
		                      instruction_target_process_id = 4242
		                      instruction_target_is_64bit = true
		                      instruction_target_is_x86 = true
		                      instruction_target_is_arm = false
		                      function getOpenedProcessID() return instruction_target_process_id end
		                      function targetIs64Bit() return instruction_target_is_64bit end
		                      function targetIsX86() return instruction_target_is_x86 end
		                      function targetIsArm() return instruction_target_is_arm end
		                      """u8);
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

	private static string LuaBoolean(bool value)
	{
		return value ? "true" : "false";
	}
}
