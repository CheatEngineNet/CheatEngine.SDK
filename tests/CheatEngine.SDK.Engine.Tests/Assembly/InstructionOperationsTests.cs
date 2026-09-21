using System;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
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
    public void ObserveCurrent_builds_an_x64_profile_from_coherent_target_probes_and_rejects_a_contradiction()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallInstructionGlobals(scope.State);

        InstructionOperationStatus observed = InstructionProfiles.TryObserveCurrent(out var targetProfile);

        Assert.Equal(InstructionOperationStatus.Success, observed);
        Assert.Equal(4242, targetProfile.Target.Value);
        Assert.Equal(CheatEngineArchitecture.X64, targetProfile.Profile.Architecture);
        Assert.Equal(PointerSize.Bit64, targetProfile.Profile.AddressWidth);

        EngineTest.Run(scope.State, "instruction_target_is_x86 = true\ninstruction_target_is_64bit = true"u8);
        InstructionOperationStatus contradictory = InstructionProfiles.TryObserveCurrent(out var rejectedProfile);

        Assert.Equal(InstructionOperationStatus.InvalidProfile, contradictory);
        Assert.Equal(default, rejectedProfile);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ObserveCurrent_uses_the_target_ARM_probe_and_matching_width_without_using_the_host_width()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallInstructionGlobals(scope.State);

        EngineTest.Run(scope.State, "instruction_target_is_64bit = false\ninstruction_target_is_arm = true"u8);
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
            destination, out var written, out var requiredLength);

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
            rejectedDestination, out var rejectedWritten, out var rejectedRequired);

        Assert.Equal(InstructionOperationStatus.InstructionRejected, rejected);
        Assert.Equal(0, rejectedWritten);
        Assert.Equal(0, rejectedRequired);
        Assert.Equal(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5, 0xA5 }, rejectedDestination.ToArray());

        Span<byte> tooSmallDestination = stackalloc byte[4];
        tooSmallDestination.Fill(0xA5);
        InstructionOperationStatus tooSmall = InstructionAssembler.TryAssemble(targetProfile, "jmp rel", 0x140001000UL,
            tooSmallDestination, out var tooSmallWritten, out var tooSmallRequired);

        Assert.Equal(InstructionOperationStatus.DestinationTooSmall, tooSmall);
        Assert.Equal(0, tooSmallWritten);
        Assert.Equal(5, tooSmallRequired);
        Assert.Equal(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5 }, tooSmallDestination.ToArray());

        Span<byte> malformedDestination = stackalloc byte[2];
        malformedDestination.Fill(0xA5);
        InstructionOperationStatus malformed = InstructionAssembler.TryAssemble(targetProfile, "malformed", 0x140001000UL,
            malformedDestination, out var malformedWritten, out var malformedRequired);

        Assert.Equal(InstructionOperationStatus.InvalidResult, malformed);
        Assert.Equal(0, malformedWritten);
        Assert.Equal(0, malformedRequired);
        Assert.Equal(new byte[] { 0xA5, 0xA5 }, malformedDestination.ToArray());
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Assemble_rejects_an_address_wider_than_the_observed_x86_profile_before_entering_Lua()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallInstructionGlobals(scope.State);
        EngineTest.Run(scope.State, "instruction_target_is_64bit = false\ninstruction_target_is_x86 = true"u8);
        InstructionTargetProfile targetProfile = Observe(scope.State);
        Span<byte> destination = stackalloc byte[5];

        InstructionOperationStatus overwide = InstructionAssembler.TryAssemble(targetProfile, "jmp rel", 0x1_0000_0000UL,
            destination, out var written, out var requiredLength);

        Assert.Equal(InstructionOperationStatus.AddressExceedsProfileWidth, overwide);
        Assert.Equal(0, written);
        Assert.Equal(0, requiredLength);
        Assert.Equal(0, ReadInteger(scope.State, "instruction_assemble_calls"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Assemble_reports_a_target_change_without_copying_the_returned_bytes()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallInstructionGlobals(scope.State);
        InstructionTargetProfile targetProfile = Observe(scope.State);
        EngineTest.Run(scope.State, "instruction_assemble_behavior = \"target-change\""u8);
        Span<byte> destination = stackalloc byte[1];
        destination[0] = 0xA5;

        InstructionOperationStatus status = InstructionAssembler.TryAssemble(targetProfile, "nop", 0x140001000UL,
            destination, out var written, out var requiredLength);

        Assert.Equal(InstructionOperationStatus.TargetChanged, status);
        Assert.Equal(0, written);
        Assert.Equal(0, requiredLength);
        Assert.Equal(0xA5, destination[0]);
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
            out var instruction, out var requiredUtf8Bytes);

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
    public void Disassemble_enforces_the_raw_text_bound_and_rejects_malformed_split_results()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallInstructionGlobals(scope.State);
        InstructionTargetProfile targetProfile = Observe(scope.State);

        InstructionOperationStatus bounded = InstructionDisassembler.TryDisassemble(targetProfile, 0x140001000UL, 4,
            out var boundedInstruction, out var requiredUtf8Bytes);
        InstructionOperationStatus malformed = InstructionDisassembler.TryDisassemble(targetProfile, 0xBADUL, 256,
            out var malformedInstruction, out var malformedRequiredUtf8Bytes);
        InstructionOperationStatus oversizedFields = InstructionDisassembler.TryDisassemble(targetProfile, 0xBADDUL, 10,
            out var oversizedInstruction, out var oversizedRequiredUtf8Bytes);

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
            InstructionOperationStatus unavailable = InstructionDisassembler.TryDisassemble(targetProfile, 0x140001000UL,
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
            out var length);
        InstructionOperationStatus previousStatus = InstructionNavigator.TryGetPrevious(targetProfile, 0x140001000UL,
            out var previous);

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

        InstructionOperationStatus invalidLength = InstructionNavigator.TryGetLength(x64Profile, 0UL, out var zeroLength);
        InstructionOperationStatus stringLength = InstructionNavigator.TryGetLength(x64Profile, 1UL, out var coercedLength);

        EngineTest.Run(scope.State, "instruction_target_is_64bit = false\ninstruction_target_is_x86 = true"u8);
        InstructionTargetProfile x86Profile = Observe(scope.State);
        InstructionOperationStatus overwidePrevious = InstructionNavigator.TryGetPrevious(x86Profile, 0x1000UL,
            out var previous);

        Assert.Equal(InstructionOperationStatus.InvalidResult, invalidLength);
        Assert.Equal(0, zeroLength);
        Assert.Equal(InstructionOperationStatus.InvalidResult, stringLength);
        Assert.Equal(0, coercedLength);
        Assert.Equal(InstructionOperationStatus.AddressExceedsProfileWidth, overwidePrevious);
        Assert.Equal(Address.Zero, previous);
        Assert.Equal(0, scope.State.Top);
    }

    private static InstructionTargetProfile Observe(CheatEngine.SDK.Lua.State.LuaState state)
    {
        InstructionOperationStatus status = InstructionProfiles.TryObserveCurrent(out var targetProfile);
        Assert.Equal(InstructionOperationStatus.Success, status);
        Assert.Equal(0, state.Top);
        return targetProfile;
    }

    private static void InstallInstructionGlobals(CheatEngine.SDK.Lua.State.LuaState state)
    {
        InstallProfileGlobals(state);
        EngineTest.Run(state, """
                              instruction_assemble_calls = 0
                              instruction_assemble_origin = 0
                              instruction_assemble_behavior = "normal"

                              assemble = function(line, address)
                                instruction_assemble_calls = instruction_assemble_calls + 1
                                instruction_assemble_origin = address
                                if instruction_assemble_behavior == "target-change" then
                                  instruction_target_process_id = 7777
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

    private static void InstallProfileGlobals(CheatEngine.SDK.Lua.State.LuaState state)
    {
        EngineTest.Run(state, """
                              instruction_target_process_id = 4242
                              instruction_target_is_64bit = true
                              instruction_target_is_x86 = false
                              instruction_target_is_arm = false
                              function getOpenedProcessID() return instruction_target_process_id end
                              function targetIs64Bit() return instruction_target_is_64bit end
                              function targetIsX86() return instruction_target_is_x86 end
                              function targetIsArm() return instruction_target_is_arm end
                              """u8);
    }

    private static long ReadInteger(CheatEngine.SDK.Lua.State.LuaState state, string name)
    {
        using var frame = new CheatEngine.SDK.Lua.State.LuaFrame(state);
        Assert.True(state.TryGetGlobal(System.Text.Encoding.UTF8.GetBytes(name)).IsOk);
        return EngineTest.ReadInteger(state, -1);
    }
}
