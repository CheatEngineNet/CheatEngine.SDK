using CheatEngine.SDK.NativeAotLibraryProbe;

namespace CheatEngine.SDK.NativeAotLoaderHarness;

/// <summary>
///     The exact export surface the NativeAOT library probe may have (scenario Q41): the probe's required names, the
///     runtime exports a NativeAOT shared library carries by itself, and nothing else. A <c>CEPlugin_*</c> name (a
///     classic Cheat Engine native plugin entry point) is refused first and always.
/// </summary>
/// <remarks>
///     <see cref="AllowedRuntimeExports" /> was measured, not assumed: <c>dotnet publish</c> of
///     <c>tests/CheatEngine.SDK.NativeAotLibraryProbe</c> with .NET SDK 10.0.401 (win-x64, Release, NativeLib=Shared)
///     produced exactly three named exports, the two probe names and <c>DotNetRuntimeDebugHeader</c> (the NativeAOT
///     runtime's debugger discovery header). A new runtime export after an SDK update fails the harness until it is
///     reviewed and listed here. Pure: no I/O, used by <c>Program</c> and by the unit tests.
/// </remarks>
internal static class LibraryProbeContract
{
	/// <summary>The prefix of the classic Cheat Engine native plugin exports.</summary>
	internal const string NativePluginPrefix = "CEPlugin_";

	/// <summary>Exports the NativeAOT runtime adds to every shared library, as observed on the pinned SDK.</summary>
	internal static IReadOnlyList<string> AllowedRuntimeExports
	{
		get;
	} = ["DotNetRuntimeDebugHeader"];

	/// <summary>
	///     Returns one message per violation of <paramref name="exportNames" />: a <c>CEPlugin_*</c> export, a missing
	///     required probe export, or an export that is neither required nor an allowed runtime export. Empty when the
	///     surface is exactly the expected one.
	/// </summary>
	internal static IReadOnlyList<string> FindViolations(IReadOnlyList<string> exportNames)
	{
		ArgumentNullException.ThrowIfNull(exportNames);
		List<string> violations = [];
		foreach (string exportName in exportNames)
		{
			if (exportName.StartsWith(NativePluginPrefix, StringComparison.Ordinal))
			{
				violations.Add(
					$"The harness refuses a DLL that exposes a Cheat Engine native-plugin entry point ('{exportName}').");
			}
		}

		foreach (string requiredName in NativeAotLibraryProbeExportNames.Required)
		{
			if (!Contains(exportNames, requiredName))
			{
				violations.Add($"The file does not expose required fixture export '{requiredName}'.");
			}
		}

		foreach (string exportName in exportNames)
		{
			if (!exportName.StartsWith(NativePluginPrefix, StringComparison.Ordinal) &&
				!Contains(NativeAotLibraryProbeExportNames.Required, exportName) &&
				!Contains(AllowedRuntimeExports, exportName))
			{
				violations.Add($"The file exposes the unexpected export '{exportName}'.");
			}
		}

		return violations;
	}

	/// <summary>Throws when <paramref name="exportNames" /> is not exactly the expected surface.</summary>
	/// <exception cref="InvalidOperationException">The surface has at least one violation; the message lists all.</exception>
	internal static void Validate(IReadOnlyList<string> exportNames)
	{
		IReadOnlyList<string> violations = FindViolations(exportNames);
		if (violations.Count > 0)
		{
			throw new InvalidOperationException(string.Join(" ", violations));
		}
	}

	/// <summary>The exports of <paramref name="exportNames" /> that are allowed runtime exports, in their order.</summary>
	internal static IReadOnlyList<string> RuntimeExportsIn(IReadOnlyList<string> exportNames)
	{
		List<string> runtimeExports = [];
		foreach (string exportName in exportNames)
		{
			if (Contains(AllowedRuntimeExports, exportName))
			{
				runtimeExports.Add(exportName);
			}
		}

		return runtimeExports;
	}

	private static bool Contains(IReadOnlyList<string> names, string name)
	{
		foreach (string candidate in names)
		{
			if (string.Equals(candidate, name, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
