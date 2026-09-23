using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Qualification scenario Q40 at C1/C2 (audit ch.21 on the native bridge, ch.04 on the deployment folder): a
///     direct consumer restored from the package under test, in a folder with no adjacent SDK workspace, must take nothing
///     from the development tree. Its manifests name the package (with that file's hash), never a project or a repository
///     path; every deployed SDK assembly is the packed one, byte for byte; no Lua runtime is deployed. The C3 half of Q40
///     (loading such a folder in the exact Cheat Engine host) is not claimed here.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed class CleanConsumerIsolationTests(PackagedUmbrellaFixture fixture)
{
	private const string ConsumerName = "DefaultConsumer";
	private const string LibraryPrefix = "lib/net10.0/";

	[Fact]
	[Trait("Qualification", "Q40")]
	public void Default_consumer_deps_json_resolves_the_sdk_as_the_package_under_test()
	{
		foreach (string depsJson in DepsJsonFiles())
		{
			List<string> problems = ConsumerManifestRules.SdkLibraryProblems(File.ReadAllText(depsJson),
				fixture.PackageVersion, fixture.PackageSha512Base64);
			Assert.True(problems.Count == 0, $"{depsJson}: {string.Join("; ", problems)}");
		}
	}

	[Fact]
	[Trait("Qualification", "Q40")]
	public void Default_consumer_deps_json_has_no_project_typed_sdk_library()
	{
		foreach (string depsJson in DepsJsonFiles())
		{
			List<string> offenders = ConsumerManifestRules.ProjectTypedSdkLibraries(File.ReadAllText(depsJson));
			Assert.True(offenders.Count == 0, $"{depsJson}: {string.Join("; ", offenders)}");
		}
	}

	[Fact]
	[Trait("Qualification", "Q40")]
	public void Consumer_manifests_contain_no_repository_or_developer_path()
	{
		string[] manifests = [$"{ConsumerName}.deps.json", $"{ConsumerName}.runtimeconfig.json"];
		List<string> offenders = [];
		foreach (string folder in DeploymentFolders())
		{
			foreach (string manifest in manifests)
			{
				string path = Path.Combine(folder, manifest);
				Assert.True(File.Exists(path), $"'{path}' was not produced.");
				foreach (string leak in ConsumerManifestRules.WorkspaceLeaks(File.ReadAllText(path),
					         RepositoryLayout.Root))
				{
					offenders.Add($"{path}: {leak}");
				}
			}

			foreach (string devConfig in Directory.GetFiles(folder, "*.runtimeconfig.dev.json"))
			{
				offenders.Add($"{devConfig}: a development runtimeconfig adds probing paths of this machine");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	[Trait("Qualification", "Q40")]
	public void Every_deployed_sdk_assembly_is_byte_identical_to_its_packed_lib_entry()
	{
		int compared = 0;
		foreach (string entry in fixture.PackageEntries)
		{
			if (!entry.StartsWith(LibraryPrefix, StringComparison.Ordinal)
			    || !entry.EndsWith(".dll", StringComparison.Ordinal))
			{
				continue;
			}

			byte[] packed = NupkgInspector.ReadEntryBytes(fixture.PackagePath, entry);
			string fileName = entry[LibraryPrefix.Length..];
			foreach (string folder in DeploymentFolders())
			{
				string deployed = Path.Combine(folder, fileName);
				Assert.True(File.Exists(deployed), $"'{fileName}' was not deployed to '{folder}'.");
				Assert.True(packed.AsSpan().SequenceEqual(File.ReadAllBytes(deployed)),
					$"'{deployed}' differs from the package entry '{entry}' of {Path.GetFileName(fixture.PackagePath)}.");
			}

			compared++;
		}

		Assert.Equal(7, compared);
	}

	[Fact]
	[Trait("Qualification", "Q40")]
	public void Deployment_and_publish_folders_contain_no_lua_runtime()
	{
		List<string> offenders = [];
		foreach (string folder in DeploymentFolders())
		{
			Assert.True(File.Exists(Path.Combine(folder, "cheatengine-sdk-lua-bridge.dll")),
				$"'{folder}' has no bridge, so it is not the deployment folder these facts are about.");
			foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
			{
				if (ConsumerManifestRules.IsLuaRuntimeFileName(Path.GetFileName(file)))
				{
					offenders.Add(file);
				}
			}
		}

		Assert.True(offenders.Count == 0, $"A Lua runtime was deployed: {string.Join(", ", offenders)}");
	}

	private string[] DeploymentFolders()
	{
		return [fixture.DefaultDeploymentDirectory, fixture.DefaultPublishDirectory];
	}

	private string[] DepsJsonFiles()
	{
		return
		[
			fixture.DefaultDepsJsonPath,
			Path.Combine(fixture.DefaultPublishDirectory, $"{ConsumerName}.deps.json")
		];
	}
}
