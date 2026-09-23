using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests;

/// <summary>
///     Assembly-wide gates. They exist so that a structure or enumeration added to <c>CheatEngine.SDK.Abi</c> without a
///     row in the expected-size table, or with a shape the ABI does not allow (see <see cref="AbiShape" />: forbidden
///     field types at any depth, function pointers that are managed, not <c>Stdcall</c>, or carry a forbidden type in
///     their signature), fails the test run instead of slipping through. The per-field offset and width of every one of
///     these structures is gated by <c>FieldLayout.FieldLayoutContractTests</c>.
/// </summary>
/// <remarks>
///     What reflection cannot see, and therefore stays a review rule: whether <c>[StructLayout]</c> was written out
///     (C# structures are sequential by default, the metadata is identical).
/// </remarks>
public sealed class AssemblyConformanceTests
{
	private const string Abi = "CheatEngine.SDK.Abi.";
	private const string Managed = Abi + "Managed.";
	private const string Native = Abi + "Native.";

	private static readonly Assembly AbiAssembly = AbiStructures.AbiAssembly;

	/// <summary>
	///     Every structure of the assembly (public, internal and nested private) with its 64-bit size, keyed by full name.
	///     Adding a structure means adding a row here and its field rows in <see cref="FieldLayoutExpectations" />.
	/// </summary>
	private static readonly Dictionary<string, int> ExpectedSizesOn64Bit = new(StringComparer.Ordinal)
	{
		[Abi + nameof(Bool32)] = 4,
		[Abi + nameof(Bool8)] = 1,
		[Managed + nameof(PluginInitRecord)] = 36,
		[Managed + nameof(ManagedExportedFunctions)] = 48,
		[Native + nameof(PluginVersion)] = 16,
		[Native + nameof(AddressListPluginInit)] = 16,
		[Native + nameof(MemoryViewPluginInit)] = 24,
		[Native + nameof(DebugEventPluginInit)] = 8,
		[Native + nameof(DebugEventObservation)] = 24,
		[Native + nameof(ProcessWatcherPluginInit)] = 8,
		[Native + nameof(FunctionPointerChangePluginInit)] = 8,
		[Native + nameof(MainMenuPluginInit)] = 24,
		[Native + nameof(DisassemblerContextPluginInit)] = 32,
		[Native + nameof(DisassemblerRenderLinePluginInit)] = 8,
		[Native + nameof(AutoAssemblerPluginInit)] = 8,
		[Native + nameof(ExportedFunctionsPrefix)] = 144,
		[Native + nameof(PluginType0Record)] = 48,
		[Native + nameof(RegisterModificationInfo)] = 264,
		[Native + nameof(ClassicSlotObservation)] = 16,
		[Native + nameof(ClassicDebugEventDispatcher) + "+DebugEventHeader"] = 12
	};

	[Fact]
	public void Assembly_disables_runtime_marshalling()
	{
		Assert.NotNull(AbiAssembly.GetCustomAttribute<DisableRuntimeMarshallingAttribute>());
	}

	[Fact]
	public void Assembly_references_no_other_cheatengine_sdk_assembly()
	{
		string[] cheatEngineSdkReferences = AbiAssembly.GetReferencedAssemblies()
			.Select(static name => name.Name ?? string.Empty)
			.Where(static name => name.StartsWith("CheatEngine.SDK", StringComparison.Ordinal))
			.ToArray();

		Assert.Empty(cheatEngineSdkReferences);
	}

	[Fact]
	public void Every_structure_is_listed_in_the_expected_size_table()
	{
		string[] actual = AbiStructures.All().Select(static type => type.FullName!).Order(StringComparer.Ordinal).ToArray();
		string[] expected = ExpectedSizesOn64Bit.Keys.Order(StringComparer.Ordinal).ToArray();

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void Every_structure_including_internal_and_nested_ones_has_the_expected_size()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.All(
			AbiStructures.All(),
			static type =>
			{
				Assert.True(ExpectedSizesOn64Bit.TryGetValue(type.FullName!, out int expected),
					$"No expected size for {type.FullName}.");
				Assert.Equal(expected, RuntimeHelpers.SizeOf(type.TypeHandle));
			});
	}

	[Fact]
	public void Public_structures_are_a_subset_of_the_gated_structures()
	{
		Assert.All(PublicStructures(),
			static type => Assert.True(ExpectedSizesOn64Bit.ContainsKey(type.FullName!), $"{type.FullName} is not gated."));
	}

	[Fact]
	public void Every_structure_is_sequential_and_blittable_at_any_depth()
	{
		Type[] structures = AbiStructures.All().ToArray();

		Assert.NotEmpty(structures);
		Assert.All(structures, static type => Assert.Null(AbiShape.FindViolation(type, AbiAssembly)));
	}

	[Fact]
	public void Only_the_init_record_overrides_the_default_packing()
	{
		Assert.All(
			AbiStructures.All(),
			static type =>
			{
				// Reflection reports the default either as 0 or as the runtime's default of 8.
				int pack = type.StructLayoutAttribute?.Pack ?? 0;
				if (type == typeof(PluginInitRecord))
				{
					Assert.Equal(1, pack);
				}
				else
				{
					Assert.True(pack is 0 or 8,
						$"{type.Name} declares Pack = {pack.ToString(CultureInfo.InvariantCulture)}: only the init record is packed by the host.");
				}
			});
	}

	[Fact]
	public void Every_public_enumeration_is_four_bytes_wide()
	{
		Type[] enums = AbiAssembly.GetExportedTypes().Where(static type => type.IsEnum).ToArray();

		Assert.NotEmpty(enums);
		Assert.All(enums, static type => Assert.Equal(typeof(int), Enum.GetUnderlyingType(type)));
	}

	private static IEnumerable<Type> PublicStructures()
	{
		return AbiAssembly.GetExportedTypes().Where(static type => type.IsValueType && !type.IsEnum);
	}
}
