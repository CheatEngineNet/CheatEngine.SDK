using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Supply-chain facts of the packed <c>.nupkg</c>: the embedded SPDX 2.2 SBOM describes this exact package and every
///     file in it, the nuspec and the embedded symbols point at the exact repository commit, the version follows the
///     MinVer line and the assembly version its major, and no repository contract file leaks into the package. Nothing
///     is loaded for execution: assemblies are read with System.Reflection.Metadata.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed partial class SupplyChainPackageTests(PackagedUmbrellaFixture fixture)
{
	private const string SbomEntry = "_manifest/spdx_2.2/manifest.spdx.json";
	private const string SbomChecksumEntry = SbomEntry + ".sha256";
	private const string RepositoryUrl = "https://github.com/CheatEngineNet/CheatEngine.SDK";
	private const string NativeBridgeEntry = "build/native/cheatengine-sdk-lua-bridge.dll";

	/// <summary>Kind GUID of the Source Link custom debug information in a Portable PDB.</summary>
	private static readonly Guid s_sourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

	private static readonly string[] s_repositoryContractFiles =
		["CompatibilitySuppressions.xml", "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt", "packages.lock.json"];

	[Fact]
	public void Package_embeds_an_spdx_2_2_sbom_describing_itself()
	{
		Assert.Contains(SbomEntry, fixture.PackageEntries, StringComparer.Ordinal);
		using ZipArchive archive = ZipFile.OpenRead(fixture.PackagePath);
		byte[] manifest = ReadEntry(archive, SbomEntry);
		using JsonDocument sbom = JsonDocument.Parse(manifest);
		JsonElement root = sbom.RootElement;

		Assert.Equal("SPDX-2.2", root.GetProperty("spdxVersion").GetString());
		string described = Assert.Single(root.GetProperty("documentDescribes").EnumerateArray()).GetString()!;
		JsonElement package = Assert.Single(root.GetProperty("packages").EnumerateArray(),
			p => string.Equals(p.GetProperty("SPDXID").GetString(), described, StringComparison.Ordinal));
		Assert.Equal(NuspecMetadata("id"), package.GetProperty("name").GetString());
		Assert.Equal(fixture.PackageVersion, package.GetProperty("versionInfo").GetString());
		Assert.Equal(fixture.PackageVersion, NuspecMetadata("version"));
		Assert.StartsWith($"{RepositoryUrl}/", root.GetProperty("documentNamespace").GetString(), StringComparison.Ordinal);

		// The sidecar checksum the SBOM tool writes next to the manifest matches it.
		string sidecar = Encoding.ASCII.GetString(ReadEntry(archive, SbomChecksumEntry)).Trim();
		Assert.Equal(Sha256(manifest), sidecar, ignoreCase: true);
	}

	[Fact]
	public void Sbom_lists_every_shipped_assembly_and_the_native_bridge_with_its_sha256()
	{
		using ZipArchive archive = ZipFile.OpenRead(fixture.PackagePath);
		Dictionary<string, string> sbomSha256 = SbomFileSha256(archive);

		List<string> shipped = [NativeBridgeEntry];
		foreach (string entry in fixture.PackageEntries)
		{
			if ((entry.StartsWith("lib/net10.0/", StringComparison.Ordinal)
				 || entry.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal))
				&& entry.EndsWith(".dll", StringComparison.Ordinal))
			{
				shipped.Add(entry);
			}
		}

		Assert.True(shipped.Count >= 13, $"Expected the 7 libraries, 5 components and the bridge, found {shipped.Count}.");
		foreach (string entry in shipped)
		{
			Assert.True(sbomSha256.TryGetValue(entry, out string? declared), $"The SBOM does not list '{entry}'.");
			Assert.Equal(Sha256(ReadEntry(archive, entry)), declared, ignoreCase: true);
		}
	}

	[Fact]
	public void Sbom_file_inventory_equals_the_package_entries()
	{
		using ZipArchive archive = ZipFile.OpenRead(fixture.PackagePath);
		SortedSet<string> listed = new(SbomFileSha256(archive).Keys, StringComparer.Ordinal);
		SortedSet<string> packed = new(StringComparer.Ordinal);
		foreach (string entry in fixture.PackageEntries)
		{
			if (!entry.StartsWith("_manifest/", StringComparison.Ordinal))
			{
				packed.Add(entry);
			}
		}

		Assert.Equal(packed, listed);
	}

	[Fact]
	public void Package_carries_no_repository_contract_file()
	{
		foreach (string entry in fixture.PackageEntries)
		{
			string fileName = entry[(entry.LastIndexOf('/') + 1)..];
			Assert.DoesNotContain(fileName, s_repositoryContractFiles, StringComparer.OrdinalIgnoreCase);
		}
	}

	[Fact]
	public void Package_version_is_on_the_minver_minimum_line_or_later()
	{
		XDocument props = XDocument.Load(RepositoryLayout.PathOf("Directory.Build.props"));
		string minimum = Assert.Single(props.Descendants("MinVerMinimumMajorMinor")).Value.Trim();
		Version floor = Version.Parse(minimum);

		Version package = CoreVersion(fixture.PackageVersion);
		Assert.True(new Version(package.Major, package.Minor) >= floor,
			$"Package {fixture.PackageVersion} is below the MinVer line {minimum}.");
	}

	[Fact]
	public void Embedded_assemblies_carry_the_package_major_as_assembly_version()
	{
		Version expected = new(CoreVersion(fixture.PackageVersion).Major, 0, 0, 0);
		using ZipArchive archive = ZipFile.OpenRead(fixture.PackagePath);
		List<string> libraries = LibraryEntries();
		Assert.Equal(7, libraries.Count);
		foreach (string entry in libraries)
		{
			using PEReader pe = new(new MemoryStream(ReadEntry(archive, entry)));
			Version actual = pe.GetMetadataReader().GetAssemblyDefinition().Version;
			Assert.True(expected == actual, $"{entry} has AssemblyVersion {actual}, expected {expected}.");
		}
	}

	[Fact]
	public void Nuspec_names_the_repository_and_the_exact_commit()
	{
		XElement repository = Repository();
		Assert.Equal("git", (string?) repository.Attribute("type"));
		Assert.Equal(RepositoryUrl, (string?) repository.Attribute("url"));
		Assert.Matches(CommitSha(), (string?) repository.Attribute("commit") ?? "");
	}

	[Fact]
	public void Embedded_libraries_carry_source_link_to_the_repository_commit()
	{
		string commit = (string?) Repository().Attribute("commit") ?? "";
		string expectedPrefix = $"https://raw.githubusercontent.com/CheatEngineNet/CheatEngine.SDK/{commit}/";
		using ZipArchive archive = ZipFile.OpenRead(fixture.PackagePath);
		foreach (string entry in LibraryEntries())
		{
			using PEReader pe = new(new MemoryStream(ReadEntry(archive, entry)));
			DebugDirectoryEntry embedded = Assert.Single(pe.ReadDebugDirectory(),
				static d => d.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
			using MetadataReaderProvider pdbProvider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
			MetadataReader pdb = pdbProvider.GetMetadataReader();

			string? sourceLink = null;
			foreach (CustomDebugInformationHandle handle in pdb.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
			{
				CustomDebugInformation information = pdb.GetCustomDebugInformation(handle);
				if (pdb.GetGuid(information.Kind) == s_sourceLinkKind)
				{
					sourceLink = Encoding.UTF8.GetString(pdb.GetBlobBytes(information.Value));
				}
			}

			Assert.True(sourceLink is not null, $"{entry} has no Source Link information in its embedded PDB.");
			using JsonDocument map = JsonDocument.Parse(sourceLink);
			List<string> targets = [];
			foreach (JsonProperty document in map.RootElement.GetProperty("documents").EnumerateObject())
			{
				targets.Add(document.Value.GetString() ?? "");
			}

			Assert.NotEmpty(targets);
			Assert.All(targets, target => Assert.StartsWith(expectedPrefix, target, StringComparison.Ordinal));
		}
	}

	private List<string> LibraryEntries()
	{
		List<string> libraries = [];
		foreach (string entry in fixture.PackageEntries)
		{
			if (entry.StartsWith("lib/net10.0/CheatEngine.SDK", StringComparison.Ordinal)
				&& entry.EndsWith(".dll", StringComparison.Ordinal))
			{
				libraries.Add(entry);
			}
		}

		return libraries;
	}

	private XElement Repository()
	{
		XNamespace ns = fixture.Nuspec.Root!.GetDefaultNamespace();
		return Assert.Single(fixture.Nuspec.Descendants(ns + "repository"));
	}

	private string? NuspecMetadata(string name)
	{
		XNamespace ns = fixture.Nuspec.Root!.GetDefaultNamespace();
		return fixture.Nuspec.Root.Element(ns + "metadata")?.Element(ns + name)?.Value;
	}

	/// <summary>The SBOM <c>files</c> array as package entry path (the <c>./</c> prefix removed) to SHA-256.</summary>
	private static Dictionary<string, string> SbomFileSha256(ZipArchive archive)
	{
		using JsonDocument sbom = JsonDocument.Parse(ReadEntry(archive, SbomEntry));
		Dictionary<string, string> files = new(StringComparer.Ordinal);
		foreach (JsonElement file in sbom.RootElement.GetProperty("files").EnumerateArray())
		{
			string name = file.GetProperty("fileName").GetString()!;
			Assert.StartsWith("./", name, StringComparison.Ordinal);
			JsonElement sha256 = Assert.Single(file.GetProperty("checksums").EnumerateArray(),
				static c => string.Equals(c.GetProperty("algorithm").GetString(), "SHA256", StringComparison.Ordinal));
			files.Add(name[2..], sha256.GetProperty("checksumValue").GetString()!);
		}

		return files;
	}

	private static byte[] ReadEntry(ZipArchive archive, string entryName)
	{
		ZipArchiveEntry entry = archive.GetEntry(entryName)
								?? throw new InvalidOperationException($"The package has no '{entryName}' entry.");
		using Stream stream = entry.Open();
		using MemoryStream copy = new();
		stream.CopyTo(copy);
		return copy.ToArray();
	}

	private static string Sha256(byte[] content)
	{
		return Convert.ToHexStringLower(SHA256.HashData(content));
	}

	private static Version CoreVersion(string packageVersion)
	{
		string core = packageVersion.Split('-', '+')[0];
		Assert.True(core.Split('.').Length == 3, $"'{packageVersion}' is not a SemVer version.");
		return Version.Parse(core);
	}

	[GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex CommitSha();
}
