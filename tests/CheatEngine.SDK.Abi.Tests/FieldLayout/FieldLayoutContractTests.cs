using System.Reflection;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.FieldLayout;

/// <summary>
///     Mechanical per-field layout gate: every instance field of every structure of <c>CheatEngine.SDK.Abi</c> (public,
///     internal and nested private, see <see cref="AbiStructures" />) has a literal row in
///     <see cref="FieldLayoutExpectations" />, and its x64 offset, width and kind equal that row. Adding, removing,
///     moving or retyping a field without editing the table fails here, so a new structure can no longer pass with a size
///     row but no per-field test.
/// </summary>
/// <remarks>
///     Offsets are measured by <see cref="FieldLayoutGate.OffsetOf" /> with the IL instruction <c>ldflda</c> (the managed
///     layout a plugin really uses, not the marshaller's unmanaged view);
///     <see cref="Reflected_offsets_agree_with_address_of_offsets_for_the_packed_init_record_and_the_managed_exports" />
///     proves on the packed init record, the managed exports, the selection record and the classic prefix that this
///     measurement equals the C# address-of arithmetic of <see cref="Layout" />.
/// </remarks>
public sealed unsafe class FieldLayoutContractTests
{
	[Fact]
	public void Every_instance_field_of_every_abi_structure_has_a_layout_row()
	{
		HashSet<(string, string)> rows = FieldLayoutExpectations.Rows
			.Select(static row => (row.TypeFullName, row.FieldName)).ToHashSet();
		List<string> missing = [];

		foreach (Type structure in AbiStructures.All())
		{
			foreach (FieldInfo field in structure.GetFields(FieldLayoutGate.InstanceFields))
			{
				if (!rows.Contains((structure.FullName!, field.Name)))
				{
					missing.Add($"{structure.FullName}.{field.Name}");
				}
			}
		}

		Assert.True(missing.Count == 0, "Fields without a layout row: " + string.Join(", ", missing));
		Assert.Equal(20, AbiStructures.All().Count());
	}

	[Fact]
	public void Every_layout_row_names_an_existing_field()
	{
		Dictionary<string, Type> structures =
			AbiStructures.All().ToDictionary(static type => type.FullName!, StringComparer.Ordinal);
		List<string> stale = [];
		HashSet<(string, string)> seen = [];

		foreach (FieldLayoutRow row in FieldLayoutExpectations.Rows)
		{
			if (!seen.Add((row.TypeFullName, row.FieldName)))
			{
				stale.Add($"{row.TypeFullName}.{row.FieldName} (duplicate)");
			}
			else if (!structures.TryGetValue(row.TypeFullName, out Type? structure) ||
			         structure.GetField(row.FieldName, FieldLayoutGate.InstanceFields) is null)
			{
				stale.Add($"{row.TypeFullName}.{row.FieldName}");
			}
		}

		Assert.True(stale.Count == 0, "Layout rows naming no field: " + string.Join(", ", stale));
	}

	[Fact]
	[Trait("Qualification", "Q01")]
	public void Every_field_has_the_expected_offset_on_64_bit()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		List<string> wrong = [];
		foreach (FieldLayoutRow row in FieldLayoutExpectations.Rows)
		{
			FieldInfo field = Field(row);
			int offset = FieldLayoutGate.OffsetOf(field);
			if (offset != row.Offset)
			{
				wrong.Add($"{row.TypeFullName}.{row.FieldName}: {offset} (expected {row.Offset})");
			}
		}

		Assert.True(wrong.Count == 0, "Wrong offsets: " + string.Join(", ", wrong));
	}

	[Fact]
	[Trait("Qualification", "Q01")]
	public void Every_field_has_the_expected_width_on_64_bit()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		List<string> wrong = [];
		foreach (FieldLayoutRow row in FieldLayoutExpectations.Rows)
		{
			FieldInfo field = Field(row);
			int width = FieldLayoutGate.WidthOf(field);
			if (width != row.Width)
			{
				wrong.Add($"{row.TypeFullName}.{row.FieldName}: {width} (expected {row.Width})");
			}
		}

		Assert.True(wrong.Count == 0, "Wrong widths: " + string.Join(", ", wrong));
	}

	[Fact]
	public void Every_field_has_the_expected_kind()
	{
		List<string> wrong = [];
		foreach (FieldLayoutRow row in FieldLayoutExpectations.Rows)
		{
			FieldKind kind = FieldLayoutGate.KindOf(Field(row));
			if (kind != row.Kind)
			{
				wrong.Add($"{row.TypeFullName}.{row.FieldName}: {kind} (expected {row.Kind})");
			}
		}

		Assert.True(wrong.Count == 0, "Wrong kinds: " + string.Join(", ", wrong));
	}

	[Fact]
	public void Every_abi_structure_passes_the_gate()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		List<string> violations = [];
		foreach (Type structure in AbiStructures.All())
		{
			violations.AddRange(FieldLayoutGate.FindViolations(structure, FieldLayoutExpectations.Rows));
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void Reflected_offsets_agree_with_address_of_offsets_for_the_packed_init_record_and_the_managed_exports()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		PluginInitRecord init = default;
		AssertSameOffset<PluginInitRecord>(nameof(PluginInitRecord.Name), Layout.OffsetOf(&init, &init.Name));
		AssertSameOffset<PluginInitRecord>(nameof(PluginInitRecord.GetVersion),
			Layout.OffsetOf(&init, &init.GetVersion));
		AssertSameOffset<PluginInitRecord>(nameof(PluginInitRecord.EnablePlugin),
			Layout.OffsetOf(&init, &init.EnablePlugin));
		AssertSameOffset<PluginInitRecord>(nameof(PluginInitRecord.DisablePlugin),
			Layout.OffsetOf(&init, &init.DisablePlugin));
		AssertSameOffset<PluginInitRecord>(nameof(PluginInitRecord.Version), Layout.OffsetOf(&init, &init.Version));

		ManagedExportedFunctions exports = default;
		AssertSameOffset<ManagedExportedFunctions>(nameof(ManagedExportedFunctions.SizeOfExportedFunctions),
			Layout.OffsetOf(&exports, &exports.SizeOfExportedFunctions));
		AssertSameOffset<ManagedExportedFunctions>(nameof(ManagedExportedFunctions.GetLuaState),
			Layout.OffsetOf(&exports, &exports.GetLuaState));
		AssertSameOffset<ManagedExportedFunctions>(nameof(ManagedExportedFunctions.LuaRegister),
			Layout.OffsetOf(&exports, &exports.LuaRegister));
		AssertSameOffset<ManagedExportedFunctions>(nameof(ManagedExportedFunctions.LuaPushClassInstance),
			Layout.OffsetOf(&exports, &exports.LuaPushClassInstance));
		AssertSameOffset<ManagedExportedFunctions>(nameof(ManagedExportedFunctions.ProcessMessages),
			Layout.OffsetOf(&exports, &exports.ProcessMessages));
		AssertSameOffset<ManagedExportedFunctions>(nameof(ManagedExportedFunctions.CheckSynchronize),
			Layout.OffsetOf(&exports, &exports.CheckSynchronize));

		PluginType0Record record = default;
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.InterpretedAddress),
			Layout.OffsetOf(&record, &record.InterpretedAddress));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.Address),
			Layout.OffsetOf(&record, &record.Address));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.IsPointer),
			Layout.OffsetOf(&record, &record.IsPointer));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.CountOffsets),
			Layout.OffsetOf(&record, &record.CountOffsets));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.Offsets),
			Layout.OffsetOf(&record, &record.Offsets));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.Description),
			Layout.OffsetOf(&record, &record.Description));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.ValueType),
			Layout.OffsetOf(&record, &record.ValueType));
		AssertSameOffset<PluginType0Record>(nameof(PluginType0Record.Size), Layout.OffsetOf(&record, &record.Size));

		ExportedFunctionsPrefix prefix = default;
		AssertSameOffset<ExportedFunctionsPrefix>(nameof(ExportedFunctionsPrefix.SizeOfExportedFunctions),
			Layout.OffsetOf(&prefix, &prefix.SizeOfExportedFunctions));
		AssertSameOffset<ExportedFunctionsPrefix>(nameof(ExportedFunctionsPrefix.OpenedProcessId),
			Layout.OffsetOf(&prefix, &prefix.OpenedProcessId));
		AssertSameOffset<ExportedFunctionsPrefix>(nameof(ExportedFunctionsPrefix.FixMemory),
			Layout.OffsetOf(&prefix, &prefix.FixMemory));
		AssertSameOffset<ExportedFunctionsPrefix>(nameof(ExportedFunctionsPrefix.GetAddressFromPointer),
			Layout.OffsetOf(&prefix, &prefix.GetAddressFromPointer));
	}

	[Fact]
	public void Gate_reports_a_missing_row_an_extra_row_a_wrong_offset_and_a_wrong_width()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		string probe = typeof(Probe).FullName!;
		FieldLayoutRow first = new(probe, nameof(Probe.First), 0, 4, FieldKind.Integer);
		FieldLayoutRow second = new(probe, nameof(Probe.Second), 8, 8, FieldKind.Integer);
		FieldLayoutRow third = new(probe, nameof(Probe.Third), 16, 8, FieldKind.OpaquePointer);

		Assert.Empty(FieldLayoutGate.FindViolations(typeof(Probe), [first, second, third]));

		Assert.Contains(FieldLayoutGate.FindViolations(typeof(Probe), [first, second]),
			static violation => violation.EndsWith("Probe.Third: missing layout row for this field.",
				StringComparison.Ordinal));
		Assert.Contains(FieldLayoutGate.FindViolations(typeof(Probe),
				[first, second, third, new FieldLayoutRow(probe, "Fourth", 24, 4, FieldKind.Integer)]),
			static violation => violation.EndsWith("Probe.Fourth: extra layout row names no field of the structure.",
				StringComparison.Ordinal));
		Assert.Contains(FieldLayoutGate.FindViolations(typeof(Probe), [first, second with { Offset = 4 }, third]),
			static violation => violation.EndsWith("Probe.Second: offset 8, expected 4.", StringComparison.Ordinal));
		Assert.Contains(FieldLayoutGate.FindViolations(typeof(Probe), [first, second with { Width = 4 }, third]),
			static violation => violation.EndsWith("Probe.Second: width 8, expected 4.", StringComparison.Ordinal));
		Assert.Contains(
			FieldLayoutGate.FindViolations(typeof(Probe), [first, second, third with { Kind = FieldKind.Pointer }]),
			static violation => violation.EndsWith("Probe.Third: kind OpaquePointer, expected Pointer.",
				StringComparison.Ordinal));
		Assert.Contains(FieldLayoutGate.FindViolations(typeof(Probe), [first, first, second, third]),
			static violation => violation.EndsWith("Probe.First: duplicate layout row.", StringComparison.Ordinal));
	}

	[Fact]
	public void Gate_catches_a_size_preserving_retype_that_a_size_check_misses()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		string mirror = typeof(ShiftedMirror).FullName!;
		FieldLayoutRow[] hostRows =
		[
			new(mirror, nameof(ShiftedMirror.First), 0, 4, FieldKind.Integer),
			new(mirror, nameof(ShiftedMirror.Second), 8, 8, FieldKind.Integer),
			new(mirror, nameof(ShiftedMirror.Third), 16, 8, FieldKind.OpaquePointer)
		];

		Assert.Equal(Layout.SizeOf<Probe>(), Layout.SizeOf<ShiftedMirror>());
		IReadOnlyList<string> violations = FieldLayoutGate.FindViolations(typeof(ShiftedMirror), hostRows);
		Assert.Contains(violations, static violation => violation.Contains(
			"ShiftedMirror.Second: offset 4, expected 8.",
			StringComparison.Ordinal));
		Assert.Contains(violations, static violation => violation.Contains("ShiftedMirror.Second: width 4, expected 8.",
			StringComparison.Ordinal));
	}

	private static FieldInfo Field(FieldLayoutRow row)
	{
		Type structure = AbiStructures.AbiAssembly.GetType(row.TypeFullName, true, false)!;
		return structure.GetField(row.FieldName, FieldLayoutGate.InstanceFields)
		       ?? throw new InvalidOperationException($"{row.TypeFullName} has no field {row.FieldName}.");
	}

	private static void AssertSameOffset<T>(string fieldName, int addressOfOffset)
		where T : unmanaged
	{
		FieldInfo field = typeof(T).GetField(fieldName, FieldLayoutGate.InstanceFields)
		                  ?? throw new InvalidOperationException($"{typeof(T).Name} has no field {fieldName}.");
		Assert.Equal(addressOfOffset, FieldLayoutGate.OffsetOf(field));
	}

	/// <summary>A conforming three-field structure the gate is exercised against.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct Probe
	{
		public int First;
		public long Second;
		public void* Third;
	}

	/// <summary>
	///     Same 24 bytes as <see cref="Probe" />, but the second field is 32-bit: every later offset moves, exactly the
	///     defect of a Pascal mirror that declares an address as <c>dword</c>.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct ShiftedMirror
	{
		public int First;
		public int Second;
		public long Padding;
		public void* Third;
	}
}
