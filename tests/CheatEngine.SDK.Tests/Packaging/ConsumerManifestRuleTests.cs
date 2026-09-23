using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Proves, on synthetic manifests and without the packaging fixture, that each <see cref="ConsumerManifestRules" /> rule
///     accepts a clean package consumer and rejects the workspace leak it exists for. Runs in both CI legs.
/// </summary>
public sealed class ConsumerManifestRuleTests
{
	private const string Version = "2.0.0-alpha.0.18";
	private const string Sha512 = "n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==";
	private const string Root = @"D:\a\CheatEngine.SDK\CheatEngine.SDK";

	[Fact]
	public void A_package_typed_library_with_the_package_hash_satisfies_the_sdk_library_rule()
	{
		string depsJson = DepsJson("package", "cheatengine.sdk/2.0.0-alpha.0.18", "sha512-" + Sha512);

		Assert.Empty(ConsumerManifestRules.SdkLibraryProblems(depsJson, Version, Sha512));
		Assert.Empty(ConsumerManifestRules.ProjectTypedSdkLibraries(depsJson));
		Assert.Empty(ConsumerManifestRules.WorkspaceLeaks(depsJson, Root));
	}

	[Fact]
	public void A_project_typed_sdk_library_is_reported_by_both_deps_json_rules()
	{
		string depsJson = DepsJson("project", "", "");

		List<string> problems = ConsumerManifestRules.SdkLibraryProblems(depsJson, Version, Sha512);

		Assert.Contains(problems, static p => p.Contains("has type 'project'", StringComparison.Ordinal));
		Assert.Contains(problems, static p => p.Contains("has sha512 ''", StringComparison.Ordinal));
		Assert.Equal($"library '{UmbrellaPackage.Id}/{Version}' is project-typed",
			Assert.Single(ConsumerManifestRules.ProjectTypedSdkLibraries(depsJson)));
	}

	[Fact]
	public void Another_package_hash_or_an_absent_library_is_reported_as_not_the_package_under_test()
	{
		string otherHash = DepsJson("package", "cheatengine.sdk/2.0.0-alpha.0.18", "sha512-AAAA");

		Assert.Contains(ConsumerManifestRules.SdkLibraryProblems(otherHash, Version, Sha512),
			static p => p.Contains("(the package under test)", StringComparison.Ordinal));
		Assert.Equal($"library '{UmbrellaPackage.Id}/2.0.0' is absent",
			Assert.Single(ConsumerManifestRules.SdkLibraryProblems(otherHash, "2.0.0", Sha512)));
	}

	[Theory]
	[InlineData(@"{""path"":""D:\\a\\CheatEngine.SDK\\CheatEngine.SDK\\artifacts\\bin""}", @"\\")]
	[InlineData(@"D:\A\CheatEngine.SDK\CheatEngine.SDK\native\lua53-64.dll", @"\")]
	[InlineData("d:/a/CheatEngine.SDK/CheatEngine.SDK/src", "/")]
	public void The_repository_root_in_any_separator_form_is_a_workspace_leak(string manifest, string separator)
	{
		string leak = Assert.Single(ConsumerManifestRules.WorkspaceLeaks(manifest, Root));

		Assert.Contains($"a{separator}CheatEngine.SDK", leak, StringComparison.Ordinal);
	}

	[Fact]
	public void Additional_probing_paths_are_a_workspace_leak()
	{
		const string runtimeConfig =
			"""{"runtimeOptions":{"additionalProbingPaths":["C:\\Users\\dev\\.nuget\\packages"]}}""";

		Assert.Equal("declares additionalProbingPaths",
			Assert.Single(ConsumerManifestRules.WorkspaceLeaks(runtimeConfig, Root)));
	}

	[Theory]
	[InlineData("lua53-64.dll", true)]
	[InlineData("LUA54.DLL", true)]
	[InlineData("lua5.1.dll", true)]
	[InlineData("cheatengine-sdk-lua-bridge.dll", false)]
	[InlineData("CheatEngine.SDK.Lua.dll", false)]
	[InlineData("lua53-64.pdb", false)]
	public void Only_lua_dll_file_names_count_as_a_lua_runtime(string fileName, bool isLuaRuntime)
	{
		Assert.Equal(isLuaRuntime, ConsumerManifestRules.IsLuaRuntimeFileName(fileName));
	}

	private static string DepsJson(string type, string path, string sha512)
	{
		return $$"""
		         {
		           "libraries": {
		             "DefaultConsumer/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
		             "{{UmbrellaPackage.Id}}/{{Version}}": { "type": "{{type}}", "serviceable": true, "sha512": "{{sha512}}", "path": "{{path}}" }
		           }
		         }
		         """;
	}
}
