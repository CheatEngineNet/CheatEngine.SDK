using System.Globalization;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     <c>.github/dependabot.yml</c> (audit register PR-CQ-07): every ecosystem waits before proposing a fresh release,
///     the Roslyn pin and the SDK-implicit packages never move on their own, and titles stay compatible with the pull
///     request policy. https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference
/// </summary>
public sealed class DependabotConfigurationTests
{
	private const string ConfigurationPath = ".github/dependabot.yml";
	private const int MinimumCooldownDays = 7;

	/// <summary>Packages that move only with <c>RoslynComponentFloor</c> (CESDK9002) or with <c>global.json</c>.</summary>
	private static readonly string[] s_pinnedPackages =
	[
		"Microsoft.CodeAnalysis.CSharp",
		"Microsoft.CodeAnalysis.CSharp.Workspaces",
		"Microsoft.CodeAnalysis.Analyzers",
		"Microsoft.NET.ILLink.Tasks",
		"Microsoft.DotNet.ILCompiler",
		"runtime.*.Microsoft.DotNet.ILCompiler"
	];

	private static readonly string[] s_cooldownKeys =
		["default-days", "semver-major-days", "semver-minor-days", "semver-patch-days"];

	[Fact]
	public void Every_ecosystem_has_a_cooldown_of_at_least_seven_days()
	{
		List<string> problems = [];
		foreach (YamlMappingNode update in Updates())
		{
			string ecosystem = YamlDocument.Scalar(update, "package-ecosystem") ?? "?";
			YamlNode? cooldown = YamlDocument.Child(update, "cooldown");
			if (cooldown is null)
			{
				problems.Add($"{ecosystem}: no cooldown");
				continue;
			}

			if (YamlDocument.Scalar(cooldown, "default-days") is null)
			{
				problems.Add($"{ecosystem}: no default-days");
			}

			foreach (string key in s_cooldownKeys)
			{
				string? value = YamlDocument.Scalar(cooldown, key);
				if (value is not null && (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days < MinimumCooldownDays))
				{
					problems.Add($"{ecosystem}: {key} is {value}");
				}
			}
		}

		Assert.True(problems.Count == 0,
			$"Every Dependabot ecosystem needs a cooldown of at least {MinimumCooldownDays} days (zizmor dependabot-cooldown): {string.Join("; ", problems)}");
	}

	[Fact]
	public void Roslyn_pins_and_sdk_implicit_packages_are_ignored()
	{
		YamlMappingNode nuget = Ecosystem("nuget");
		HashSet<string> ignored = new(StringComparer.Ordinal);
		foreach (YamlMappingNode rule in YamlDocument.Mappings(nuget, "ignore"))
		{
			// A whole-package ignore has no update-types: a partial ignore would still let some bumps through.
			Assert.True(YamlDocument.Child(rule, "update-types") is null && YamlDocument.Child(rule, "versions") is null,
				$"The nuget ignore rule for '{YamlDocument.Scalar(rule, "dependency-name")}' must ignore every version.");
			ignored.Add(YamlDocument.Scalar(rule, "dependency-name") ?? "");
		}

		foreach (string package in s_pinnedPackages)
		{
			Assert.True(ignored.Contains(package), $"Dependabot must ignore '{package}' ({ConfigurationPath}).");
		}
	}

	[Fact]
	public void Roslyn_ignores_cover_every_package_pinned_to_the_roslyn_floor()
	{
		XDocument roslynProps = XDocument.Load(RepositoryFile.FullPath("eng/RoslynComponent.props"));
		string floor = Assert.Single(roslynProps.Descendants("RoslynComponentFloor")).Value.Trim();
		XDocument packages = XDocument.Load(RepositoryFile.FullPath("Directory.Packages.props"));

		List<string> pinnedToFloor = [];
		foreach (XElement version in packages.Descendants("PackageVersion"))
		{
			string id = (string?) version.Attribute("Include") ?? "";
			if (id.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal)
				&& string.Equals((string?) version.Attribute("Version"), floor, StringComparison.Ordinal))
			{
				pinnedToFloor.Add(id);
			}
		}

		Assert.NotEmpty(pinnedToFloor);
		foreach (string id in pinnedToFloor)
		{
			Assert.True(Array.IndexOf(s_pinnedPackages, id) >= 0,
				$"'{id}' is pinned to RoslynComponentFloor {floor} in Directory.Packages.props: add a Dependabot ignore for it.");
		}
	}

	[Fact]
	public void Dotnet_sdk_ecosystem_ignores_major_updates()
	{
		YamlMappingNode sdk = Ecosystem("dotnet-sdk");
		Assert.Equal("/", YamlDocument.Scalar(sdk, "directory"));

		bool ignoresMajor = false;
		foreach (YamlMappingNode rule in YamlDocument.Mappings(sdk, "ignore"))
		{
			if (string.Equals(YamlDocument.Scalar(rule, "dependency-name"), "*", StringComparison.Ordinal)
				&& YamlDocument.Scalars(rule, "update-types").Contains("version-update:semver-major", StringComparer.Ordinal))
			{
				ignoresMajor = true;
			}
		}

		Assert.True(ignoresMajor, "The dotnet-sdk ecosystem must ignore semver-major updates: a new .NET major is a migration.");
	}

	[Fact]
	public void Github_actions_updates_cover_the_composite_action_directories()
	{
		YamlMappingNode actions = Ecosystem("github-actions");
		IReadOnlyList<string> directories = YamlDocument.Scalars(actions, "directories");

		Assert.Contains("/", directories, StringComparer.Ordinal);
		Assert.Contains("/.github/actions/*", directories, StringComparer.Ordinal);
		Assert.True(Directory.Exists(RepositoryFile.FullPath(".github/actions")),
			"The composite action folder moved: update the github-actions directories.");
	}

	[Fact]
	public void No_ecosystem_sets_a_commit_message_prefix()
	{
		foreach (YamlMappingNode update in Updates())
		{
			YamlNode? commitMessage = YamlDocument.Child(update, "commit-message");
			Assert.True(commitMessage is null || YamlDocument.Scalar(commitMessage, "prefix") is null,
				$"{YamlDocument.Scalar(update, "package-ecosystem")} sets commit-message.prefix: 'deps: ...' titles break the pull request title rule.");
			Assert.Null(YamlDocument.Child(update, "insecure-external-code-execution"));
		}
	}

	[Fact]
	public void Specific_nuget_groups_come_before_the_catch_all_group()
	{
		IReadOnlyList<string> groups = YamlDocument.KeysOf(YamlDocument.Child(Ecosystem("nuget"), "groups"));

		int catchAll = -1;
		for (int i = 0; i < groups.Count; i++)
		{
			YamlNode? group = YamlDocument.Child(YamlDocument.Child(Ecosystem("nuget"), "groups"), groups[i]);
			bool versionUpdates = !string.Equals(YamlDocument.Scalar(group, "applies-to"), "security-updates",
				StringComparison.Ordinal);
			if (versionUpdates && YamlDocument.Scalars(group, "patterns") is ["*"])
			{
				catchAll = i;
			}
		}

		// Dependabot puts a dependency in the first group it matches.
		Assert.Equal(groups.Count - 1, catchAll);
	}

	private static List<YamlMappingNode> Updates()
	{
		IReadOnlyList<YamlMappingNode> updates = YamlDocument.Mappings(YamlDocument.Load(ConfigurationPath).Root, "updates");
		Assert.NotEmpty(updates);
		return [.. updates];
	}

	private static YamlMappingNode Ecosystem(string name)
	{
		List<YamlMappingNode> matches = [];
		foreach (YamlMappingNode update in Updates())
		{
			if (string.Equals(YamlDocument.Scalar(update, "package-ecosystem"), name, StringComparison.Ordinal))
			{
				matches.Add(update);
			}
		}

		return Assert.Single(matches);
	}
}
