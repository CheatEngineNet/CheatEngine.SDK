using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;

namespace CheatEngine.SDK.Hosting.Tests.Coexistence;

/// <summary>
///     Consumes <c>tests/native-host-emulator</c>'s built exe against the two Coexistence plugins' prebuilt output
///     (WI-7, SDK-COEX-1, F03, Q09). C2 evidence only: a native hostfxr host, not Cheat Engine. See
///     <see cref="NativeHostEmulatorEnvironment" /> for the opt-in shape and
///     <c>tests/native-host-emulator/README.md</c>, "Known local dependency", for why every emulator-dependent test
///     below is currently red until a fix lands outside this lot's owned files.
/// </summary>
public sealed unsafe partial class NativeHostEmulatorTests
{
	[Fact]
	public void Required_mode_rejects_an_absent_emulator_directory()
	{
		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() =>
				NativeHostEmulatorEnvironment.ResolveDirectory(null, "true"));

		Assert.Equal(
			$"'{NativeHostEmulatorEnvironment.RequiredEnvironmentVariable}=true' requires '{NativeHostEmulatorEnvironment.DirectoryEnvironmentVariable}' to name a built native host emulator directory.",
			exception.Message);
	}

	[Fact]
	public void Emulator_is_optional_when_required_mode_is_off()
	{
		Assert.Null(NativeHostEmulatorEnvironment.ResolveDirectory(null, null));
	}

	[Fact]
	public void Emulator_abi_header_declares_the_managed_record_sizes()
	{
		string headerPath = Path.Combine(CoexistencePluginLayout.RepoRoot, "tests", "native-host-emulator",
			"ce_host_emulator_abi.h");
		string headerText = File.ReadAllText(headerPath);

		Dictionary<string, int> declaredSizes = new(StringComparer.Ordinal);
		foreach (Match match in StaticAssertSizeOf().Matches(headerText))
		{
			declaredSizes[match.Groups["type"].Value] =
				int.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
		}

		Assert.Equal(36, declaredSizes["CePluginInitRecord"]);
		Assert.Equal(sizeof(PluginInitRecord), declaredSizes["CePluginInitRecord"]);
		Assert.Equal(48, declaredSizes["CeManagedExportedFunctions"]);
		Assert.Equal(sizeof(ManagedExportedFunctions), declaredSizes["CeManagedExportedFunctions"]);
		Assert.Equal(16, declaredSizes["CePluginVersion"]);
		Assert.Equal(sizeof(PluginVersion), declaredSizes["CePluginVersion"]);
	}

	[Trait("Qualification", "Q09.b")]
	[Fact]
	public void Separate_folders_measure_distinct_hosting_instances_and_disabling_A_leaves_B_callable()
	{
		string? emulatorDirectory = NativeHostEmulatorEnvironment.FromEnvironment();
		if (emulatorDirectory is null)
		{
			Assert.Null(emulatorDirectory);
			return;
		}

		NativeHostEmulatorResult result = RunSeparateComponentScenario(emulatorDirectory, out string factsPath);

		Assert.True(0 == result.ExitCode, DescribeFailure("Q09.b", result, factsPath));
		Assert.Equal("separate", result.Facts["layout"]);
		Assert.Equal("component", result.Facts["alc.route"]);
		AssertBootstrapAndEnableSucceeded(result, "a", factsPath);
		AssertBootstrapAndEnableSucceeded(result, "b", factsPath);

		// The measured relation: the component route isolates each assembly path into its own ALC, so two distinct
		// output directories give two distinct PluginHost static instances.
		Assert.Equal("true", result.Facts["ab.hosting_type_handle_distinct"]);

		Assert.Equal("nil", result.Facts["after_disable_a.a_identity_type"]);
		Assert.Equal("nil", result.Facts["after_disable_a.a_ping_type"]);
		Assert.Equal("ok", result.Facts["after_disable_a.b_identity_call"]);
		Assert.True(
			TryParseInt(result.Facts["after_disable_a.b_ping"], out int pingAfterDisableA) && pingAfterDisableA > 0,
			DescribeFailure("Q09.b", result, factsPath));
	}

	[Trait("Qualification", "Q09.a")]
	[Fact]
	public void Shared_folder_under_the_component_route_is_measured_and_disabling_A_leaves_B_callable()
	{
		string? emulatorDirectory = NativeHostEmulatorEnvironment.FromEnvironment();
		if (emulatorDirectory is null)
		{
			Assert.Null(emulatorDirectory);
			return;
		}

		string sharedDirectory =
			Path.Combine(Path.GetTempPath(), "cesdk-native-host-emulator-" + Guid.NewGuid().ToString("N"));
		try
		{
			CoexistencePluginLayout.CreateSharedLayout(sharedDirectory);
			string factsPath = Path.Combine(sharedDirectory, "facts-q09a.txt");
			NativeHostEmulatorResult result = NativeHostEmulatorRunner.Run(
				emulatorDirectory, sharedDirectory, sharedDirectory, NativeHostEmulatorAlcRoute.Component, factsPath);

			Assert.True(0 == result.ExitCode, DescribeFailure("Q09.a", result, factsPath));
			Assert.Equal("shared", result.Facts["layout"]);
			Assert.Equal("component", result.Facts["alc.route"]);
			AssertBootstrapAndEnableSucceeded(result, "a", factsPath);
			AssertBootstrapAndEnableSucceeded(result, "b", factsPath);

			// Reported, not presumed (s-host.md WI-7): the component route keys isolation on the assembly path, so a
			// shared output folder measures the same "distinct instances" relation as separate folders.
			Assert.Equal("true", result.Facts["ab.hosting_mvid_equal"]);
			Assert.Equal("true", result.Facts["ab.hosting_type_handle_distinct"]);

			Assert.Equal("ok", result.Facts["after_disable_a.b_identity_call"]);
			Assert.True(TryParseInt(result.Facts["after_disable_a.b_ping"], out int ping) && ping > 0,
				DescribeFailure("Q09.a", result, factsPath));
		}
		finally
		{
			TryDeleteDirectory(sharedDirectory);
		}
	}

	[Trait("Qualification", "Q09")]
	[Fact]
	public void Default_context_loading_rejects_the_second_plugin_deterministically()
	{
		string? emulatorDirectory = NativeHostEmulatorEnvironment.FromEnvironment();
		if (emulatorDirectory is null)
		{
			Assert.Null(emulatorDirectory);
			return;
		}

		string sharedDirectory =
			Path.Combine(Path.GetTempPath(), "cesdk-native-host-emulator-" + Guid.NewGuid().ToString("N"));
		try
		{
			CoexistencePluginLayout.CreateSharedLayout(sharedDirectory);
			string factsPath = Path.Combine(sharedDirectory, "facts-q09.txt");
			NativeHostEmulatorResult result = NativeHostEmulatorRunner.Run(
				emulatorDirectory, sharedDirectory, sharedDirectory, NativeHostEmulatorAlcRoute.Default, factsPath);

			Assert.True(0 == result.ExitCode, DescribeFailure("Q09", result, factsPath));
			Assert.Equal("default", result.Facts["alc.route"]);

			// A (the first factory registered in the shared Hosting instance) succeeds.
			Assert.Equal("ok", result.Facts["a.bootstrap.second"]);
			Assert.Equal("ok", result.Facts["a.enable.1"]);
			Assert.Contains("Default", result.Facts["a.identity.plugin_alc"], StringComparison.Ordinal);

			// B shares that same Hosting instance (the default ALC never isolates it), so PluginHost's "one factory
			// per loaded Hosting instance" rule (PluginHost.cs TryRegisterFactory) rejects it deterministically, at
			// the bootstrap call itself -- this is measured from the real generated bootstrap, not asserted from
			// documentation.
			Assert.Equal("failed", result.Facts["b.bootstrap.first"]);
			Assert.Equal("failed", result.Facts["b.bootstrap.second"]);
			Assert.Equal("skipped", result.Facts["b.enable.1"]);
		}
		finally
		{
			TryDeleteDirectory(sharedDirectory);
		}
	}

	[Trait("Qualification", "Q02")]
	[Fact]
	public void Bootstrap_canaries_and_name_pointers_stay_intact_for_both_plugins()
	{
		string? emulatorDirectory = NativeHostEmulatorEnvironment.FromEnvironment();
		if (emulatorDirectory is null)
		{
			Assert.Null(emulatorDirectory);
			return;
		}

		NativeHostEmulatorResult result = RunSeparateComponentScenario(emulatorDirectory, out string factsPath);

		Assert.True(0 == result.ExitCode, DescribeFailure("Q02", result, factsPath));
		Assert.Equal("intact", result.Facts["a.bootstrap.guard"]);
		Assert.Equal("intact", result.Facts["b.bootstrap.guard"]);
		Assert.Equal("true", result.Facts["a.bootstrap.name_pointer_stable"]);
		Assert.Equal("true", result.Facts["b.bootstrap.name_pointer_stable"]);
	}

	[Trait("Qualification", "Q05")]
	[Fact]
	public void Reenabling_A_answers_with_a_new_epoch()
	{
		string? emulatorDirectory = NativeHostEmulatorEnvironment.FromEnvironment();
		if (emulatorDirectory is null)
		{
			Assert.Null(emulatorDirectory);
			return;
		}

		NativeHostEmulatorResult result = RunSeparateComponentScenario(emulatorDirectory, out string factsPath);

		Assert.True(0 == result.ExitCode, DescribeFailure("Q05", result, factsPath));
		Assert.Equal("ok", result.Facts["a.enable.2"]);
		Assert.True(TryParseInt(result.Facts["a.identity.epoch"], out int firstEpoch),
			DescribeFailure("Q05", result, factsPath));
		Assert.True(TryParseInt(result.Facts["a.identity.epoch_after_reenable"], out int reenabledEpoch),
			DescribeFailure("Q05", result, factsPath));
		Assert.True(reenabledEpoch > firstEpoch, DescribeFailure("Q05", result, factsPath));
	}

	[Fact]
	public void Emulator_facts_contain_no_absolute_path()
	{
		string? emulatorDirectory = NativeHostEmulatorEnvironment.FromEnvironment();
		if (emulatorDirectory is null)
		{
			Assert.Null(emulatorDirectory);
			return;
		}

		NativeHostEmulatorResult result = RunSeparateComponentScenario(emulatorDirectory, out _);

		foreach (KeyValuePair<string, string> fact in result.Facts)
		{
			Assert.False(AbsolutePathPattern().IsMatch(fact.Value),
				$"Fact '{fact.Key}'='{fact.Value}' contains what looks like an absolute path.");
		}
	}

	private static NativeHostEmulatorResult RunSeparateComponentScenario(string emulatorDirectory, out string factsPath)
	{
		factsPath = Path.Combine(Path.GetTempPath(), $"cesdk-native-host-emulator-{Guid.NewGuid():N}.txt");
		return NativeHostEmulatorRunner.Run(
			emulatorDirectory,
			CoexistencePluginLayout.PluginADirectory,
			CoexistencePluginLayout.PluginBDirectory,
			NativeHostEmulatorAlcRoute.Component,
			factsPath);
	}

	private static void AssertBootstrapAndEnableSucceeded(NativeHostEmulatorResult result, string label,
		string factsPath)
	{
		Assert.True(string.Equals("ok", result.Facts[$"{label}.bootstrap.first"], StringComparison.Ordinal),
			DescribeFailure(label, result, factsPath));
		Assert.True(string.Equals("ok", result.Facts[$"{label}.bootstrap.second"], StringComparison.Ordinal),
			DescribeFailure(label, result, factsPath));
		Assert.True(string.Equals("ok", result.Facts[$"{label}.getversion"], StringComparison.Ordinal),
			DescribeFailure(label, result, factsPath));
		Assert.True(string.Equals("ok", result.Facts[$"{label}.enable.1"], StringComparison.Ordinal),
			DescribeFailure(label, result, factsPath));
	}

	private static bool TryParseInt(string text, out int value)
	{
		return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	private static string DescribeFailure(string label, NativeHostEmulatorResult result, string factsPath)
	{
		StringBuilder builder = new();
		builder.Append(CultureInfo.InvariantCulture, $"[{label}] exit={result.ExitCode} facts='{factsPath}':");
		foreach (KeyValuePair<string, string> fact in result.Facts)
		{
			builder.Append(CultureInfo.InvariantCulture, $" {fact.Key}={fact.Value};");
		}

		return builder.ToString();
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, true);
			}
		}
		catch (IOException)
		{
			// Best-effort cleanup: a lingering file handle on a temp copy of the emulator's inputs never fails the test.
		}
		catch (UnauthorizedAccessException)
		{
			// Same rationale as above.
		}
	}

	[GeneratedRegex(@"static_assert\(sizeof\((?<type>\w+)\)\s*==\s*(?<size>\d+)", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex StaticAssertSizeOf();

	[GeneratedRegex(@"[A-Za-z]:[\\/]|\\\\[A-Za-z0-9._-]+\\",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex AbsolutePathPattern();
}
