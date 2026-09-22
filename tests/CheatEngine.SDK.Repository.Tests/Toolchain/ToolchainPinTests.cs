using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Toolchain;

/// <summary>
///     The toolchain is pinned exactly: one .NET SDK (<c>global.json</c> with <c>rollForward: disable</c>, required by the
///     committed NuGet lock files because the SDK's implicit ILLink/ILCompiler packages move with its version) and one
///     code-analysis level, so the rule set only changes in a reviewed commit. The build enforces the analysis-level pin
///     with <c>CESDK9004</c>; these tests keep the committed values themselves honest.
/// </summary>
public sealed partial class ToolchainPinTests
{
	private const string GlobalJson = "global.json";
	private const string DirectoryBuildProps = "Directory.Build.props";
	private const string AnalysisLevelPinProperty = "_CheatEngineSdkPinnedAnalysisLevel";

	private static readonly string[] s_msbuildFilePatterns = ["*.csproj", "*.props", "*.targets"];

	[Fact]
	public void Global_json_requires_the_exact_sdk_with_roll_forward_disabled()
	{
		JsonElement sdk = ReadGlobalJsonSdk();

		string? version = sdk.GetProperty("version").GetString();
		Assert.NotNull(version);
		Assert.Matches(ExactSdkVersion(), version);
		Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
		Assert.Equal(JsonValueKind.False, sdk.GetProperty("allowPrerelease").ValueKind);
	}

	[Fact]
	public void Global_json_error_message_names_the_pinned_sdk_version()
	{
		JsonElement sdk = ReadGlobalJsonSdk();
		string version = sdk.GetProperty("version").GetString()!;

		Assert.True(sdk.TryGetProperty("errorMessage", out JsonElement errorMessage),
			"global.json must carry sdk.errorMessage so a missing SDK fails with install instructions (.NET 10 SDK feature).");
		string? message = errorMessage.GetString();
		Assert.False(string.IsNullOrWhiteSpace(message));
		Assert.Contains(version, message, StringComparison.Ordinal);
		Assert.Contains($"--version {version}", message, StringComparison.Ordinal);
	}

	[Fact]
	public void Analysis_level_is_pinned_to_a_release_not_latest()
	{
		XDocument props = XDocument.Load(RepositoryFile(DirectoryBuildProps));

		string pin = Assert.Single(PropertyValues(props, AnalysisLevelPinProperty));
		Assert.Matches(PinnedAnalysisLevel(), pin);
		Assert.DoesNotContain("latest", pin, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("preview", pin, StringComparison.OrdinalIgnoreCase);

		// Every AnalysisLevel assignment in the root props goes through the pin, so CESDK9004 compares like with like.
		Assert.All(PropertyValues(props, "AnalysisLevel"),
			static value => Assert.Equal($"$({AnalysisLevelPinProperty})", value));
	}

	[Fact]
	public void Analysis_level_pin_moves_with_the_pinned_sdk_major_and_minor()
	{
		string sdkVersion = ReadGlobalJsonSdk().GetProperty("version").GetString()!;
		string pin = Assert.Single(PropertyValues(XDocument.Load(RepositoryFile(DirectoryBuildProps)),
			AnalysisLevelPinProperty));

		string sdkMajorMinor = string.Join('.', sdkVersion.Split('.')[..2]);
		string pinMajorMinor = pin[..pin.IndexOf('-', StringComparison.Ordinal)];
		Assert.True(string.Equals(sdkMajorMinor, pinMajorMinor, StringComparison.Ordinal),
			$"global.json pins SDK {sdkVersion} but Directory.Build.props pins AnalysisLevel {pin}: raise both together.");
	}

	[Fact]
	public void No_project_or_props_file_overrides_the_pinned_analysis_level()
	{
		List<string> offenders = [];
		foreach (string pattern in s_msbuildFilePatterns)
		{
			foreach (string file in RepositoryRoot.EnumerateSourceFiles(pattern))
			{
				if (string.Equals(file, DirectoryBuildProps, StringComparison.Ordinal))
				{
					continue;
				}

				XDocument document = XDocument.Load(RepositoryFile(file));
				if (PropertyValues(document, "AnalysisLevel").Count != 0)
				{
					offenders.Add(file);
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"Only Directory.Build.props may set AnalysisLevel (CESDK9004 also fails the build): {string.Join(", ", offenders)}");
	}

	internal static string RepositoryFile(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>The values of every <c>&lt;PropertyGroup&gt;</c> child element with the given name, in document order.</summary>
	internal static List<string> PropertyValues(XDocument document, string propertyName)
	{
		List<string> values = [];
		foreach (XElement group in document.Descendants("PropertyGroup"))
		{
			foreach (XElement property in group.Elements(propertyName))
			{
				values.Add(property.Value.Trim());
			}
		}

		return values;
	}

	private static JsonElement ReadGlobalJsonSdk()
	{
		JsonDocumentOptions options = new()
		{
			CommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true
		};
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(RepositoryFile(GlobalJson)), options);
		return document.RootElement.GetProperty("sdk").Clone();
	}

	[GeneratedRegex(@"^\d+\.\d+\.\d{3}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ExactSdkVersion();

	[GeneratedRegex(@"^\d+\.\d+-recommended$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex PinnedAnalysisLevel();
}
