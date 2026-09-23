using System.Globalization;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Build;

/// <summary>
///     The packaged build target <c>CheatEngineSdkWarnNativeAotPluginProfile</c> (
///     <c>src/CheatEngine.SDK/build/CheatEngine.SDK.targets</c>)
///     warns with <c>CESDK9102</c> when a direct consumer <b>library</b> sets <c>PublishAot=true</c>: a NativeAOT plugin
///     DLL cannot be unloaded by Cheat Engine's <c>FreeLibrary</c> (audit F02, EXT-01), so the only supported plugin
///     profile is the managed hostfxr route. An executable publishing with NativeAOT is not warned.
/// </summary>
/// <remarks>
///     Not a packaging test (no <c>Category=Packaging</c> trait): each case writes a throwaway SDK-style project under
///     <see cref="Path.GetTempPath" />, outside any repository, that imports the repository's
///     <c>build/CheatEngine.SDK.props</c> and <c>.targets</c> exactly like NuGet imports them for a direct package
///     reference, pins the repository's <c>global.json</c>, and runs only that target with <c>dotnet msbuild</c> (no
///     restore: the target depends on nothing). The packaged consumers of <c>PackagedUmbrellaFixture</c> (an AOT
///     executable among them) prove the same file stays silent for executables in the real package.
/// </remarks>
public sealed class NativeAotProfileTargetTests
{
	private const string TargetName = "CheatEngineSdkWarnNativeAotPluginProfile";
	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

	[Theory]
	[InlineData("")]
	[InlineData("Shared")]
	[InlineData("Static")]
	[Trait("Qualification", "Q41")]
	public async Task PublishAot_library_consumer_gets_CESDK9102(string nativeLib)
	{
		ProcessResult result = await RunTargetAsync("Library", "true", nativeLib, false);

		Assert.True(result.ExitCode == 0, result.CombinedOutput);
		Assert.Contains("warning CESDK9102", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains($"NativeLib={nativeLib})", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("FreeLibrary", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public void CESDK9102_warning_links_its_rule_page_and_runs_before_the_build()
	{
		XDocument targets =
			XDocument.Load(RepositoryLayout.PathOf("src/CheatEngine.SDK/build/CheatEngine.SDK.targets"));

		XElement target = Assert.Single(targets.Root!.Elements("Target"),
			static element => string.Equals((string?) element.Attribute("Name"), TargetName, StringComparison.Ordinal));
		Assert.Equal("BeforeBuild", (string?) target.Attribute("BeforeTargets"));
		XElement warning = Assert.Single(target.Elements("Warning"));
		Assert.Equal("CESDK9102", (string?) warning.Attribute("Code"));
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/CESDK9102.md",
			(string?) warning.Attribute("HelpLink"));
		Assert.True(File.Exists(RepositoryLayout.PathOf("analyzers/docs/CESDK9102.md")));
	}

	[Theory]
	[InlineData("Exe")]
	[InlineData("WinExe")]
	[Trait("Qualification", "Q41")]
	public async Task PublishAot_executable_consumer_gets_no_CESDK9102(string outputType)
	{
		ProcessResult result = await RunTargetAsync(outputType, "true", "", false);

		Assert.True(result.ExitCode == 0, result.CombinedOutput);
		Assert.DoesNotContain("CESDK9102", result.CombinedOutput, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("")]
	[InlineData("false")]
	[Trait("Qualification", "Q41")]
	public async Task Consumer_without_PublishAot_gets_no_CESDK9102(string publishAot)
	{
		ProcessResult result = await RunTargetAsync("Library", publishAot, "Shared", false);

		Assert.True(result.ExitCode == 0, result.CombinedOutput);
		Assert.DoesNotContain("CESDK9102", result.CombinedOutput, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q41")]
	public async Task NoWarn_demotes_CESDK9102_to_a_message()
	{
		ProcessResult result = await RunTargetAsync("Library", "true", "Shared", true);

		// Detailed verbosity: MSBuild logs a warning demoted by NoWarn as a low-importance message.
		Assert.True(result.ExitCode == 0, result.CombinedOutput);
		Assert.DoesNotContain("warning CESDK9102", result.CombinedOutput, StringComparison.Ordinal);
		Assert.Contains("CESDK9102", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("FreeLibrary", result.StandardOutput, StringComparison.Ordinal);
	}

	private static async Task<ProcessResult> RunTargetAsync(string outputType, string publishAot, string nativeLib,
		bool noWarn)
	{
		string directory = Path.Combine(Path.GetTempPath(), "cesdk9102-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			string buildDirectory = RepositoryLayout.PathOf("src/CheatEngine.SDK/build");
			string project = Path.Combine(directory, "NativeAotProfileConsumer.csproj");
			File.Copy(RepositoryLayout.PathOf("global.json"), Path.Combine(directory, "global.json"));
			await File.WriteAllTextAsync(project, $"""
			                                       <Project Sdk="Microsoft.NET.Sdk">
			                                         <Import Project="{Path.Combine(buildDirectory, "CheatEngine.SDK.props")}"/>
			                                         <PropertyGroup>
			                                           <TargetFramework>net10.0</TargetFramework>
			                                           <OutputType>{outputType}</OutputType>
			                                           <PublishAot>{publishAot}</PublishAot>
			                                           <NativeLib>{nativeLib}</NativeLib>
			                                           <NoWarn Condition="'{(noWarn ? "true" : "false")}' == 'true'">$(NoWarn);CESDK9102</NoWarn>
			                                         </PropertyGroup>
			                                         <Import Project="{Path.Combine(buildDirectory, "CheatEngine.SDK.targets")}"/>
			                                       </Project>
			                                       """, TestContext.Current.CancellationToken);

			string arguments = string.Create(CultureInfo.InvariantCulture,
				$"msbuild \"{project}\" -t:{TargetName} -nologo -v:{(noWarn ? "d" : "n")} -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:ImportDirectoryPackagesProps=false");
			return await ProcessRunner.RunAsync("dotnet", arguments, directory, Timeout);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}
}
