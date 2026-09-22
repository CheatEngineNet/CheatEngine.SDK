using System.Text.Json;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.LockFiles;

/// <summary>
///     Offline mirror of the structural checks of <c>eng/Update-LockFiles.ps1</c>, so a hand-edited, missing or
///     IDE-reformatted lock file is caught before CI runs the script. Only the script regenerates lock files; the CI
///     <c>lock-files</c> job (<c>-Verify</c>) proves they equal a fresh restore.
/// </summary>
public sealed class LockFileTests
{
	private const string LockFileName = "packages.lock.json";
	private const string RegenerateHint = "Regenerate with ./eng/Update-LockFiles.ps1 (eng/api/README.md#lock-files).";

	[Fact]
	public void Every_project_has_a_committed_lock_file()
	{
		List<string> missing = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			if (!File.Exists(FullPath(LockPathOf(project))))
			{
				missing.Add(project);
			}
		}

		Assert.True(missing.Count == 0, $"Projects without {LockFileName}: {string.Join(", ", missing)}. {RegenerateHint}");
	}

	[Fact]
	public void Lock_files_parse_and_declare_a_supported_format_version()
	{
		foreach ((string path, JsonElement lockFile) in LoadLockFiles())
		{
			Assert.True(lockFile.TryGetProperty("version", out JsonElement version), $"{path} has no version.");
			Assert.True(version.GetInt32() is 1 or 2, $"{path} declares the unsupported lock file version {version}.");
			Assert.Equal(JsonValueKind.Object, lockFile.GetProperty("dependencies").ValueKind);
		}
	}

	[Fact]
	public void Every_lock_file_is_version_2_because_every_project_uses_central_package_management()
	{
		// CESDK9005 forbids opting out of Central Package Management, and NuGet writes version 2 lock files for CPM projects.
		foreach ((string path, JsonElement lockFile) in LoadLockFiles())
		{
			Assert.True(lockFile.GetProperty("version").GetInt32() == 2, $"{path} is not a version 2 lock file. {RegenerateHint}");
		}
	}

	[Fact]
	public void Version_1_lock_files_hold_no_central_transitive_entries()
	{
		foreach ((string path, JsonElement lockFile) in LoadLockFiles())
		{
			if (lockFile.GetProperty("version").GetInt32() != 1)
			{
				continue;
			}

			foreach ((string section, string packageId, JsonElement package) in Entries(lockFile))
			{
				Assert.False(string.Equals(TypeOf(package), "CentralTransitive", StringComparison.Ordinal),
					$"{path}: version 1 lock holds the CentralTransitive entry {packageId} ({section}). {RegenerateHint}");
			}
		}
	}

	[Fact]
	public void Native_aot_projects_lock_the_win_x64_ilcompiler_packages()
	{
		int runtimeSpecificProjects = 0;
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			XDocument document = XDocument.Load(FullPath(project));
			string? runtime = LastPropertyValue(document, "RuntimeIdentifier");
			if (runtime is null)
			{
				continue;
			}

			runtimeSpecificProjects++;
			bool publishAot = string.Equals(LastPropertyValue(document, "PublishAot"), "true", StringComparison.OrdinalIgnoreCase);
			string lockPath = LockPathOf(project);
			using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllBytes(FullPath(lockPath)));
			JsonProperty? section = null;
			foreach (JsonProperty candidate in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
			{
				if (candidate.Name.EndsWith($"/{runtime}", StringComparison.Ordinal))
				{
					section = candidate;
				}
			}

			Assert.True(section is not null,
				$"{project} sets RuntimeIdentifier {runtime} but {lockPath} has no '<tfm>/{runtime}' section, so 'dotnet publish --no-restore' fails. {RegenerateHint}");
			if (publishAot)
			{
				string compiler = $"runtime.{runtime}.Microsoft.DotNet.ILCompiler";
				Assert.True(section.Value.Value.TryGetProperty(compiler, out _),
					$"{project} publishes Native AOT but {lockPath} does not lock {compiler} in '{section.Value.Name}'. {RegenerateHint}");
			}
		}

		Assert.True(runtimeSpecificProjects > 0, "No project sets RuntimeIdentifier: the Native AOT probes are expected to.");
	}

	[Fact]
	public void No_lock_file_resolves_a_cheatengine_package()
	{
		// The SDK never consumes its own package through NuGet; the ApiCompat 1.0.0 baseline is a PackageDownload, which
		// lock files do not record. Project references appear with type "Project" and are not packages.
		foreach ((string path, JsonElement lockFile) in LoadLockFiles())
		{
			foreach ((string section, string packageId, JsonElement package) in Entries(lockFile))
			{
				Assert.False(
					!string.Equals(TypeOf(package), "Project", StringComparison.Ordinal)
					&& packageId.StartsWith("CheatEngine.", StringComparison.OrdinalIgnoreCase),
					$"{path} resolves the package {packageId} ({section}) from a feed.");
			}
		}
	}

	[Fact]
	public void Lock_files_end_without_a_final_newline_as_nuget_writes_them()
	{
		// The script restores committed bytes when the JSON is unchanged, so an editor-added final newline would survive
		// until the next real change; catching it here keeps the files byte-equal to NuGet's output.
		foreach ((string path, _) in LoadLockFiles())
		{
			byte[] content = File.ReadAllBytes(FullPath(path));
			Assert.True(content.Length > 0 && content[^1] == (byte) '}',
				$"{path} does not end with '}}' as NuGet writes it (edited by hand or by an editor?). {RegenerateHint}");
		}
	}

	private static List<(string Path, JsonElement LockFile)> LoadLockFiles()
	{
		List<(string, JsonElement)> lockFiles = [];
		foreach (string path in RepositoryRoot.EnumerateSourceFiles(LockFileName))
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(FullPath(path)));
			lockFiles.Add((path, document.RootElement.Clone()));
		}

		Assert.NotEmpty(lockFiles);
		return lockFiles;
	}

	private static IEnumerable<(string Section, string PackageId, JsonElement Package)> Entries(JsonElement lockFile)
	{
		foreach (JsonProperty section in lockFile.GetProperty("dependencies").EnumerateObject())
		{
			foreach (JsonProperty package in section.Value.EnumerateObject())
			{
				yield return (section.Name, package.Name, package.Value);
			}
		}
	}

	private static string? TypeOf(JsonElement package)
	{
		return package.TryGetProperty("type", out JsonElement type) ? type.GetString() : null;
	}

	private static string? LastPropertyValue(XDocument document, string name)
	{
		string? value = null;
		foreach (XElement group in document.Descendants("PropertyGroup"))
		{
			foreach (XElement property in group.Elements(name))
			{
				value = property.Value.Trim();
			}
		}

		return value;
	}

	private static string LockPathOf(string project)
	{
		return $"{project[..project.LastIndexOf('/')]}/{LockFileName}";
	}

	private static string FullPath(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}
}
