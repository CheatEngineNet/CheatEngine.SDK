using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.SDK.Engine.Tests.Enums;

/// <summary>
///     Every member pinned against the number read in <c>defines.lua</c> of Cheat Engine 7.7.0.10621, and every
///     underlying type explicit. A change here is a change of the interface with Cheat Engine.
/// </summary>
public sealed class EnumValueTests
{
	[Theory]
	[InlineData(VariableType.Byte, 0)]
	[InlineData(VariableType.Word, 1)]
	[InlineData(VariableType.Dword, 2)]
	[InlineData(VariableType.Qword, 3)]
	[InlineData(VariableType.Single, 4)]
	[InlineData(VariableType.Double, 5)]
	[InlineData(VariableType.String, 6)]
	[InlineData(VariableType.WideString, 7)]
	[InlineData(VariableType.ByteArray, 8)]
	[InlineData(VariableType.Binary, 9)]
	[InlineData(VariableType.All, 10)]
	[InlineData(VariableType.AutoAssembler, 11)]
	[InlineData(VariableType.Pointer, 12)]
	[InlineData(VariableType.Custom, 13)]
	[InlineData(VariableType.Grouped, 14)]
	public void VariableType_values_match_defines_lua(VariableType member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(ScanOption.UnknownValue, 0)]
	[InlineData(ScanOption.ExactValue, 1)]
	[InlineData(ScanOption.ValueBetween, 2)]
	[InlineData(ScanOption.BiggerThan, 3)]
	[InlineData(ScanOption.SmallerThan, 4)]
	[InlineData(ScanOption.IncreasedValue, 5)]
	[InlineData(ScanOption.IncreasedValueBy, 6)]
	[InlineData(ScanOption.DecreasedValue, 7)]
	[InlineData(ScanOption.DecreasedValueBy, 8)]
	[InlineData(ScanOption.Changed, 9)]
	[InlineData(ScanOption.Unchanged, 10)]
	public void ScanOption_values_match_defines_lua(ScanOption member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(RoundingType.Rounded, 0)]
	[InlineData(RoundingType.ExtremeRounded, 1)]
	[InlineData(RoundingType.Truncated, 2)]
	public void RoundingType_values_match_defines_lua(RoundingType member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(FastScanMethod.NotAligned, 0)]
	[InlineData(FastScanMethod.Aligned, 1)]
	[InlineData(FastScanMethod.LastDigits, 2)]
	public void FastScanMethod_values_match_defines_lua(FastScanMethod member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(BreakpointMethod.Int3, 0)]
	[InlineData(BreakpointMethod.DebugRegister, 1)]
	[InlineData(BreakpointMethod.Exception, 2)]
	public void BreakpointMethod_values_match_defines_lua(BreakpointMethod member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(BreakpointTrigger.Execute, 0)]
	[InlineData(BreakpointTrigger.Access, 1)]
	[InlineData(BreakpointTrigger.Write, 2)]
	public void BreakpointTrigger_values_match_defines_lua(BreakpointTrigger member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(ContinueMethod.Run, 0)]
	[InlineData(ContinueMethod.StepInto, 1)]
	[InlineData(ContinueMethod.StepOver, 2)]
	public void ContinueMethod_values_match_defines_lua(ContinueMethod member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(MemoryProtection.None, 0u)]
	[InlineData(MemoryProtection.ReadOnly, 2u)]
	[InlineData(MemoryProtection.ReadWrite, 4u)]
	[InlineData(MemoryProtection.WriteCopy, 8u)]
	[InlineData(MemoryProtection.Execute, 16u)]
	[InlineData(MemoryProtection.ExecuteRead, 32u)]
	[InlineData(MemoryProtection.ExecuteReadWrite, 64u)]
	[InlineData(MemoryProtection.ExecuteWriteCopy, 128u)]
	public void MemoryProtection_values_match_defines_lua(MemoryProtection member, uint expected)
	{
		Assert.Equal(expected, (uint) member);
	}

	[Theory]
	[InlineData(DuplicateHandling.Ignore, 0)]
	[InlineData(DuplicateHandling.Accept, 1)]
	[InlineData(DuplicateHandling.Error, 2)]
	public void DuplicateHandling_values_match_defines_lua(DuplicateHandling member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Theory]
	[InlineData(typeof(VariableType), typeof(int), 15)]
	[InlineData(typeof(ScanOption), typeof(int), 11)]
	[InlineData(typeof(RoundingType), typeof(int), 3)]
	[InlineData(typeof(FastScanMethod), typeof(int), 3)]
	[InlineData(typeof(BreakpointMethod), typeof(int), 3)]
	[InlineData(typeof(BreakpointTrigger), typeof(int), 3)]
	[InlineData(typeof(ContinueMethod), typeof(int), 3)]
	[InlineData(typeof(MemoryProtection), typeof(uint), 8)]
	[InlineData(typeof(DuplicateHandling), typeof(int), 3)]
	public void Underlying_types_and_member_counts_are_as_declared(Type enumType, Type underlying, int memberCount)
	{
		Assert.Equal(underlying, Enum.GetUnderlyingType(enumType));
		Assert.Equal(memberCount, Enum.GetNames(enumType).Length);
	}

	[Fact]
	public void MemoryProtection_is_the_only_flags_enum()
	{
		Assert.True(typeof(MemoryProtection).IsDefined(typeof(FlagsAttribute), false));
		Assert.False(typeof(VariableType).IsDefined(typeof(FlagsAttribute), false));
		Assert.False(typeof(ScanOption).IsDefined(typeof(FlagsAttribute), false));
		Assert.Equal(MemoryProtection.ReadWrite | MemoryProtection.Execute, (MemoryProtection) 20u);
	}
}
