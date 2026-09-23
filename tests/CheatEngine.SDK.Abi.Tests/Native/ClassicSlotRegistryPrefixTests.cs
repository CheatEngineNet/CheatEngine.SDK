using System.Reflection;
using System.Text.Json;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Keeps the internal <see cref="ExportedFunctionsPrefix" /> and the committed classic slot registry in lockstep:
///     the prefix is exactly registry slots 0-17, typed only where the registry says so, opaque where the host assigns
///     nil or the declarations diverge, and no ABI type maps a slot beyond it (audit A00-24, A03-14, A03-15, A18-08,
///     A23-DT-03, AX04-01, AX05-15).
/// </summary>
public sealed class ClassicSlotRegistryPrefixTests
{
	private static readonly FieldInfo[] PrefixFields =
		typeof(ExportedFunctionsPrefix).GetFields(FieldLayoutGate.InstanceFields);

	[Fact]
	public void Prefix_fields_are_registry_slots_0_to_17_in_order_with_offsets_and_widths()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		JsonElement[] slots = ClassicSlotRegistry.Slots;

		Assert.Equal(18, PrefixFields.Length);
		for (int slot = 0; slot < PrefixFields.Length; slot++)
		{
			FieldInfo field = PrefixFields[slot];
			JsonElement entry = slots[slot];
			Assert.Equal(entry.GetProperty("sdkField").GetString(), field.Name);
			Assert.Equal(entry.GetProperty("x64Offset").GetInt32(), FieldLayoutGate.OffsetOf(field));
			Assert.Equal(entry.GetProperty("width").GetInt32(), FieldLayoutGate.WidthOf(field));
		}

		Assert.All(slots[18..],
			static entry => Assert.Equal(JsonValueKind.Null, entry.GetProperty("sdkField").ValueKind));
	}

	[Fact]
	public void Direct_prefix_byte_count_is_the_min_declared_size_of_slot_17()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.Equal(ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount,
			ClassicSlotRegistry.MinDeclaredSize(17));
		Assert.Equal(ClassicSlotRegistry.MinDeclaredSize(17), Layout.SizeOf<ExportedFunctionsPrefix>());
		Assert.Equal(ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount,
			ClassicSlotRegistry.Root.GetProperty("contract").GetProperty("sdkDirectPrefixSize").GetInt32());
		Assert.True(
			ClassicExportedFunctionsPrefixReader.DirectPrefixByteCount < ClassicSlotRegistry.MinDeclaredSize(18));
	}

	[Fact]
	public void Typed_prefix_slots_are_exactly_the_registry_prefix_typed_slots()
	{
		JsonElement[] slots = ClassicSlotRegistry.Slots;

		for (int slot = 0; slot < PrefixFields.Length; slot++)
		{
			bool opaque = FieldLayoutGate.KindOf(PrefixFields[slot]) == FieldKind.OpaquePointer;
			string expected = opaque ? "PrefixOpaque" : "PrefixTyped";
			Assert.True(
				string.Equals(expected, slots[slot].GetProperty("sdkExposure").GetString(), StringComparison.Ordinal),
				$"Slot {slot} ({PrefixFields[slot].Name}) is {expected} in the SDK but {slots[slot].GetProperty("sdkExposure").GetString()} in the registry.");
		}
	}

	[Fact]
	public void Nil_or_divergent_prefix_slots_are_opaque_void_pointers()
	{
		JsonElement[] slots = ClassicSlotRegistry.Slots;
		List<int> guarded = [];

		for (int slot = 0; slot < PrefixFields.Length; slot++)
		{
			JsonElement entry = slots[slot];
			bool nil = string.Equals(entry.GetProperty("nullability").GetString(), "NilAssigned",
				StringComparison.Ordinal);
			bool divergent = entry.GetProperty("divergenceRefs").GetArrayLength() > 0;
			if (nil || divergent)
			{
				guarded.Add(slot);
				Type fieldType = PrefixFields[slot].GetModifiedFieldType().UnderlyingSystemType;
				Assert.True(fieldType.IsPointer && fieldType.GetElementType() == typeof(void),
					$"Slot {slot} ({PrefixFields[slot].Name}) is nil or divergent in the registry but typed in the SDK.");
			}
		}

		Assert.Equal([14, 17], guarded);
	}

	[Fact]
	public void No_abi_type_declares_a_field_for_a_suffix_slot()
	{
		HashSet<string> suffixNames = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonElement entry in ClassicSlotRegistry.Slots[18..])
		{
			suffixNames.Add(entry.GetProperty("hostField").GetProperty("name").GetString()!);
			suffixNames.Add(entry.GetProperty("mirrors").GetProperty("headerC").GetProperty("name").GetString()!);
			suffixNames.Add(entry.GetProperty("mirrors").GetProperty("pascalMirror").GetProperty("name").GetString()!);
		}

		foreach (Type structure in AbiStructures.All())
		{
			// The managed exports table is a different route (ADR-02); its own GetLuaState is not classic slot 157.
			if (structure == typeof(ManagedExportedFunctions))
			{
				continue;
			}

			foreach (FieldInfo field in structure.GetFields(FieldLayoutGate.InstanceFields))
			{
				Assert.False(suffixNames.Contains(field.Name),
					$"{structure.FullName}.{field.Name} maps a classic suffix slot; the registry keeps slots 18-158 unexposed.");
			}
		}

		Assert.Contains("GetLuaState", suffixNames);
	}

	[Fact]
	public void Plugin_type_values_equal_the_registry_callback_categories()
	{
		JsonElement[] categories = ClassicSlotRegistry.CallbackCategories;
		PluginType[] members = Enum.GetValues<PluginType>();

		Assert.Equal(categories.Length, members.Length);
		foreach (JsonElement category in categories)
		{
			int value = category.GetProperty("pluginType").GetInt32();
			PluginType member = (PluginType) value;
			Assert.True(Enum.IsDefined(member), $"Plugin type {value} has no PluginType member.");
			Assert.Equal("pt" + member, category.GetProperty("name").GetString(), StringComparer.OrdinalIgnoreCase);

			string recordName = category.GetProperty("sdkRecord").GetString()!;
			Type record = AbiStructures.AbiAssembly.GetType("CheatEngine.SDK.Abi.Native." + recordName, true, false)!;
			FieldInfo callback = record.GetField("Callback", FieldLayoutGate.InstanceFields)!;
			Assert.Equal(category.GetProperty("sdkCallbackTyped").GetBoolean(),
				FieldLayoutGate.KindOf(callback) == FieldKind.FunctionPointer);
		}
	}
}
