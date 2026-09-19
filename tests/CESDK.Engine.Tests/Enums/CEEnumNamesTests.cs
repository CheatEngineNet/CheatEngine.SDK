using System.Text;
using CESDK.Engine.Enums;
using CESDK.Engine.Tests.Support;

namespace CESDK.Engine.Tests.Enums;

/// <summary>The CE name of every member, pinned to the identifier in <c>defines.lua</c>, and the reverse lookup.</summary>
public sealed class CEEnumNamesTests
{
    [Theory]
    [InlineData(VariableType.Byte, "vtByte")]
    [InlineData(VariableType.Word, "vtWord")]
    [InlineData(VariableType.Dword, "vtDword")]
    [InlineData(VariableType.Qword, "vtQword")]
    [InlineData(VariableType.Single, "vtSingle")]
    [InlineData(VariableType.Double, "vtDouble")]
    [InlineData(VariableType.String, "vtString")]
    [InlineData(VariableType.WideString, "vtWideString")]
    [InlineData(VariableType.ByteArray, "vtByteArray")]
    [InlineData(VariableType.Binary, "vtBinary")]
    [InlineData(VariableType.All, "vtAll")]
    [InlineData(VariableType.AutoAssembler, "vtAutoAssembler")]
    [InlineData(VariableType.Pointer, "vtPointer")]
    [InlineData(VariableType.Custom, "vtCustom")]
    [InlineData(VariableType.Grouped, "vtGrouped")]
    public void VariableType_names_round_trip(VariableType member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out VariableType parsed));
        Assert.Equal(member, parsed);
    }

    [Fact]
    public void VariableType_accepts_the_documented_alias_and_nothing_else()
    {
        Assert.True(CEEnumNames.TryParseCEName("vtUnicodeString"u8, out VariableType alias));
        Assert.Equal(VariableType.WideString, alias);
        Assert.False(CEEnumNames.TryParseCEName("vtdword"u8, out VariableType wrongCase));
        Assert.Equal(default, wrongCase);
        Assert.False(CEEnumNames.TryParseCEName("Dword"u8, out VariableType _));
        Assert.False(CEEnumNames.TryParseCEName(""u8, out VariableType _));
        Assert.False(CEEnumNames.TryParseCEName("vtDword "u8, out VariableType _));
        Assert.True(((VariableType)99).ToCEName().IsEmpty);
    }

    [Theory]
    [InlineData(ScanOption.UnknownValue, "soUnknownValue")]
    [InlineData(ScanOption.ExactValue, "soExactValue")]
    [InlineData(ScanOption.ValueBetween, "soValueBetween")]
    [InlineData(ScanOption.BiggerThan, "soBiggerThan")]
    [InlineData(ScanOption.SmallerThan, "soSmallerThan")]
    [InlineData(ScanOption.IncreasedValue, "soIncreasedValue")]
    [InlineData(ScanOption.IncreasedValueBy, "soIncreasedValueBy")]
    [InlineData(ScanOption.DecreasedValue, "soDecreasedValue")]
    [InlineData(ScanOption.DecreasedValueBy, "soDecreasedValueBy")]
    [InlineData(ScanOption.Changed, "soChanged")]
    [InlineData(ScanOption.Unchanged, "soUnchanged")]
    public void ScanOption_names_round_trip(ScanOption member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out ScanOption parsed));
        Assert.Equal(member, parsed);
    }

    [Theory]
    [InlineData(RoundingType.Rounded, "rtRounded")]
    [InlineData(RoundingType.ExtremeRounded, "rtExtremerounded")]
    [InlineData(RoundingType.Truncated, "rtTruncated")]
    public void RoundingType_names_round_trip(RoundingType member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out RoundingType parsed));
        Assert.Equal(member, parsed);
    }

    [Theory]
    [InlineData(FastScanMethod.NotAligned, "fsmNotAligned")]
    [InlineData(FastScanMethod.Aligned, "fsmAligned")]
    [InlineData(FastScanMethod.LastDigits, "fsmLastDigits")]
    public void FastScanMethod_names_round_trip(FastScanMethod member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out FastScanMethod parsed));
        Assert.Equal(member, parsed);
    }

    [Theory]
    [InlineData(BreakpointMethod.Int3, "bpmInt3")]
    [InlineData(BreakpointMethod.DebugRegister, "bpmDebugRegister")]
    [InlineData(BreakpointMethod.Exception, "bpmException")]
    public void BreakpointMethod_names_round_trip(BreakpointMethod member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out BreakpointMethod parsed));
        Assert.Equal(member, parsed);
    }

    [Theory]
    [InlineData(BreakpointTrigger.Execute, "bptExecute")]
    [InlineData(BreakpointTrigger.Access, "bptAccess")]
    [InlineData(BreakpointTrigger.Write, "bptWrite")]
    public void BreakpointTrigger_names_round_trip(BreakpointTrigger member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out BreakpointTrigger parsed));
        Assert.Equal(member, parsed);
    }

    [Theory]
    [InlineData(ContinueMethod.Run, "co_run")]
    [InlineData(ContinueMethod.StepInto, "co_stepinto")]
    [InlineData(ContinueMethod.StepOver, "co_stepover")]
    public void ContinueMethod_names_round_trip(ContinueMethod member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out ContinueMethod parsed));
        Assert.Equal(member, parsed);
    }

    [Theory]
    [InlineData(MemoryProtection.ReadOnly, "PAGE_READONLY")]
    [InlineData(MemoryProtection.ReadWrite, "PAGE_READWRITE")]
    [InlineData(MemoryProtection.WriteCopy, "PAGE_WRITECOPY")]
    [InlineData(MemoryProtection.Execute, "PAGE_EXECUTE")]
    [InlineData(MemoryProtection.ExecuteRead, "PAGE_EXECUTE_READ")]
    [InlineData(MemoryProtection.ExecuteReadWrite, "PAGE_EXECUTE_READWRITE")]
    [InlineData(MemoryProtection.ExecuteWriteCopy, "PAGE_EXECUTE_WRITECOPY")]
    public void MemoryProtection_names_round_trip(MemoryProtection member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out MemoryProtection parsed));
        Assert.Equal(member, parsed);
    }

    [Fact]
    public void MemoryProtection_combinations_and_None_have_no_CE_name()
    {
        Assert.True(MemoryProtection.None.ToCEName().IsEmpty);
        Assert.True((MemoryProtection.ReadWrite | MemoryProtection.Execute).ToCEName().IsEmpty);
        Assert.False(CEEnumNames.TryParseCEName("PAGE_NOACCESS"u8, out MemoryProtection notListed));
        Assert.Equal(MemoryProtection.None, notListed);
    }

    [Theory]
    [InlineData(DuplicateHandling.Ignore, "dupIgnore")]
    [InlineData(DuplicateHandling.Accept, "dupAccept")]
    [InlineData(DuplicateHandling.Error, "dupError")]
    public void DuplicateHandling_names_round_trip(DuplicateHandling member, string ceName)
    {
        Assert.Equal(ceName, Encoding.UTF8.GetString(member.ToCEName()));
        Assert.True(CEEnumNames.TryParseCEName(Encoding.UTF8.GetBytes(ceName), out DuplicateHandling parsed));
        Assert.Equal(member, parsed);
    }

    [Fact]
    public void Every_defined_member_of_every_enum_has_a_name_that_parses_back()
    {
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out VariableType v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out ScanOption v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out RoundingType v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out FastScanMethod v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out BreakpointMethod v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out BreakpointTrigger v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out ContinueMethod v) => CEEnumNames.TryParseCEName(n, out v));
        AssertAllNamed(static v => v.ToCEName(),
            static (ReadOnlySpan<byte> n, out DuplicateHandling v) => CEEnumNames.TryParseCEName(n, out v));
    }

    [Fact]
    public void Name_lookup_allocates_nothing()
    {
        var sink = 0;

        AllocationGate.AssertZero(() =>
        {
            sink += VariableType.Dword.ToCEName().Length;
            if (CEEnumNames.TryParseCEName("soExactValue"u8, out ScanOption option)) sink += (int)option;
        });

        Assert.NotEqual(0, sink);
    }

    private static void AssertAllNamed<TEnum>(NameOf<TEnum> nameOf, ParseName<TEnum> parse)
        where TEnum : struct, Enum
    {
        foreach (var member in Enum.GetValues<TEnum>())
        {
            var name = nameOf(member);
            Assert.False(name.IsEmpty, member + " has no CE name.");
            Assert.True(parse(name, out var parsed), member + " does not parse back.");
            Assert.Equal(member, parsed);
        }
    }

    private delegate ReadOnlySpan<byte> NameOf<TEnum>(TEnum value);

    private delegate bool ParseName<TEnum>(ReadOnlySpan<byte> name, out TEnum value);
}
