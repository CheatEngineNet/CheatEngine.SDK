using System.Reflection;

namespace CheatEngine.SDK.Hosting.Tests.Coexistence;

/// <summary>
///     Locates the prebuilt output of <c>tests/CheatEngine.SDK.LivePlugin.Coexistence/{PluginA,PluginB}</c> through
///     <see cref="AssemblyMetadataAttribute" /> values this test project's own csproj emits
///     (<c>CoexistencePluginBinRoot</c>, <c>CoexistencePluginConfiguration</c>, <c>RepoRoot</c>), never a
///     <c>ProjectReference</c> to either plugin: a <c>ProjectReference</c> there would change
///     <c>tests/CheatEngine.SDK.LivePlugin.Coexistence/{PluginA,PluginB}/packages.lock.json</c>, which this lot does
///     not own.
/// </summary>
internal static class CoexistencePluginLayout
{
	/// <summary>The exact <c>&lt;AssemblyName&gt;</c> of Plugin A's csproj.</summary>
	internal const string PluginAAssemblyName = "CheatEngine.SDK.LivePlugin.Coexistence.PluginA";

	/// <summary>The exact <c>&lt;AssemblyName&gt;</c> of Plugin B's csproj.</summary>
	internal const string PluginBAssemblyName = "CheatEngine.SDK.LivePlugin.Coexistence.PluginB";

	/// <summary>The repository root, from the <c>RepoRoot</c> assembly metadata.</summary>
	internal static string RepoRoot => RequireMetadata("RepoRoot");

	/// <summary>Plugin A's own output directory (<c>&lt;bin root&gt;/&lt;assembly name&gt;/&lt;configuration&gt;</c>).</summary>
	internal static string PluginADirectory => Path.Combine(BinRoot, PluginAAssemblyName, LowercaseConfiguration);

	/// <summary>Plugin B's own output directory.</summary>
	internal static string PluginBDirectory => Path.Combine(BinRoot, PluginBAssemblyName, LowercaseConfiguration);

	private static string BinRoot => RequireMetadata("CoexistencePluginBinRoot");

	private static string LowercaseConfiguration => RequireMetadata("CoexistencePluginConfiguration").ToLowerInvariant();

	/// <summary>
	///     Builds the shared-layout scenario: copies both plugin output directories into one folder. Fails loudly, per
	///     the fixture's own README ("do not copy either result into the other directory" is exactly the opposite of
	///     the shared-layout scenario's point), if a same-named file differs between the two builds -- the shared
	///     dependency files (the SDK assemblies, the bridge) must be byte-identical, since both plugins are built from
	///     the same source tree and configuration.
	/// </summary>
	internal static string CreateSharedLayout(string destinationDirectory)
	{
		Directory.CreateDirectory(destinationDirectory);
		CopyDirectoryInto(PluginADirectory, destinationDirectory, compareExisting: false);
		CopyDirectoryInto(PluginBDirectory, destinationDirectory, compareExisting: true);
		return destinationDirectory;
	}

	private static void CopyDirectoryInto(string sourceDirectory, string destinationDirectory, bool compareExisting)
	{
		foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
		{
			string fileName = Path.GetFileName(sourceFile);
			string destinationFile = Path.Combine(destinationDirectory, fileName);
			if (File.Exists(destinationFile))
			{
				if (compareExisting)
				{
					byte[] sourceBytes = File.ReadAllBytes(sourceFile);
					byte[] destinationBytes = File.ReadAllBytes(destinationFile);
					if (!sourceBytes.AsSpan().SequenceEqual(destinationBytes))
					{
						throw new InvalidOperationException(
							$"'{fileName}' differs between Plugin A's and Plugin B's output; the shared-layout scenario requires every same-named file to be identical.");
					}
				}

				continue;
			}

			File.Copy(sourceFile, destinationFile);
		}
	}

	private static string RequireMetadata(string key)
	{
		foreach (AssemblyMetadataAttribute attribute in typeof(CoexistencePluginLayout).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
		{
			if (string.Equals(attribute.Key, key, StringComparison.Ordinal) && !string.IsNullOrEmpty(attribute.Value))
			{
				return attribute.Value;
			}
		}

		throw new InvalidOperationException($"Assembly metadata '{key}' was not emitted by the test project.");
	}
}
