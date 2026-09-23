using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.SourceScanning;

namespace CheatEngine.SDK.Repository.Tests.Abi;

/// <summary>
///     The committed classic slot registry (test-owned data, never a top-level <c>docs/</c> folder): the 159-slot
///     contract of the classic <c>ExportedFunctions</c> table, whose authority is the pinned <c>plugin.pas</c> (audit
///     annex 04), with the C and Pascal mirrors as secondary columns and the divergence register of annex 05 (audit
///     F09, A23-F09-1, AX04-*, AX05-*). These tests read only the committed document, never the Cheat Engine sources or
///     an installation, and enforce its shape with plain C# assertions (there is no JSON Schema infrastructure left in
///     this repository).
/// </summary>
public sealed partial class ClassicSlotRegistryDocumentTests
{
	private const string ShapeTestsPath = "tests/CheatEngine.SDK.Abi.Tests/Native/PluginCallbackShapeTests.cs";

	/// <summary>Host nil assignments of TPluginHandler.create (audit annex 04 and brief appendix A).</summary>
	private static readonly int[] NilSlots = [14, 28, 29, 40, 43, 46, 49, 50, 51, 52, 53];

	/// <summary>Host <c>@@X</c> assignments: slots 18-27, 30-35 and 73-81.</summary>
	private static readonly int[] CellSlots =
	[
		.. Enumerable.Range(18, 10), .. Enumerable.Range(30, 6), .. Enumerable.Range(73, 9)
	];

	private static readonly string[] AuditDivergences = ["D01", "D02", "D03", "D04", "D05", "D06", "D07", "D08", "D09", "D10"];

	private static JsonElement Registry => RepositoryDocument.LoadJson(ClassicSlotRegistryContract.RegistryPath);

	private static JsonElement[] Slots => [.. Registry.GetProperty("slots").EnumerateArray()];

	[Fact]
	public void Registry_top_level_and_nested_objects_have_the_expected_required_properties()
	{
		JsonElement root = Registry;
		AssertProperties(ClassicSlotRegistryContract.TopLevelRequired, root);
		AssertProperties(ClassicSlotRegistryContract.SourceRequired, root.GetProperty("sources")[0]);
		AssertProperties(ClassicSlotRegistryContract.ContractRequired, root.GetProperty("contract"));
		AssertProperties(ClassicSlotRegistryContract.SlotRequired, Slots[0]);
		AssertProperties(ClassicSlotRegistryContract.DivergenceRequired, root.GetProperty("divergences")[0]);
		AssertProperties(ClassicSlotRegistryContract.CallbackCategoryRequired, root.GetProperty("callbackCategories")[0]);

		Assert.All(root.GetProperty("sources").EnumerateArray(),
			source => AssertProperties(ClassicSlotRegistryContract.SourceRequired, source));
		Assert.All(Slots, slot => AssertProperties(ClassicSlotRegistryContract.SlotRequired, slot));
		Assert.All(root.GetProperty("divergences").EnumerateArray(),
			divergence => AssertProperties(ClassicSlotRegistryContract.DivergenceRequired, divergence));
		Assert.All(root.GetProperty("callbackCategories").EnumerateArray(),
			category => AssertProperties(ClassicSlotRegistryContract.CallbackCategoryRequired, category));

		Assert.Equal(ClassicSlotRegistryContract.Kind, root.GetProperty("schema").GetString());
		Assert.All(root.GetProperty("sources").EnumerateArray(), source =>
			Assert.Contains(source.GetProperty("role").GetString(), ClassicSlotRegistryContract.SourceRoles));
		Assert.All(Slots, slot =>
		{
			Assert.Contains(slot.GetProperty("section").GetString(), ClassicSlotRegistryContract.Sections);
			Assert.Contains(slot.GetProperty("hostAssignment").GetProperty("kind").GetString(), ClassicSlotRegistryContract.AssignmentKinds);
			Assert.Contains(slot.GetProperty("nature").GetString(), ClassicSlotRegistryContract.Natures);
			Assert.Contains(slot.GetProperty("callingConvention").GetString(), ClassicSlotRegistryContract.CallingConventions);
			Assert.Contains(slot.GetProperty("nullability").GetString(), ClassicSlotRegistryContract.Nullabilities);
			Assert.Contains(slot.GetProperty("sdkExposure").GetString(), ClassicSlotRegistryContract.SdkExposures);
			Assert.Contains(slot.GetProperty("facadeStatus").GetString(), ClassicSlotRegistryContract.FacadeStatuses);
			Assert.Contains(slot.GetProperty("ownership").GetString(), ClassicSlotRegistryContract.Ownerships);
			Assert.Contains(slot.GetProperty("evidenceKind").GetString(), ClassicSlotRegistryContract.SlotEvidenceKinds);
		});
	}

	[Fact]
	public void Registry_lists_one_int_and_158_pointer_slots_with_contiguous_x64_offsets_and_1272_bytes()
	{
		JsonElement[] slots = Slots;
		JsonElement contract = Registry.GetProperty("contract");

		Assert.Equal(159, slots.Length);
		Assert.Equal(159, contract.GetProperty("fieldCount").GetInt32());
		Assert.Equal(158, contract.GetProperty("pointerFieldCount").GetInt32());
		Assert.Equal(1272, contract.GetProperty("x64TableSize").GetInt32());
		Assert.Equal(6, contract.GetProperty("pluginContractVersion").GetInt32());
		Assert.Equal("TExportedFunctions5", contract.GetProperty("hostTableType").GetString());

		for (int index = 0; index < slots.Length; index++)
		{
			JsonElement slot = slots[index];
			Assert.Equal(index, slot.GetProperty("slot").GetInt32());
			Assert.Equal(index == 0 ? 0 : 8 * index, slot.GetProperty("x64Offset").GetInt32());
			Assert.Equal(index == 0 ? 4 : 8, slot.GetProperty("width").GetInt32());
		}

		Assert.Equal("integer", slots[0].GetProperty("hostField").GetProperty("type").GetString());
		Assert.Equal(158, slots.Count(static slot => slot.GetProperty("width").GetInt32() == 8));
		JsonElement last = slots[^1];
		Assert.Equal(1272, last.GetProperty("x64Offset").GetInt32() + last.GetProperty("width").GetInt32());
	}

	[Fact]
	public void Every_slot_min_declared_size_is_its_offset_plus_width()
	{
		foreach (JsonElement slot in Slots)
		{
			int index = slot.GetProperty("slot").GetInt32();
			int expected = slot.GetProperty("x64Offset").GetInt32() + slot.GetProperty("width").GetInt32();
			Assert.Equal(expected, slot.GetProperty("minDeclaredSize").GetInt32());
			Assert.Equal(index == 0 ? 4 : 8 * (index + 1), expected);
		}

		Assert.Equal(144, Slots[17].GetProperty("minDeclaredSize").GetInt32());
		Assert.Equal(Registry.GetProperty("contract").GetProperty("sdkDirectPrefixSize").GetInt32(),
			Slots[17].GetProperty("minDeclaredSize").GetInt32());
	}

	[Fact]
	public void Sections_have_the_audit_slot_ranges()
	{
		foreach (JsonElement slot in Slots)
		{
			int index = slot.GetProperty("slot").GetInt32();
			string expected = index switch
			{
				<= 83 => "Base",
				<= 86 => "V2",
				<= 94 => "V3",
				<= 154 => "V4",
				_ => "V5"
			};
			Assert.True(string.Equals(expected, slot.GetProperty("section").GetString(), StringComparison.Ordinal),
				$"Slot {index} is in section {slot.GetProperty("section").GetString()}, expected {expected}.");
		}

		Assert.Equal(["sym_nameToAddress", "sym_addressToName", "sym_generateAPIHookScript"],
			Slots[84..87].Select(static slot => HostName(slot)), StringComparer.Ordinal);
		Assert.Equal(["ExecuteKernelCode", "UserdefinedInterruptHook", "GetLuaState", "MainThreadCall"],
			Slots[155..].Select(static slot => HostName(slot)), StringComparer.Ordinal);
	}

	[Fact]
	public void Nil_assigned_slots_are_exactly_the_eleven_host_nil_assignments()
	{
		int[] nil = SlotsWhere(static slot => IsKind(slot, "Nil"));
		int[] nilAssigned = SlotsWhere(static slot => string.Equals(slot.GetProperty("nullability").GetString(), "NilAssigned", StringComparison.Ordinal));

		Assert.Equal(NilSlots, nil);
		Assert.Equal(NilSlots, nilAssigned);
		Assert.All(nil, static index => Assert.Equal("nil", Slots[index].GetProperty("hostAssignment").GetProperty("expression").GetString()));
		Assert.Equal("fixmem", HostName(Slots[14]));
	}

	[Fact]
	public void Cell_slots_are_the_twenty_five_double_address_assignments()
	{
		int[] cells = SlotsWhere(static slot => IsKind(slot, "CellAddress"));
		int[] doubleAddress = SlotsWhere(static slot =>
			slot.GetProperty("hostAssignment").GetProperty("expression").GetString()!.StartsWith("@@", StringComparison.Ordinal));
		int[] functionPointerCells = SlotsWhere(static slot => string.Equals(slot.GetProperty("nature").GetString(), "FunctionPointerCell", StringComparison.Ordinal));

		Assert.Equal(25, cells.Length);
		Assert.Equal(CellSlots, cells);
		Assert.Equal(CellSlots, doubleAddress);
		Assert.Equal(CellSlots, functionPointerCells);
		Assert.All(cells, static index =>
		{
			Assert.Equal(1, Slots[index].GetProperty("indirection").GetInt32());
			Assert.Equal("Borrowed", Slots[index].GetProperty("ownership").GetString());
			Assert.Equal("windows", Slots[index].GetProperty("hostAssignment").GetProperty("condition").GetString());
		});
	}

	[Fact]
	public void Data_and_object_reference_cells_have_their_declared_nature()
	{
		Assert.Equal([4, 5], SlotsWhere(static slot => string.Equals(slot.GetProperty("nature").GetString(), "DataCell", StringComparison.Ordinal)));
		Assert.Equal([82, 83], SlotsWhere(static slot => string.Equals(slot.GetProperty("nature").GetString(), "ObjectRefCell", StringComparison.Ordinal)));
		int[] cells = [4, 5, 82, 83];
		foreach (int index in cells)
		{
			JsonElement slot = Slots[index];
			Assert.Equal("VariableAddress", AssignmentKind(slot));
			Assert.Equal(1, slot.GetProperty("indirection").GetInt32());
			Assert.Equal("Borrowed", slot.GetProperty("ownership").GetString());
			Assert.Equal("NotApplicable", slot.GetProperty("callingConvention").GetString());
		}

		Assert.Equal(["OpenedProcessID", "OpenedProcessHandle", "mainform", "memorybrowser"],
			cells.Select(static index => HostName(Slots[index])), StringComparer.Ordinal);
		Assert.Equal("Int32", Slots[0].GetProperty("nature").GetString());
		Assert.Equal("Size", AssignmentKind(Slots[0]));
		Assert.All(Slots.Where(static slot => IsKind(slot, "FunctionAddress")), static slot =>
		{
			Assert.Equal("Function", slot.GetProperty("nature").GetString());
			Assert.Equal(0, slot.GetProperty("indirection").GetInt32());
		});
	}

	[Fact]
	public void Divergence_register_lists_the_ten_audit_divergences()
	{
		JsonElement[] divergences = [.. Registry.GetProperty("divergences").EnumerateArray()];
		string[] audit = [.. divergences.Where(static row => string.Equals(row.GetProperty("origin").GetString(), "Audit", StringComparison.Ordinal))
			.Select(static row => row.GetProperty("id").GetString()!)];

		Assert.Equal(AuditDivergences, audit);
		Assert.Equal([17], Ints(Divergence(divergences, "D01"), "slots"));
		Assert.Equal([89, 90], Ints(Divergence(divergences, "D02"), "slots"));
		Assert.Equal([91], Ints(Divergence(divergences, "D03"), "slots"));
		Assert.Equal([124], Ints(Divergence(divergences, "D04"), "slots"));
		Assert.Equal([134, 135], Ints(Divergence(divergences, "D05"), "slots"));
		Assert.Equal([157], Ints(Divergence(divergences, "D06"), "slots"));
		Assert.Equal([3], Ints(Divergence(divergences, "D07"), "callbacks"));
		Assert.Equal([4], Ints(Divergence(divergences, "D08"), "callbacks"));
		Assert.Equal([0], Ints(Divergence(divergences, "D09"), "callbacks"));
		Assert.Contains("PluginInitRecord", Strings(Divergence(divergences, "D10"), "records"), StringComparer.Ordinal);

		// Lot observations are extra rows, never replacements: each is a source observation of the pinned source.
		foreach (JsonElement row in divergences.Where(static row => string.Equals(row.GetProperty("origin").GetString(), "LotObservation", StringComparison.Ordinal)))
		{
			Assert.Equal("ObservedSource", row.GetProperty("evidenceKind").GetString());
		}

		Assert.Equal([61], Ints(Divergence(divergences, "D11"), "slots"));
		Assert.Equal("@VQE", Slots[61].GetProperty("hostAssignment").GetProperty("expression").GetString());
	}

	[Fact]
	public void Divergence_references_resolve_in_both_directions()
	{
		Dictionary<string, int[]> slotsById = Registry.GetProperty("divergences").EnumerateArray()
			.ToDictionary(static row => row.GetProperty("id").GetString()!, static row => Ints(row, "slots"),
				StringComparer.Ordinal);

		foreach (JsonElement slot in Slots)
		{
			int index = slot.GetProperty("slot").GetInt32();
			string[] refs = [.. slot.GetProperty("divergenceRefs").EnumerateArray().Select(static id => id.GetString()!)];
			foreach (string id in refs)
			{
				Assert.True(slotsById.TryGetValue(id, out int[]? listed), $"Slot {index} cites the unknown divergence {id}.");
				Assert.Contains(index, listed);
			}

			string[] expected = [.. slotsById.Where(pair => pair.Value.Contains(index)).Select(static pair => pair.Key).Order(StringComparer.Ordinal)];
			Assert.Equal(expected, refs.Order(StringComparer.Ordinal), StringComparer.Ordinal);
		}

		// D12: every slot of the cell section that the host assigns a direct @ expression (not @@, not nil).
		int[] direct = SlotsWhere(static slot => slot.GetProperty("slot").GetInt32() is >= 18 and <= 81 &&
												 IsKind(slot, "FunctionAddress"));
		Assert.Equal(direct, slotsById["D12"]);
	}

	[Fact]
	public void Every_divergent_or_nil_slot_is_never_prefix_typed()
	{
		foreach (JsonElement slot in Slots)
		{
			bool divergent = slot.GetProperty("divergenceRefs").GetArrayLength() > 0;
			bool nil = string.Equals(slot.GetProperty("nullability").GetString(), "NilAssigned", StringComparison.Ordinal);
			if (divergent || nil)
			{
				Assert.NotEqual("PrefixTyped", slot.GetProperty("sdkExposure").GetString(), StringComparer.Ordinal);
			}
		}

		Assert.Equal([14, 17], SlotsWhere(static slot => string.Equals(slot.GetProperty("sdkExposure").GetString(), "PrefixOpaque", StringComparison.Ordinal)));
		Assert.Equal([.. Enumerable.Range(0, 18).Except([14, 17])],
			SlotsWhere(static slot => string.Equals(slot.GetProperty("sdkExposure").GetString(), "PrefixTyped", StringComparison.Ordinal)));
		Assert.Equal([.. Enumerable.Range(18, 141)],
			SlotsWhere(static slot => string.Equals(slot.GetProperty("sdkExposure").GetString(), "None", StringComparison.Ordinal)));
		Assert.All(Slots[18..], static slot =>
		{
			Assert.Equal("Deferred", slot.GetProperty("facadeStatus").GetString());
			Assert.Equal(JsonValueKind.Null, slot.GetProperty("sdkField").ValueKind);
		});
	}

	[Fact]
	public void Authority_is_plugin_pas_and_mirrors_carry_their_hashes()
	{
		JsonElement[] sources = [.. Registry.GetProperty("sources").EnumerateArray()];

		Assert.Equal(ClassicSlotRegistryContract.SourceIds, sources.Select(static source => source.GetProperty("id").GetString()!),
			StringComparer.Ordinal);
		JsonElement authority = Assert.Single(sources, static source => string.Equals(source.GetProperty("role").GetString(), "Authority", StringComparison.Ordinal));
		Assert.Equal("Cheat Engine/plugin.pas", authority.GetProperty("path").GetString());
		foreach (JsonElement source in sources)
		{
			string id = source.GetProperty("id").GetString()!;
			Assert.Equal(ClassicSlotRegistryContract.PinnedSourceSha256[id], source.GetProperty("sha256").GetString());
			Assert.Equal(ClassicSlotRegistryContract.UpstreamCommit, source.GetProperty("commit").GetString());
			Assert.Equal("cheat-engine/cheat-engine", source.GetProperty("repository").GetString());
		}

		JsonElement header = sources[2].GetProperty("installed");
		Assert.Equal("MirrorC", sources[2].GetProperty("role").GetString());
		Assert.Equal("9c0e31bb753d782ce20710d19828f4e97b4371c8733abd0c5c6f7f485306fb28", header.GetProperty("sha256").GetString());
		Assert.Equal("CommentOnlyDifference", header.GetProperty("relation").GetString());
		JsonElement pascal = sources[3].GetProperty("installed");
		Assert.Equal("MirrorPascal", sources[3].GetProperty("role").GetString());
		Assert.Equal(sources[3].GetProperty("sha256").GetString(), pascal.GetProperty("sha256").GetString());
		Assert.Equal("Identical", pascal.GetProperty("relation").GetString());
		Assert.Equal(JsonValueKind.Null, sources[0].GetProperty("installed").ValueKind);

		// A slot's facts come from the authority: every host field and assignment line is inside a plugin.pas range.
		JsonElement[] ranges = [.. authority.GetProperty("lineRanges").EnumerateArray()];
		Assert.All(Slots, slot =>
		{
			Assert.True(InRange(ranges, slot.GetProperty("hostField").GetProperty("line").GetInt32()));
			Assert.True(InRange(ranges, slot.GetProperty("hostAssignment").GetProperty("line").GetInt32()));
		});

		// A host implementation is only cited for a function address whose target plugin.pas assigns.
		Assert.All(Slots.Where(static slot => slot.GetProperty("hostImplementation").ValueKind != JsonValueKind.Null), static slot =>
		{
			Assert.Equal("FunctionAddress", AssignmentKind(slot));
			Assert.Equal("pluginexports-pas", slot.GetProperty("hostImplementation").GetProperty("source").GetString());
			Assert.Equal("ObservedSource", slot.GetProperty("evidenceKind").GetString());
		});
		Assert.All(Slots.Where(static slot => IsKind(slot, "FunctionAddress") &&
											   slot.GetProperty("hostImplementation").ValueKind == JsonValueKind.Null),
			static slot => Assert.Equal("ToQualify", slot.GetProperty("evidenceKind").GetString()));
	}

	[Fact]
	public void Qualifiable_profile_status_is_not_observed_with_a_reason()
	{
		JsonElement[] statuses = [.. Registry.GetProperty("profileStatus").EnumerateArray()];

		JsonElement qualifiable = Assert.Single(statuses, static status =>
			string.Equals(status.GetProperty("profileId").GetString(), ClassicSlotRegistryContract.QualifiableProfile, StringComparison.Ordinal));
		Assert.Equal("NotObserved", qualifiable.GetProperty("status").GetString());
		Assert.Contains("no managed-hostfxr route reaches the classic table",
			qualifiable.GetProperty("reason").GetString(), StringComparison.Ordinal);
		JsonElement documentary = Assert.Single(statuses, static status =>
			string.Equals(status.GetProperty("profileId").GetString(), ClassicSlotRegistryContract.DocumentaryProfile, StringComparison.Ordinal));
		Assert.Equal("SourceOnly", documentary.GetProperty("status").GetString());
		Assert.All(Slots, static slot => Assert.Equal("NotObserved", slot.GetProperty("hostProfileStatus").GetString()));
	}

	[Fact]
	public void No_slot_is_counted_as_qualified()
	{
		Assert.All(Slots, static slot =>
		{
			Assert.Equal("NotExecuted", slot.GetProperty("qualification").GetString());
			Assert.Equal("Deduced", slot.GetProperty("layoutEvidenceKind").GetString());
			Assert.NotEqual("ObservedHost", slot.GetProperty("evidenceKind").GetString(), StringComparer.Ordinal);
		});
	}

	[Fact]
	public void Lua_equivalents_are_named_only_for_function_slots()
	{
		JsonElement[] withLua = [.. Slots.Where(static slot => slot.GetProperty("luaEquivalent").ValueKind != JsonValueKind.Null)];

		Assert.NotEmpty(withLua);
		Assert.All(withLua, static slot =>
		{
			Assert.Equal("Function", slot.GetProperty("nature").GetString());
			Assert.Equal(HostName(slot), slot.GetProperty("luaEquivalent").GetProperty("name").GetString(),
				StringComparer.OrdinalIgnoreCase);
		});
		Assert.All(Slots, static slot => Assert.Equal(JsonValueKind.Null, slot.GetProperty("catalogSurfaceId").ValueKind));
		Assert.Equal("createForm", Slots[124].GetProperty("luaEquivalent").GetProperty("name").GetString());
	}

	[Fact]
	public void Registry_uses_lowercase_hashes_and_no_absolute_local_path()
	{
		string text = RepositoryDocument.ReadNormalizedText(ClassicSlotRegistryContract.RegistryPath);

		Assert.DoesNotMatch(AbsoluteLocalPath(), text);
		foreach (Match hash in HexHash().Matches(text))
		{
			Assert.Equal(hash.Value.ToLowerInvariant(), hash.Value);
		}

		Assert.True(HexHash().Count(text) >= 6, "The registry lost its source hashes.");
	}

	[Fact]
	public void Registry_is_canonically_formatted()
	{
		string text = RepositoryDocument.ReadNormalizedText(ClassicSlotRegistryContract.RegistryPath);

		Assert.Equal(RepositoryDocument.Canonical(RepositoryDocument.ParseJson(text)), text);
		Assert.EndsWith("}\n", text, StringComparison.Ordinal);
		Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
	}

	[Fact]
	public void Callback_categories_cover_plugin_types_0_to_8()
	{
		JsonElement[] categories = [.. Registry.GetProperty("callbackCategories").EnumerateArray()];

		Assert.Equal([.. Enumerable.Range(0, 9)], categories.Select(static row => row.GetProperty("pluginType").GetInt32()));
		Assert.Equal(
			["ptAddressList", "ptMemoryView", "ptOnDebugEvent", "ptProcesswatcherEvent", "ptFunctionPointerchange", "ptMainMenu",
			 "ptDisassemblerContext", "ptDisassemblerRenderLine", "ptAutoAssembler"],
			categories.Select(static row => row.GetProperty("name").GetString()!), StringComparer.Ordinal);
		Assert.True(categories[2].GetProperty("synchronousDecision").GetBoolean());
		Assert.Equal(1, categories.Count(static row => row.GetProperty("synchronousDecision").GetBoolean()));
		Assert.Equal("WorkerThread", categories[3].GetProperty("context").GetString());
		Assert.Equal([3, 6, 8], categories.Where(static row => row.GetProperty("versionDependent").GetBoolean())
			.Select(static row => row.GetProperty("pluginType").GetInt32()));
		Assert.All(categories, static row => Assert.Equal(row.GetProperty("versionDependent").GetBoolean(),
			row.GetProperty("versionVariants").GetArrayLength() > 0));
		Assert.All(categories, static row => Assert.StartsWith("PluginCallbackShapeTests.",
			row.GetProperty("shapeTest").GetString(), StringComparison.Ordinal));

		// Every named shape test must exist, and none carries the Q38 trait: shape tests test no thread.
		foreach (JsonElement row in categories)
		{
			string[] test = row.GetProperty("shapeTest").GetString()!.Split('.');
			IReadOnlyList<string>? traits = TestMethodTraits.Of(ShapeTestsPath, test[1]);
			Assert.True(traits is not null, $"{ShapeTestsPath} declares no {test[0]}.{test[1]}.");
			Assert.DoesNotContain("Q38", traits, StringComparer.Ordinal);
		}
	}

	private static void AssertProperties(string[] expected, JsonElement obj)
	{
		string[] actual = [.. obj.EnumerateObject().Select(static property => property.Name)];
		foreach (string name in expected)
		{
			Assert.Contains(name, actual);
		}
	}

	private static int[] SlotsWhere(Func<JsonElement, bool> predicate)
	{
		return [.. Slots.Where(predicate).Select(static slot => slot.GetProperty("slot").GetInt32())];
	}

	private static string AssignmentKind(JsonElement slot)
	{
		return slot.GetProperty("hostAssignment").GetProperty("kind").GetString()!;
	}

	private static bool IsKind(JsonElement slot, string kind)
	{
		return string.Equals(AssignmentKind(slot), kind, StringComparison.Ordinal);
	}

	private static string HostName(JsonElement slot)
	{
		return slot.GetProperty("hostField").GetProperty("name").GetString()!;
	}

	private static JsonElement Divergence(JsonElement[] divergences, string id)
	{
		return Assert.Single(divergences, row => string.Equals(row.GetProperty("id").GetString(), id, StringComparison.Ordinal));
	}

	private static int[] Ints(JsonElement row, string property)
	{
		return [.. row.GetProperty(property).EnumerateArray().Select(static item => item.GetInt32())];
	}

	private static string[] Strings(JsonElement row, string property)
	{
		return [.. row.GetProperty(property).EnumerateArray().Select(static item => item.GetString()!)];
	}

	private static bool InRange(JsonElement[] ranges, int line)
	{
		return ranges.Any(range => line >= range.GetProperty("start").GetInt32() && line <= range.GetProperty("end").GetInt32());
	}

	[GeneratedRegex(@"(?<![A-Za-z])[A-Za-z]:(\\|/)|file://|\\Users\\", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex AbsoluteLocalPath();

	[GeneratedRegex("\\b[0-9A-Fa-f]{64}\\b", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex HexHash();
}
