using CheatEngine.SDK.NativeAotLibraryProbe;
using CheatEngine.SDK.NativeAotLoaderHarness.Tests.Support;

namespace CheatEngine.SDK.NativeAotLoaderHarness.Tests;

/// <summary>
///     The exact export surface the harness accepts from the NativeAOT library probe (Q41): the two probe names, the
///     measured NativeAOT runtime export, nothing else, and never a <c>CEPlugin_*</c> classic plugin entry point.
/// </summary>
public sealed class LibraryProbeContractTests
{
	private static readonly string[] Expected =
		[.. NativeAotLibraryProbeExportNames.Required, .. LibraryProbeContract.AllowedRuntimeExports];

	[Theory]
	[InlineData("CEPlugin_GetVersion")]
	[InlineData("CEPlugin_InitializePlugin")]
	[InlineData("CEPlugin_DisablePlugin")]
	[InlineData("CEPlugin_Anything")]
	[Trait("Qualification", "Q41")]
	public void Refuses_a_library_that_exports_a_CEPlugin_name(string exportName)
	{
		IReadOnlyList<string> violations = LibraryProbeContract.FindViolations([.. Expected, exportName]);

		string violation = Assert.Single(violations);
		Assert.Contains("Cheat Engine native-plugin entry point", violation, StringComparison.Ordinal);
		Assert.Contains(exportName, violation, StringComparison.Ordinal);
		Assert.Throws<InvalidOperationException>(() => LibraryProbeContract.Validate([.. Expected, exportName]));
	}

	[Fact]
	[Trait("Qualification", "Q41")]
	public void Refuses_a_CEPlugin_export_read_from_the_bytes_of_a_patched_image()
	{
		byte[] image = BridgeImage.Load();
		BridgeImage.RenameExport(image, "cheatengine_sdk_lua_protected", "CEPlugin_GetVersion");

		List<string> names = PortableExecutableExportReader.ReadExportNames(image);

		Assert.Contains("CEPlugin_GetVersion", names, StringComparer.Ordinal);
		Assert.Contains(LibraryProbeContract.FindViolations(names),
			static violation => violation.Contains("'CEPlugin_GetVersion'", StringComparison.Ordinal) &&
			                    violation.Contains("native-plugin entry point", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(NativeAotLibraryProbeExportNames.NameQuery)]
	[InlineData(NativeAotLibraryProbeExportNames.LoadOnly)]
	[Trait("Qualification", "Q41")]
	public void Refuses_a_library_missing_a_required_probe_export(string missing)
	{
		string[] exports = [.. Expected.Where(name => !string.Equals(name, missing, StringComparison.Ordinal))];

		IReadOnlyList<string> violations = LibraryProbeContract.FindViolations(exports);

		Assert.Equal([$"The file does not expose required fixture export '{missing}'."], violations);
		Assert.Throws<InvalidOperationException>(() => LibraryProbeContract.Validate(exports));
	}

	[Theory]
	[InlineData("cheatengine_sdk_lua_protected")]
	[InlineData("DllGetClassObject")]
	[InlineData("CheatEngineSdkNativeAotProbe_Extra")]
	[InlineData("ceplugin_getversion")]
	[Trait("Qualification", "Q41")]
	public void Refuses_an_unexpected_extra_export(string extra)
	{
		IReadOnlyList<string> violations = LibraryProbeContract.FindViolations([.. Expected, extra]);

		Assert.Equal([$"The file exposes the unexpected export '{extra}'."], violations);
		Assert.Throws<InvalidOperationException>(() => LibraryProbeContract.Validate([.. Expected, extra]));
	}

	[Fact]
	[Trait("Qualification", "Q41")]
	public void Accepts_exactly_the_required_probe_exports_and_the_allowed_runtime_exports()
	{
		Assert.Empty(LibraryProbeContract.FindViolations(Expected));
		Assert.Empty(LibraryProbeContract.FindViolations([.. NativeAotLibraryProbeExportNames.Required]));
		LibraryProbeContract.Validate(Expected);
		Assert.Equal(["DotNetRuntimeDebugHeader"], LibraryProbeContract.AllowedRuntimeExports);
		Assert.Equal(["DotNetRuntimeDebugHeader"], LibraryProbeContract.RuntimeExportsIn(Expected));
	}

	[Fact]
	public void The_real_bridge_surface_is_refused_as_a_whole()
	{
		IReadOnlyList<string> violations =
			LibraryProbeContract.FindViolations(PortableExecutableExportReader.ReadExportNames(BridgeImage.Load()));

		Assert.Equal(6, violations.Count);
		Assert.Equal(2,
			violations.Count(static violation =>
				violation.Contains("required fixture export", StringComparison.Ordinal)));
		Assert.Equal(4,
			violations.Count(static violation => violation.Contains("unexpected export", StringComparison.Ordinal)));
	}
}
