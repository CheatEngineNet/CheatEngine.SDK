using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Fixture;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     The type-0 selection record against its oracle and its two known-wrong mirrors. The oracle is the host type
///     actually called, <c>TPlugin0_SelectedRecord</c> of the pinned <c>plugin.pas</c> (lines 726-735,
///     <c>address: ptrUint; ispointer: BOOL</c>), which the C header agrees with. The Pascal kit unit
///     <c>cepluginsdk.pas</c> carries a same-named record with <c>address: dword</c> (lines 161-170) and a
///     <c>TSelectedRecord</c> with <c>ispointer: boolean</c> (lines 147-156). All three are 48 bytes on x64, so only a
///     per-field check separates them (audit annex 05, A03-31, AX05-07).
/// </summary>
/// <remarks>
///     The mirrors are modelled here as nested C# structures, never in <c>CheatEngine.SDK.Abi</c>. The CI-only fixture test
///     compares the same three layouts as compiled by MSVC from the transcriptions in
///     <c>tests/native-abi-fixture/ce77_plugin_abi_contract.h</c>. Nothing here observes the CE 7.7 binary.
/// </remarks>
public sealed class SelectedRecordOracleTests
{
	private const string HostType = "CheatEngine.SDK.Abi.Native.PluginType0Record";

	/// <summary>The host type of plugin.pas as audit annex 05 tabulates it: field, offset, width.</summary>
	private static readonly (string Field, int Offset, int Width)[] HostLayout =
	[
		("InterpretedAddress", 0, 8),
		("Address", 8, 8),
		("IsPointer", 16, 4),
		("CountOffsets", 20, 4),
		("Offsets", 24, 8),
		("Description", 32, 8),
		("ValueType", 40, 1),
		("Size", 41, 1)
	];

	[Fact]
	public void PluginType0Record_matches_the_pinned_host_type_field_by_field()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.Equal(48, Layout.SizeOf<PluginType0Record>());
		Assert.Empty(FieldLayoutGate.FindViolations(typeof(PluginType0Record), HostRows(HostType, FieldKindsOfSdkRecord())));
		Assert.Equal(HostLayout.Select(static row => row.Field),
			typeof(PluginType0Record).GetFields(FieldLayoutGate.InstanceFields).Select(static field => field.Name),
			StringComparer.Ordinal);
	}

	[Fact]
	public void Pascal_dword_mirror_has_the_same_size_but_different_offsets()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.Equal(48, Layout.SizeOf<PascalDwordMirrorSelectedRecord>());
		Assert.Equal(4, Width<PascalDwordMirrorSelectedRecord>("Address"));
		Assert.Equal(12, Offset<PascalDwordMirrorSelectedRecord>("IsPointer"));
		Assert.Equal(16, Offset<PascalDwordMirrorSelectedRecord>("CountOffsets"));
		Assert.Equal(24, Offset<PascalDwordMirrorSelectedRecord>("Offsets"));

		Assert.NotEqual(Offset<PluginType0Record>("IsPointer"), Offset<PascalDwordMirrorSelectedRecord>("IsPointer"));
		Assert.NotEqual(Offset<PluginType0Record>("CountOffsets"),
			Offset<PascalDwordMirrorSelectedRecord>("CountOffsets"));
		Assert.NotEqual(Width<PluginType0Record>("Address"), Width<PascalDwordMirrorSelectedRecord>("Address"));
	}

	[Fact]
	public void Pascal_boolean_mirror_has_the_same_offsets_but_a_one_byte_IsPointer()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.Equal(48, Layout.SizeOf<PascalBooleanMirrorSelectedRecord>());
		foreach ((string field, int offset, int _) in HostLayout)
		{
			Assert.Equal(offset, Offset<PascalBooleanMirrorSelectedRecord>(field));
			Assert.Equal(Offset<PluginType0Record>(field), Offset<PascalBooleanMirrorSelectedRecord>(field));
		}

		Assert.Equal(1, Width<PascalBooleanMirrorSelectedRecord>("IsPointer"));
		Assert.Equal(4, Width<PluginType0Record>("IsPointer"));
	}

	[Fact]
	public void Size_only_check_cannot_tell_the_host_type_from_either_mirror()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		// What a size-only gate sees: three identical numbers.
		Assert.Equal(Layout.SizeOf<PluginType0Record>(), Layout.SizeOf<PascalDwordMirrorSelectedRecord>());
		Assert.Equal(Layout.SizeOf<PluginType0Record>(), Layout.SizeOf<PascalBooleanMirrorSelectedRecord>());

		// What the per-field gate sees against the host oracle: the SDK record passes, both mirrors fail.
		Assert.Empty(FieldLayoutGate.FindViolations(typeof(PluginType0Record), HostRows(HostType, FieldKindsOfSdkRecord())));
		Assert.Contains(FieldLayoutGate.FindViolations(typeof(PascalDwordMirrorSelectedRecord),
			HostRows(typeof(PascalDwordMirrorSelectedRecord).FullName!, null)), IsOffsetOrWidth);
		Assert.Contains(FieldLayoutGate.FindViolations(typeof(PascalBooleanMirrorSelectedRecord),
			HostRows(typeof(PascalBooleanMirrorSelectedRecord).FullName!, null)), IsOffsetOrWidth);
	}

	[Fact]
	public void Native_fixture_host_and_mirror_transcriptions_match_the_managed_oracle_when_CI_supplies_them()
	{
		Dictionary<string, string>? facts = NativeAbiFixtureFacts.LoadFromEnvironment();
		if (facts is null)
		{
			Assert.Null(facts);
			return;
		}

		Assert.True(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		AssertFixtureEquals<PluginType0Record>(facts, "host_plugin0_selected_record");
		AssertFixtureEquals<PascalDwordMirrorSelectedRecord>(facts, "pascal_dword_mirror_selected_record");
		AssertFixtureEquals<PascalBooleanMirrorSelectedRecord>(facts, "pascal_boolean_mirror_selected_record");

		// The SDK record differs from each compiled mirror exactly where annex 05 says it does.
		Assert.NotEqual(facts["offsetof.pascal_dword_mirror_selected_record.IsPointer"],
			Text(Offset<PluginType0Record>("IsPointer")), StringComparer.Ordinal);
		Assert.NotEqual(facts["fieldsize.pascal_dword_mirror_selected_record.Address"],
			Text(Width<PluginType0Record>("Address")), StringComparer.Ordinal);
		Assert.Equal(facts["offsetof.pascal_boolean_mirror_selected_record.IsPointer"],
			Text(Offset<PluginType0Record>("IsPointer")), StringComparer.Ordinal);
		Assert.NotEqual(facts["fieldsize.pascal_boolean_mirror_selected_record.IsPointer"],
			Text(Width<PluginType0Record>("IsPointer")), StringComparer.Ordinal);
	}

	private static void AssertFixtureEquals<T>(Dictionary<string, string> facts, string key)
		where T : unmanaged
	{
		Dictionary<string, string> managed = new(StringComparer.Ordinal);
		NativeAbiFixtureManagedComparisonTests.AddRecord<T>(managed, [], key);
		foreach (KeyValuePair<string, string> fact in managed)
		{
			Assert.True(facts.TryGetValue(fact.Key, out string? native), $"The native fixture omitted '{fact.Key}'.");
			Assert.True(string.Equals(fact.Value, native, StringComparison.Ordinal),
				$"'{fact.Key}': managed {fact.Value}, native {native}.");
		}
	}

	private static FieldLayoutRow[] HostRows(string typeFullName, Dictionary<string, FieldKind>? kinds)
	{
		return
		[
			.. HostLayout.Select(row => new FieldLayoutRow(typeFullName, row.Field, row.Offset, row.Width,
				kinds?[row.Field] ?? KindOf(typeFullName, row.Field)))
		];
	}

	private static Dictionary<string, FieldKind> FieldKindsOfSdkRecord()
	{
		return new Dictionary<string, FieldKind>(StringComparer.Ordinal)
		{
			["InterpretedAddress"] = FieldKind.Pointer,
			["Address"] = FieldKind.Integer,
			["IsPointer"] = FieldKind.AbiBoolean,
			["CountOffsets"] = FieldKind.Integer,
			["Offsets"] = FieldKind.Pointer,
			["Description"] = FieldKind.Pointer,
			["ValueType"] = FieldKind.Integer,
			["Size"] = FieldKind.Integer
		};
	}

	private static FieldKind KindOf(string typeFullName, string fieldName)
	{
		Type type = typeof(SelectedRecordOracleTests).Assembly.GetType(typeFullName, true, false)!;
		return FieldLayoutGate.KindOf(type.GetField(fieldName, FieldLayoutGate.InstanceFields)!);
	}

	private static bool IsOffsetOrWidth(string violation)
	{
		return violation.Contains(": offset ", StringComparison.Ordinal) ||
			   violation.Contains(": width ", StringComparison.Ordinal);
	}

	private static int Offset<T>(string fieldName)
	{
		return FieldLayoutGate.OffsetOf(FieldOf<T>(fieldName));
	}

	private static int Width<T>(string fieldName)
	{
		return FieldLayoutGate.WidthOf(FieldOf<T>(fieldName));
	}

	private static FieldInfo FieldOf<T>(string fieldName)
	{
		return typeof(T).GetField(fieldName, FieldLayoutGate.InstanceFields)
			   ?? throw new InvalidOperationException($"{typeof(T).Name} has no field {fieldName}.");
	}

	private static string Text(int value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>
	///     C# model of the kit mirror <c>TPlugin0_SelectedRecord</c> (<c>cepluginsdk.pas</c> lines 161-170): a 32-bit
	///     address moves <c>IsPointer</c> and <c>CountOffsets</c> 4 bytes down; natural alignment keeps 48 bytes.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct PascalDwordMirrorSelectedRecord
	{
		public byte* InterpretedAddress;
		public uint Address;
		public Bool32 IsPointer;
		public int CountOffsets;
		public uint* Offsets;
		public byte* Description;
		public byte ValueType;
		public byte Size;
	}

	/// <summary>
	///     C# model of the kit mirror <c>TSelectedRecord</c> (<c>cepluginsdk.pas</c> lines 147-156): the host offsets, but
	///     a one-byte Pascal <c>boolean</c> pointer flag.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct PascalBooleanMirrorSelectedRecord
	{
		public byte* InterpretedAddress;
		public nuint Address;
		public byte IsPointer;
		public int CountOffsets;
		public uint* Offsets;
		public byte* Description;
		public byte ValueType;
		public byte Size;
	}
}
