using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.SDK.Engine.Tests.Runtime;

/// <summary>Pure managed contract tests for explicit Cheat Engine runtime facts and optional capabilities.</summary>
public sealed class RuntimeContractsTests
{
    [Fact]
    public void CheatEngineVersion_exact_components_compare_and_format_without_precision_loss()
    {
        var version = new CheatEngineVersion(7, 7, 0, 10621);
        var olderBuild = new CheatEngineVersion(7, 7, 0, 10620);
        var laterRelease = new CheatEngineVersion(7, 7, 1, 0);

        Assert.Equal(CheatEngineVersion.Ce77010621, version);
        Assert.Equal(7, version.Major);
        Assert.Equal(7, version.Minor);
        Assert.Equal(0, version.Release);
        Assert.Equal(10621, version.Build);
        Assert.Equal("7.7.0.10621", version.ToString());
        Assert.True(version > olderBuild);
        Assert.True(version < laterRelease);
        Assert.True(version >= CheatEngineVersion.Ce77010621);
        Assert.True(version <= CheatEngineVersion.Ce77010621);
        Assert.NotEqual(version, olderBuild);
        Assert.Equal(version.GetHashCode(), CheatEngineVersion.Ce77010621.GetHashCode());
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void CheatEngineVersion_negative_component_is_rejected(int major, int minor, int release, int build)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CheatEngineVersion(major, minor, release, build));
    }

    [Theory]
    [InlineData(0, CheatEngineArchitecture.X86)]
    [InlineData(1, CheatEngineArchitecture.X64)]
    [InlineData(2, CheatEngineArchitecture.Arm32)]
    [InlineData(3, CheatEngineArchitecture.Arm64)]
    public void TryDecodeSystemArchitecture_known_CE77_codes_decode(int code, CheatEngineArchitecture expected)
    {
        Assert.True(RuntimeInfo.TryDecodeSystemArchitecture(code, out var architecture));
        Assert.Equal(expected, architecture);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void TryDecodeSystemArchitecture_unknown_code_is_rejected(int code)
    {
        Assert.False(RuntimeInfo.TryDecodeSystemArchitecture(code, out var architecture));
        Assert.Equal(CheatEngineArchitecture.Unknown, architecture);
    }

    [Theory]
    [InlineData(0, TargetAbi.Windows)]
    [InlineData(1, TargetAbi.Unix)]
    public void TryDecodeTargetAbi_known_CE77_codes_decode(int code, TargetAbi expected)
    {
        Assert.True(RuntimeInfo.TryDecodeTargetAbi(code, out var abi));
        Assert.Equal(expected, abi);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void TryDecodeTargetAbi_unknown_code_is_rejected(int code)
    {
        Assert.False(RuntimeInfo.TryDecodeTargetAbi(code, out var abi));
        Assert.Equal(TargetAbi.Unknown, abi);
    }

    [Fact]
    public void PointerSize_known_widths_expose_bits_and_reject_other_widths()
    {
        Assert.Equal(4, PointerSize.Bit32.Bytes);
        Assert.Equal(32, PointerSize.Bit32.Bits);
        Assert.Equal(8, PointerSize.Bit64.Bytes);
        Assert.Equal(64, PointerSize.Bit64.Bits);
        Assert.True(PointerSize.Bit64.IsKnown);
        Assert.False(PointerSize.Unknown.IsKnown);
        Assert.Equal(0, PointerSize.Unknown.Bits);
        Assert.Equal(PointerSize.Bit32, PointerSize.FromArchitecture(CheatEngineArchitecture.X86));
        Assert.Equal(PointerSize.Bit32, PointerSize.FromArchitecture(CheatEngineArchitecture.Arm32));
        Assert.Equal(PointerSize.Bit64, PointerSize.FromArchitecture(CheatEngineArchitecture.X64));
        Assert.Equal(PointerSize.Bit64, PointerSize.FromArchitecture(CheatEngineArchitecture.Arm64));
        Assert.Equal(PointerSize.Unknown, PointerSize.FromArchitecture(CheatEngineArchitecture.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointerSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointerSize(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointerSize(16));
    }

    [Fact]
    public void PointerSize_little_endian_primitives_use_the_explicit_target_width_and_preserve_destinations_on_rejection()
    {
        ReadOnlySpan<byte> bytes = [0x98, 0xBA, 0xDC, 0xFE, 0xEF, 0xCD, 0xAB, 0x89];
        Span<byte> narrow = stackalloc byte[4];
        Span<byte> wide = stackalloc byte[8];
        Span<byte> untouched = stackalloc byte[3];
        untouched.Fill(0xA5);

        Assert.True(PointerSize.Bit32.TryReadLittleEndian(bytes[..4], out var x86Pointer));
        Assert.Equal(0xFEDCBA98UL, x86Pointer);
        Assert.True(PointerSize.Bit64.TryReadLittleEndian(bytes, out var x64Pointer));
        Assert.Equal(0x89ABCDEF_FEDCBA98UL, x64Pointer);
        Assert.False(PointerSize.Bit32.TryReadLittleEndian(bytes, out _));
        Assert.False(PointerSize.Bit64.TryReadLittleEndian(bytes[..7], out _));
        Assert.False(PointerSize.Unknown.TryReadLittleEndian(ReadOnlySpan<byte>.Empty, out _));

        Assert.True(PointerSize.Bit32.TryWriteLittleEndian(0xFEDCBA98UL, narrow));
        Assert.True(narrow.SequenceEqual(bytes[..4]));
        Assert.True(PointerSize.Bit64.TryWriteLittleEndian(0x89ABCDEF_FEDCBA98UL, wide));
        Assert.True(wide.SequenceEqual(bytes));
        Assert.False(PointerSize.Bit32.TryWriteLittleEndian(0x1_0000_0000UL, narrow));
        Assert.False(PointerSize.Bit32.TryWriteLittleEndian(0x1234UL, untouched));
        Assert.True(untouched.SequenceEqual(new byte[] { 0xA5, 0xA5, 0xA5 }));
    }

    [Fact]
    public void RuntimeCapabilities_creation_copies_entries_and_preserves_full_contract_metadata()
    {
        var contract = new RuntimeCapabilityContract(
            CheatEngineVersion.Ce77010621,
            RuntimeArchitectureScope.Target,
            RuntimeArchitectureRequirement.X64,
            RuntimeThreadRequirement.MainThread,
            RuntimeOwnership.Borrowed,
            RuntimeReturnSemantics.OptionalValue);
        RuntimeCapabilityAvailability[] source =
        [
            new(RuntimeCapabilityId.TargetArchitecture, RuntimeCapabilityAvailabilityState.Available, contract),
            new(RuntimeCapabilityId.TargetAbi, RuntimeCapabilityAvailabilityState.Unavailable,
                RuntimeCapabilityContract.Unknown),
        ];

        var capabilities = RuntimeCapabilities.Create(source);
        source[0] = new RuntimeCapabilityAvailability(RuntimeCapabilityId.TargetArchitecture,
            RuntimeCapabilityAvailabilityState.Unknown, RuntimeCapabilityContract.Unknown);

        Assert.Equal(2, capabilities.Count);
        Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
            capabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
        Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
            capabilities.GetState(RuntimeCapabilityId.SystemArchitecture));
        Assert.True(capabilities.TryGet(RuntimeCapabilityId.TargetArchitecture, out var availability));
        Assert.True(availability.IsAvailable);
        Assert.True(availability.IsKnown);
        Assert.Equal(contract, availability.Contract);
        Assert.Equal(CheatEngineVersion.Ce77010621, availability.Contract.MinimumCheatEngineVersion);
        Assert.Equal(RuntimeArchitectureScope.Target, availability.Contract.ArchitectureScope);
        Assert.Equal(RuntimeArchitectureRequirement.X64, availability.Contract.ArchitectureRequirement);
        Assert.Equal(RuntimeThreadRequirement.MainThread, availability.Contract.ThreadRequirement);
        Assert.Equal(RuntimeOwnership.Borrowed, availability.Contract.Ownership);
        Assert.Equal(RuntimeReturnSemantics.OptionalValue, availability.Contract.ReturnSemantics);
        Assert.Equal(RuntimeCapabilityAvailabilityState.Unavailable, capabilities.Entries[1].State);
        Assert.False(capabilities.TryGet(RuntimeCapabilityId.CheatEngineVersion, out _));
    }

    [Fact]
    public void RuntimeCapabilities_empty_or_duplicate_identifiers_are_rejected()
    {
        RuntimeCapabilityAvailability[] emptyIdentifier =
            [new(default, RuntimeCapabilityAvailabilityState.Unknown, RuntimeCapabilityContract.Unknown)];
        RuntimeCapabilityAvailability[] duplicateIdentifier =
        [
            new(RuntimeCapabilityId.TargetAbi, RuntimeCapabilityAvailabilityState.Available,
                RuntimeCapabilityContract.Unknown),
            new(RuntimeCapabilityId.TargetAbi, RuntimeCapabilityAvailabilityState.Unavailable,
                RuntimeCapabilityContract.Unknown),
        ];

        Assert.Throws<ArgumentException>(() => RuntimeCapabilities.Create(emptyIdentifier));
        Assert.Throws<ArgumentException>(() => RuntimeCapabilities.Create(duplicateIdentifier));
        Assert.Throws<ArgumentException>(() => new RuntimeCapabilityId(" "));
    }

    [Fact]
    public void RuntimeInfo_constructor_preserves_explicit_runtime_facts_without_normalization()
    {
        var capabilities = RuntimeCapabilities.Create(
        [
            new RuntimeCapabilityAvailability(RuntimeCapabilityId.SystemArchitecture,
                RuntimeCapabilityAvailabilityState.Available, RuntimeCapabilityContract.Unknown),
        ]);
        var info = new RuntimeInfo(
            CheatEngineVersion.Ce77010621,
            CheatEngineArchitecture.X64,
            CheatEngineArchitecture.Unknown,
            PointerSize.Unknown,
            TargetAbi.Unknown,
            capabilities);

        Assert.Equal(CheatEngineVersion.Ce77010621, info.Version);
        Assert.Equal(CheatEngineArchitecture.X64, info.SystemArchitecture);
        Assert.Equal(CheatEngineArchitecture.Unknown, info.TargetArchitecture);
        Assert.Equal(PointerSize.Unknown, info.PointerSize);
        Assert.Equal(TargetAbi.Unknown, info.TargetAbi);
        Assert.Same(capabilities, info.Capabilities);
        Assert.Throws<ArgumentNullException>(() => new RuntimeInfo(
            CheatEngineVersion.Ce77010621,
            CheatEngineArchitecture.X64,
            CheatEngineArchitecture.Unknown,
            PointerSize.Unknown,
            TargetAbi.Unknown,
            null!));
    }
}
