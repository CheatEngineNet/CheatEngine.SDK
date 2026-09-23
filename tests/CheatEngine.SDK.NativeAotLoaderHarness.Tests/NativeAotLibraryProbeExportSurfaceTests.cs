using System.Reflection;
using System.Runtime.InteropServices;

using CheatEngine.SDK.NativeAotLibraryProbe;

namespace CheatEngine.SDK.NativeAotLoaderHarness.Tests;

/// <summary>
///     The probe's source declares exactly the export surface the harness requires: its <c>[UnmanagedCallersOnly]</c>
///     entry points are the two required names, and none is a classic <c>CEPlugin_*</c> plugin entry point. A NativeAOT
///     publication exports only these methods of the published assembly, plus the runtime's own exports.
/// </summary>
public sealed class NativeAotLibraryProbeExportSurfaceTests
{
	[Fact]
	[Trait("Qualification", "Q41")]
	public void Probe_exports_exactly_the_required_names_and_no_CEPlugin_entry_point()
	{
		string[] entryPoints =
		[
			.. typeof(NativeAotLibraryProbeExports)
				.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
				.Select(static method => method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>())
				.Where(static attribute => attribute is not null)
				.Select(static attribute => attribute!.EntryPoint!)
				.Order(StringComparer.Ordinal)
		];

		Assert.Equal(NativeAotLibraryProbeExportNames.Required.Order(StringComparer.Ordinal), entryPoints,
			StringComparer.Ordinal);
		Assert.DoesNotContain(entryPoints,
			static name => name.StartsWith(LibraryProbeContract.NativePluginPrefix, StringComparison.Ordinal));
		Assert.Empty(
			LibraryProbeContract.FindViolations([.. entryPoints, .. LibraryProbeContract.AllowedRuntimeExports]));
	}
}
