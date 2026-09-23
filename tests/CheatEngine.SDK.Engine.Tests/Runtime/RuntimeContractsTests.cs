using System.Reflection;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.SDK.Engine.Tests.Runtime;

/// <summary>Pure managed contract tests for explicit Cheat Engine runtime facts and optional capabilities.</summary>
public sealed class RuntimeContractsTests
{
	[Fact]
	public void CheatEngineVersion_exact_components_compare_and_format_without_precision_loss()
	{
		CheatEngineVersion version = new(7, 7, 0, 10621);
		CheatEngineVersion olderBuild = new(7, 7, 0, 10620);
		CheatEngineVersion laterRelease = new(7, 7, 1, 0);

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
		Assert.True(RuntimeInfo.TryDecodeSystemArchitecture(code, out CheatEngineArchitecture architecture));
		Assert.Equal(expected, architecture);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(4)]
	[InlineData(99)]
	public void TryDecodeSystemArchitecture_unknown_code_is_rejected(int code)
	{
		Assert.False(RuntimeInfo.TryDecodeSystemArchitecture(code, out CheatEngineArchitecture architecture));
		Assert.Equal(CheatEngineArchitecture.Unknown, architecture);
	}

	[Theory]
	[InlineData(0, TargetAbi.Windows)]
	[InlineData(1, TargetAbi.Unix)]
	public void TryDecodeTargetAbi_known_CE77_codes_decode(int code, TargetAbi expected)
	{
		Assert.True(RuntimeInfo.TryDecodeTargetAbi(code, out TargetAbi abi));
		Assert.Equal(expected, abi);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(2)]
	public void TryDecodeTargetAbi_unknown_code_is_rejected(int code)
	{
		Assert.False(RuntimeInfo.TryDecodeTargetAbi(code, out TargetAbi abi));
		Assert.Equal(TargetAbi.Unknown, abi);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(true, false, true, true, CheatEngineArchitecture.X64)]
	[InlineData(true, false, false, true, CheatEngineArchitecture.X86)]
	[InlineData(false, true, true, true, CheatEngineArchitecture.Arm64)]
	[InlineData(false, true, false, true, CheatEngineArchitecture.Arm32)]
	[InlineData(true, true, true, false, CheatEngineArchitecture.Unknown)]
	[InlineData(true, true, false, false, CheatEngineArchitecture.Unknown)]
	[InlineData(false, false, true, false, CheatEngineArchitecture.Unknown)]
	[InlineData(false, false, false, false, CheatEngineArchitecture.Unknown)]
	public void try_derive_target_architecture_follows_the_ce_family_table(bool isX86Family, bool isArmFamily,
		bool is64Bit, bool expectedResult, CheatEngineArchitecture expected)
	{
		bool derived = RuntimeInfo.TryDeriveTargetArchitecture(isX86Family, isArmFamily, is64Bit,
			out CheatEngineArchitecture architecture);

		Assert.Equal(expectedResult, derived);
		Assert.Equal(expected, architecture);
	}

	[Fact]
	[Trait("Qualification", "Q32.a")]
	public void try_derive_target_architecture_maps_the_x86_family_with_64_bit_to_x64()
	{
		// Spike C3 D2 (CE 7.7.0.10621, x64 Tutorial target): targetIsX86 = true, targetIs64Bit = true, targetIsArm = false.
		Assert.True(RuntimeInfo.TryDeriveTargetArchitecture(true, false, true, out CheatEngineArchitecture x64));
		Assert.Equal(CheatEngineArchitecture.X64, x64);
	}

	[Fact]
	[Trait("Qualification", "Q32.b")]
	public void try_derive_target_architecture_maps_the_x86_family_without_64_bit_to_x86()
	{
		// Spike C3 D2 (CE 7.7.0.10621, i386 tutorial target): targetIsX86 = true, targetIs64Bit = false.
		Assert.True(RuntimeInfo.TryDeriveTargetArchitecture(true, false, false, out CheatEngineArchitecture x86));
		Assert.Equal(CheatEngineArchitecture.X86, x86);
	}

	[Fact]
	[Trait("Qualification", "Q32.d")]
	public void try_derive_target_architecture_keeps_arm_and_contradictory_families_apart_from_the_64_bit_flag()
	{
		Assert.True(RuntimeInfo.TryDeriveTargetArchitecture(false, true, false, out CheatEngineArchitecture arm32));
		Assert.True(RuntimeInfo.TryDeriveTargetArchitecture(false, true, true, out CheatEngineArchitecture arm64));
		Assert.False(RuntimeInfo.TryDeriveTargetArchitecture(true, true, true, out CheatEngineArchitecture both));
		Assert.False(RuntimeInfo.TryDeriveTargetArchitecture(false, false, true, out CheatEngineArchitecture neither));

		Assert.Equal(CheatEngineArchitecture.Arm32, arm32);
		Assert.Equal(CheatEngineArchitecture.Arm64, arm64);
		Assert.Equal(CheatEngineArchitecture.Unknown, both);
		Assert.Equal(CheatEngineArchitecture.Unknown, neither);
	}

	[Theory]
	[InlineData(0, CheatEngineOperatingSystem.Windows)]
	[InlineData(1, CheatEngineOperatingSystem.MacOS)]
	[InlineData(2, CheatEngineOperatingSystem.Linux)]
	public void try_decode_operating_system_known_codes_decode(int code, CheatEngineOperatingSystem expected)
	{
		Assert.True(RuntimeInfo.TryDecodeOperatingSystem(code, out CheatEngineOperatingSystem operatingSystem));
		Assert.Equal(expected, operatingSystem);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(3)]
	[InlineData(int.MaxValue)]
	public void try_decode_operating_system_unknown_code_is_rejected(int code)
	{
		Assert.False(RuntimeInfo.TryDecodeOperatingSystem(code, out CheatEngineOperatingSystem operatingSystem));
		Assert.Equal(CheatEngineOperatingSystem.Unknown, operatingSystem);
	}

	[Fact]
	public void try_decode_file_version_splits_the_packed_integer()
	{
		// Spike C3 D5: CE 7.7.0.10621 x64 returned 0x700070000297D (1970354901756285).
		Assert.True(RuntimeInfo.TryDecodeFileVersion(0x7_0007_0000_297DL, out CheatEngineVersion ce77));
		Assert.Equal(CheatEngineVersion.Ce77010621, ce77);

		// The largest non-negative Lua integer: every 16-bit field at its maximum, the major field at 0x7FFF.
		Assert.True(RuntimeInfo.TryDecodeFileVersion(long.MaxValue, out CheatEngineVersion largest));
		Assert.Equal(new CheatEngineVersion(32767, 65535, 65535, 65535), largest);

		Assert.True(RuntimeInfo.TryDecodeFileVersion(0x0001_0002_0003_0004L, out CheatEngineVersion ordered));
		Assert.Equal(new CheatEngineVersion(1, 2, 3, 4), ordered);

		// A negative packed value (all 64 bits set, or any sign bit) is not a version.
		Assert.False(RuntimeInfo.TryDecodeFileVersion(-1L, out CheatEngineVersion negative));
		Assert.Equal(default, negative);
		Assert.False(RuntimeInfo.TryDecodeFileVersion(long.MinValue, out _));
	}

	[Fact]
	[Trait("Qualification", "Q32.d")]
	public void target_architecture_observation_computed_members_never_infer_missing_facts()
	{
		TargetProcessId pid = new(4242);
		TargetArchitectureObservation onlyBitness = new(pid, TargetBackend.Unknown, PointerSize.Bit64, null, null,
			null, null, null);
		TargetArchitectureObservation onlyX86 = new(pid, TargetBackend.LocalProcess, PointerSize.Bit64, true, null,
			false, 0, 8);
		TargetArchitectureObservation unknownBitness = new(pid, TargetBackend.LocalProcess, PointerSize.Unknown, true,
			false, false, 0, 8);
		TargetArchitectureObservation odd = new(pid, TargetBackend.LocalProcess, PointerSize.Bit64, true, false, false,
			9, 2);

		Assert.Equal(CheatEngineArchitecture.Unknown, onlyBitness.Architecture);
		Assert.Equal(PointerSize.Unknown, onlyBitness.ConfiguredPointerSize);
		Assert.Null(onlyBitness.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(TargetAbi.Unknown, onlyBitness.Abi);
		Assert.Null(onlyBitness.IsAndroid);

		// The ARM fact is absent, so no architecture is derived even though the x86 family is reported.
		Assert.Equal(CheatEngineArchitecture.Unknown, onlyX86.Architecture);
		Assert.Equal(PointerSize.Bit64, onlyX86.ConfiguredPointerSize);

		Assert.Equal(CheatEngineArchitecture.Unknown, unknownBitness.Architecture);
		Assert.Null(unknownBitness.ConfiguredPointerSizeDiffersFromBitness);

		// Any integer is kept raw; only 4 and 8 become a PointerSize; an undocumented ABI code stays raw.
		Assert.Equal(2, odd.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, odd.ConfiguredPointerSize);
		Assert.True(odd.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(9, odd.AbiCode);
		Assert.Equal(TargetAbi.Unknown, odd.Abi);
		Assert.Equal(CheatEngineArchitecture.X64, odd.Architecture);
	}

	[Fact]
	public void target_architecture_observation_is_a_value_that_compares_every_fact()
	{
		TargetProcessId pid = new(4242);
		TargetArchitectureObservation x64 = new(pid, TargetBackend.LocalProcess, PointerSize.Bit64, true, false, false,
			0, 8);
		TargetArchitectureObservation same = new(pid, TargetBackend.LocalProcess, PointerSize.Bit64, true, false, false,
			0, 8);
		TargetArchitectureObservation remote = new(pid, TargetBackend.CEServer, PointerSize.Bit64, true, false, false,
			0, 8);
		TargetArchitectureObservation narrowed = new(pid, TargetBackend.LocalProcess, PointerSize.Bit64, true, false,
			false, 0, 4);

		Assert.Equal(x64, same);
		Assert.NotEqual(x64, remote);
		Assert.NotEqual(x64, narrowed);
		Assert.Equal(TargetBackend.LocalProcess, x64.Backend);
		Assert.Equal(pid, x64.ProcessId);
	}

	[Fact]
	public void runtime_capability_identifiers_are_stable_distinct_and_not_lua_global_names()
	{
		RuntimeCapabilityId[] identifiers =
		[
			RuntimeCapabilityId.CheatEngineVersion, RuntimeCapabilityId.SystemArchitecture,
			RuntimeCapabilityId.TargetArchitecture, RuntimeCapabilityId.CurrentProcess,
			RuntimeCapabilityId.ProcessSelection, RuntimeCapabilityId.TargetAbi,
			RuntimeCapabilityId.ConfiguredPointerSize, RuntimeCapabilityId.CheatEngineBitness,
			RuntimeCapabilityId.OperatingSystem, RuntimeCapabilityId.TargetAndroid, RuntimeCapabilityId.TargetBackend
		];
		string[] luaGlobals =
		[
			"getCheatEngineFileVersion", "getCEVersion", "getSystemArchitecture", "cheatEngineIs64Bit",
			"getOperatingSystem", "getOpenedProcessID", "openProcess", "targetIs64Bit", "targetIsX86", "targetIsArm",
			"targetIsAndroid", "getABI", "getPointerSize", "isConnectedToCEServer"
		];

		Assert.Equal("Runtime.ConfiguredPointerSize", RuntimeCapabilityId.ConfiguredPointerSize.Value);
		Assert.Equal("Runtime.CheatEngineBitness", RuntimeCapabilityId.CheatEngineBitness.Value);
		Assert.Equal("Runtime.OperatingSystem", RuntimeCapabilityId.OperatingSystem.Value);
		Assert.Equal("Runtime.TargetAndroid", RuntimeCapabilityId.TargetAndroid.Value);
		Assert.Equal("Runtime.TargetBackend", RuntimeCapabilityId.TargetBackend.Value);
		Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
		foreach (RuntimeCapabilityId identifier in identifiers)
		{
			Assert.False(identifier.IsEmpty);
			Assert.DoesNotContain(identifier.Value, luaGlobals, StringComparer.OrdinalIgnoreCase);
			Assert.Contains(".", identifier.Value, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void target_backend_and_operating_system_values_are_pinned()
	{
		Assert.Equal(0, (byte) TargetBackend.Unknown);
		Assert.Equal(1, (byte) TargetBackend.LocalProcess);
		Assert.Equal(2, (byte) TargetBackend.FileAsProcess);
		Assert.Equal(3, (byte) TargetBackend.CEServer);
		Assert.Equal(4, Enum.GetValues<TargetBackend>().Length);

		Assert.Equal(0, (byte) CheatEngineOperatingSystem.Unknown);
		Assert.Equal(1, (byte) CheatEngineOperatingSystem.Windows);
		Assert.Equal(2, (byte) CheatEngineOperatingSystem.MacOS);
		Assert.Equal(3, (byte) CheatEngineOperatingSystem.Linux);
		Assert.Equal(4, Enum.GetValues<CheatEngineOperatingSystem>().Length);
	}

	[Fact]
	public void cheat_engine_host_observation_keeps_each_fact_separate()
	{
		CheatEngineHostObservation host = new(null, CheatEngineArchitecture.X64, null,
			CheatEngineOperatingSystem.Unknown);
		CheatEngineHostObservation reported = host with
		{
			CheatEngineIs64Bit = false
		};

		Assert.Null(host.FileVersion);
		Assert.Null(host.CheatEngineIs64Bit);
		Assert.Equal(CheatEngineArchitecture.X64, host.SystemArchitecture);
		Assert.False(reported.CheatEngineIs64Bit);
		Assert.NotEqual(host, reported);
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
#pragma warning disable CESDK7001 // Pins the obsolete 1.0.0 behaviour, kept for binary compatibility.
		Assert.Equal(PointerSize.Bit32, PointerSize.FromArchitecture(CheatEngineArchitecture.X86));
		Assert.Equal(PointerSize.Bit32, PointerSize.FromArchitecture(CheatEngineArchitecture.Arm32));
		Assert.Equal(PointerSize.Bit64, PointerSize.FromArchitecture(CheatEngineArchitecture.X64));
		Assert.Equal(PointerSize.Bit64, PointerSize.FromArchitecture(CheatEngineArchitecture.Arm64));
		Assert.Equal(PointerSize.Unknown, PointerSize.FromArchitecture(CheatEngineArchitecture.Unknown));
#pragma warning restore CESDK7001
		Assert.Throws<ArgumentOutOfRangeException>(() => new PointerSize(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PointerSize(2));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PointerSize(16));
	}

	[Fact]
	public void pointer_size_from_architecture_is_obsolete_with_its_documented_diagnostic_id()
	{
		MethodInfo? method = typeof(PointerSize).GetMethod(nameof(PointerSize.FromArchitecture),
			BindingFlags.Public | BindingFlags.Static, [typeof(CheatEngineArchitecture)]);

		Assert.NotNull(method);
		ObsoleteAttribute? obsolete = method.GetCustomAttribute<ObsoleteAttribute>();
		Assert.NotNull(obsolete);
		Assert.Equal("CESDK7001", obsolete.DiagnosticId);
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md",
			obsolete.UrlFormat);
		Assert.False(obsolete.IsError);
		Assert.Contains("ConfiguredPointerSize", obsolete.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void
		PointerSize_little_endian_primitives_use_the_explicit_target_width_and_preserve_destinations_on_rejection()
	{
		ReadOnlySpan<byte> bytes = [0x98, 0xBA, 0xDC, 0xFE, 0xEF, 0xCD, 0xAB, 0x89];
		Span<byte> narrow = stackalloc byte[4];
		Span<byte> wide = stackalloc byte[8];
		Span<byte> untouched = stackalloc byte[3];
		untouched.Fill(0xA5);

		Assert.True(PointerSize.Bit32.TryReadLittleEndian(bytes[..4], out ulong x86Pointer));
		Assert.Equal(0xFEDCBA98UL, x86Pointer);
		Assert.True(PointerSize.Bit64.TryReadLittleEndian(bytes, out ulong x64Pointer));
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
		RuntimeCapabilityContract contract = new(
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
				RuntimeCapabilityContract.Unknown)
		];

		RuntimeCapabilities capabilities = RuntimeCapabilities.Create(source);
		source[0] = new RuntimeCapabilityAvailability(RuntimeCapabilityId.TargetArchitecture,
			RuntimeCapabilityAvailabilityState.Unknown, RuntimeCapabilityContract.Unknown);

		Assert.Equal(2, capabilities.Count);
		Assert.Equal(RuntimeCapabilityAvailabilityState.Available,
			capabilities.GetState(RuntimeCapabilityId.TargetArchitecture));
		Assert.Equal(RuntimeCapabilityAvailabilityState.Unknown,
			capabilities.GetState(RuntimeCapabilityId.SystemArchitecture));
		Assert.True(capabilities.TryGet(RuntimeCapabilityId.TargetArchitecture,
			out RuntimeCapabilityAvailability availability));
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
				RuntimeCapabilityContract.Unknown)
		];

		Assert.Throws<ArgumentException>(() => RuntimeCapabilities.Create(emptyIdentifier));
		Assert.Throws<ArgumentException>(() => RuntimeCapabilities.Create(duplicateIdentifier));
		Assert.Throws<ArgumentException>(() => new RuntimeCapabilityId(" "));
	}

	[Fact]
	[Trait("Qualification", "Q31.a")]
	public void runtime_info_created_from_observations_uses_the_configured_pointer_size_and_keeps_unknowns()
	{
		CheatEngineHostObservation host = new(CheatEngineVersion.Ce77010621, CheatEngineArchitecture.X64, true,
			CheatEngineOperatingSystem.Windows);
		TargetArchitectureObservation narrowed = new(new TargetProcessId(4242), TargetBackend.LocalProcess,
			PointerSize.Bit64, true, false, false, 0, 4);
		TargetArchitectureObservation odd = narrowed with
		{
		};
		TargetArchitectureObservation unknownFamilies = new(new TargetProcessId(4242), TargetBackend.Unknown,
			PointerSize.Bit64, null, null, null, 9, 2);

		RuntimeInfo info = new(host, narrowed, RuntimeCapabilities.Empty);
		RuntimeInfo withoutTarget = new(host with
		{
			FileVersion = null
		}, null, RuntimeCapabilities.Empty);
		RuntimeInfo unknown = new(host, unknownFamilies, RuntimeCapabilities.Empty);

		Assert.Equal(host, info.Host);
		Assert.Equal(narrowed, info.Target);
		Assert.Equal(odd, info.Target);
		Assert.Equal(CheatEngineVersion.Ce77010621, info.Version);
		Assert.Equal(CheatEngineArchitecture.X64, info.SystemArchitecture);
		Assert.Equal(CheatEngineArchitecture.X64, info.TargetArchitecture);
		Assert.Equal(PointerSize.Bit32, info.PointerSize);
		Assert.Equal(PointerSize.Bit64, info.Target?.Bitness);
		Assert.Equal(TargetAbi.Windows, info.TargetAbi);

		Assert.Null(withoutTarget.Target);
		Assert.Equal(default, withoutTarget.Version);
		Assert.Equal(CheatEngineArchitecture.Unknown, withoutTarget.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, withoutTarget.PointerSize);
		Assert.Equal(TargetAbi.Unknown, withoutTarget.TargetAbi);

		Assert.Equal(CheatEngineArchitecture.Unknown, unknown.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, unknown.PointerSize);
		Assert.Equal(TargetAbi.Unknown, unknown.TargetAbi);
		Assert.Throws<ArgumentNullException>(() => new RuntimeInfo(host, null, null!));
	}

	[Fact]
	public void legacy_runtime_info_constructor_keeps_caller_supplied_facts()
	{
		RuntimeInfo info = new(new CheatEngineVersion(7, 5, 0, 0), CheatEngineArchitecture.X64,
			CheatEngineArchitecture.Arm64, PointerSize.Bit64, TargetAbi.Unix, RuntimeCapabilities.Empty);

		Assert.Null(info.Host);
		Assert.Null(info.Target);
		Assert.Equal(new CheatEngineVersion(7, 5, 0, 0), info.Version);
		Assert.Equal(CheatEngineArchitecture.Arm64, info.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, info.PointerSize);
		Assert.Equal(TargetAbi.Unix, info.TargetAbi);
	}

	[Fact]
	[Trait("Qualification", "Q32.c")]
	public void system_architecture_i386_is_reported_as_a_host_fact_and_never_changes_target_facts()
	{
		// An i386 Cheat Engine host (getSystemArchitecture() == 0) is an unsupported route of the x64-only SDK; the fact
		// is still reported as-is and never rewrites the target facts or the CE bitness fact.
		Assert.True(RuntimeInfo.TryDecodeSystemArchitecture(0, out CheatEngineArchitecture i386));
		CheatEngineHostObservation host = new(null, i386, null, CheatEngineOperatingSystem.Windows);
		TargetArchitectureObservation x64Target = new(new TargetProcessId(4242), TargetBackend.LocalProcess,
			PointerSize.Bit64, true, false, false, 0, 8);

		RuntimeInfo info = new(host, x64Target, RuntimeCapabilities.Empty);

		Assert.Equal(CheatEngineArchitecture.X86, info.SystemArchitecture);
		Assert.Null(info.Host?.CheatEngineIs64Bit);
		Assert.Equal(CheatEngineArchitecture.X64, info.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, info.PointerSize);
		Assert.Equal(PointerSize.Bit64, info.Target?.Bitness);
	}

	[Fact]
	public void RuntimeInfo_constructor_preserves_explicit_runtime_facts_without_normalization()
	{
		RuntimeCapabilities capabilities = RuntimeCapabilities.Create(
		[
			new RuntimeCapabilityAvailability(RuntimeCapabilityId.SystemArchitecture,
				RuntimeCapabilityAvailabilityState.Available, RuntimeCapabilityContract.Unknown)
		]);
		RuntimeInfo info = new(
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
