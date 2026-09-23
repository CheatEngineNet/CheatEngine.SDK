using System.Globalization;
using System.Reflection;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Fixture;

/// <summary>
///     Compares the x64 facts emitted by the independently compiled C++ fixture (schema 3) with measurements of the
///     managed ABI records: size, alignment, and every field's offset and width. The native CI job supplies the facts
///     file; ordinary managed runs deliberately have no C++ fixture filesystem dependency (see
///     <see cref="NativeAbiFixtureFacts" />).
/// </summary>
/// <remarks>
///     The fixture transcribes the C header records and, since schema 3, the pinned host Pascal types of the managed
///     route (<c>TPluginDotNetInitResult</c>, packed, and <c>TExportedFunctionsDotNetV1</c>). Field names in the fact keys
///     are the C# field names, so every field of a compared managed record is looked up by reflection and must have
///     a native counterpart, and every native field of a compared record must have a managed one.
/// </remarks>
public sealed class NativeAbiFixtureManagedComparisonTests
{
	[Fact]
	public void Native_fixture_layout_facts_match_the_managed_x64_measurements_when_CI_supplies_them()
	{
		Dictionary<string, string>? nativeFacts = NativeAbiFixtureFacts.LoadFromEnvironment();
		if (nativeFacts is null)
		{
			Assert.Null(nativeFacts);
			return;
		}

		Assert.True(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Dictionary<string, string> managedFacts = CreateManagedLayoutFacts(out IReadOnlyList<string> comparedRecords);
		foreach (KeyValuePair<string, string> managedFact in managedFacts)
		{
			Assert.True(nativeFacts.TryGetValue(managedFact.Key, out string? nativeValue),
				$"The native ABI fixture omitted '{managedFact.Key}'.");
			Assert.True(string.Equals(managedFact.Value, nativeValue, StringComparison.Ordinal),
				$"'{managedFact.Key}': managed {managedFact.Value}, native {nativeValue}.");
		}

		foreach (string nativeKey in nativeFacts.Keys)
		{
			foreach (string record in comparedRecords)
			{
				if (nativeKey.StartsWith("offsetof." + record + ".", StringComparison.Ordinal) ||
				    nativeKey.StartsWith("fieldsize." + record + ".", StringComparison.Ordinal))
				{
					Assert.True(managedFacts.ContainsKey(nativeKey),
						$"The native fixture transcribes '{nativeKey}', which has no managed field.");
				}
			}
		}
	}

	[Fact]
	public void Native_fixture_facts_declare_schema_3_when_CI_supplies_them()
	{
		Dictionary<string, string>? nativeFacts = NativeAbiFixtureFacts.LoadFromEnvironment();
		if (nativeFacts is null)
		{
			Assert.Null(nativeFacts);
			return;
		}

		Assert.Equal(NativeAbiFixtureFacts.ExpectedSchema, nativeFacts["fixture.schema"]);
		Assert.Equal("ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37", nativeFacts["source.upstream_commit"]);
		Assert.Equal("Cheat Engine/plugin/cepluginsdk.h", nativeFacts["source.path"]);
		Assert.Equal("Cheat Engine/plugin.pas", nativeFacts["source.host_path"]);
		Assert.Equal("Cheat Engine/plugin/cepluginsdk.pas", nativeFacts["source.mirror_path"]);
		Assert.Equal("transcribed-pinned-header-and-host-pascal-subset", nativeFacts["source.contract"]);
	}

	[Fact]
	public void Native_fixture_proves_the_packed_init_record_write_leaves_the_tail_guard_intact_when_CI_supplies_them()
	{
		Dictionary<string, string>? nativeFacts = NativeAbiFixtureFacts.LoadFromEnvironment();
		if (nativeFacts is null)
		{
			Assert.Null(nativeFacts);
			return;
		}

		Assert.Equal("passed", nativeFacts["sentinel.managed_plugin_init_record.tail_guard"]);
		Assert.Equal("36", nativeFacts["sizeof.managed_plugin_init_record"]);
		Assert.Equal("1", nativeFacts["alignof.managed_plugin_init_record"]);
		Assert.Equal("4", nativeFacts["fieldsize.managed_plugin_init_record.Version"]);
		Assert.Equal("48", nativeFacts["sizeof.managed_exported_functions"]);
	}

	[Fact]
	public void Native_fixture_facts_path_is_optional_when_required_mode_is_not_enabled()
	{
		Assert.Null(NativeAbiFixtureFacts.ResolveFactsPath(null, null));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t")]
	public void Native_fixture_required_mode_rejects_an_absent_facts_path(string? factsPath)
	{
		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => NativeAbiFixtureFacts.ResolveFactsPath(factsPath, "true"));

		Assert.Equal(
			$"'{NativeAbiFixtureFacts.RequiredEnvironmentVariable}=true' requires '{NativeAbiFixtureFacts.FactsPathEnvironmentVariable}' to name a validated native ABI fixture facts file.",
			exception.Message);
	}

	[Fact]
	public void Native_fixture_comparison_rejects_a_supplied_missing_facts_file()
	{
		string factsPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

		FileNotFoundException exception =
			Assert.Throws<FileNotFoundException>(() => NativeAbiFixtureFacts.ReadFacts(factsPath));

		Assert.Equal(factsPath, exception.FileName);
	}

	[Fact]
	public void Managed_fact_set_names_every_field_of_every_compared_record()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Dictionary<string, string> facts = CreateManagedLayoutFacts(out IReadOnlyList<string> records);

		Assert.Equal(15, records.Count);
		Assert.Equal("36", facts["sizeof.managed_plugin_init_record"]);
		Assert.Equal("1", facts["alignof.managed_plugin_init_record"]);
		Assert.Equal("32", facts["offsetof.managed_plugin_init_record.Version"]);
		Assert.Equal("8", facts["fieldsize.managed_exported_functions.CheckSynchronize"]);
		Assert.Equal("4", facts["fieldsize.plugin_type0_record.IsPointer"]);
		Assert.Equal("260", facts["offsetof.register_modification_info.NewOf"]);
		Assert.Equal("8", facts["fieldsize.exported_functions_prefix.GetAddressFromPointer"]);
	}

	/// <summary>
	///     The managed side of the comparison: for each compared record, size and alignment (generic address-of
	///     arithmetic) and every field's offset and width (<see cref="FieldLayoutGate" />, the helper of the WI-1 gate).
	/// </summary>
	internal static Dictionary<string, string> CreateManagedLayoutFacts(out IReadOnlyList<string> comparedRecords)
	{
		Dictionary<string, string> facts = new(StringComparer.Ordinal);
		List<string> records = [];
		AddRecord<PluginVersion>(facts, records, "plugin_version");
		AddRecord<PluginType0Record>(facts, records, "plugin_type0_record");
		AddRecord<AddressListPluginInit>(facts, records, "plugin_type0_init");
		AddRecord<MemoryViewPluginInit>(facts, records, "plugin_type1_init");
		AddRecord<DebugEventPluginInit>(facts, records, "plugin_type2_init");
		AddRecord<ProcessWatcherPluginInit>(facts, records, "plugin_type3_init");
		AddRecord<FunctionPointerChangePluginInit>(facts, records, "plugin_type4_init");
		AddRecord<MainMenuPluginInit>(facts, records, "plugin_type5_init");
		AddRecord<DisassemblerContextPluginInit>(facts, records, "plugin_type6_init");
		AddRecord<DisassemblerRenderLinePluginInit>(facts, records, "plugin_type7_init");
		AddRecord<AutoAssemblerPluginInit>(facts, records, "plugin_type8_init");
		AddRecord<RegisterModificationInfo>(facts, records, "register_modification_info");
		AddRecord<ExportedFunctionsPrefix>(facts, records, "exported_functions_prefix");
		AddRecord<PluginInitRecord>(facts, records, "managed_plugin_init_record");
		AddRecord<ManagedExportedFunctions>(facts, records, "managed_exported_functions");
		comparedRecords = records;
		return facts;
	}

	/// <summary>Adds the size, alignment and per-field facts of <typeparamref name="T" /> under <paramref name="key" />.</summary>
	internal static void AddRecord<T>(Dictionary<string, string> facts, List<string> records, string key)
		where T : unmanaged
	{
		records.Add(key);
		facts.Add($"sizeof.{key}", Text(Layout.SizeOf<T>()));
		facts.Add($"alignof.{key}", Text(Layout.AlignmentOf<T>()));
		foreach (FieldInfo field in typeof(T).GetFields(FieldLayoutGate.InstanceFields))
		{
			facts.Add($"offsetof.{key}.{field.Name}", Text(FieldLayoutGate.OffsetOf(field)));
			facts.Add($"fieldsize.{key}.{field.Name}", Text(FieldLayoutGate.WidthOf(field)));
		}
	}

	private static string Text(int value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}
