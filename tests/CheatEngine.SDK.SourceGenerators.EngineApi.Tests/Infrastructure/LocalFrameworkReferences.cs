using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     The <c>Microsoft.NETCore.App</c> references of the test compilations, found on the local disk only: no NuGet
///     client, no network, no package cache.
/// </summary>
/// <remarks>
///     Two sources, in this order. (1) The targeting pack of the .NET installation that runs the tests
///     (<c>&lt;dotnet root&gt;/packs/Microsoft.NETCore.App.Ref/&lt;10.0.x&gt;/ref/net10.0</c>): the very reference
///     assemblies a <c>net10.0</c> project compiles against. Every SDK installation has it. (2) When only a runtime is
///     installed: the implementation assemblies of the running runtime, taken from the host's trusted-platform-assembly
///     list.
/// </remarks>
internal static class LocalFrameworkReferences
{
	private const string TargetFrameworkFolder = "net10.0";

	private static string RuntimeDirectory =>
		Path.GetDirectoryName(typeof(object).Assembly.Location)
		?? throw new InvalidOperationException(
			"System.Private.CoreLib has no location: single-file test hosts are not supported.");

	/// <summary>Targeting pack when there is one, the running runtime otherwise.</summary>
	/// <exception cref="InvalidOperationException">Neither source yields a single assembly.</exception>
	public static ImmutableArray<MetadataReference> Load()
	{
		ImmutableArray<MetadataReference> references = FromTargetingPack();
		if (references.IsEmpty)
		{
			references = FromRunningRuntime();
		}

		return references.IsEmpty
			? throw new InvalidOperationException(
				$"No Microsoft.NETCore.App references found: no targeting pack next to '{RuntimeDirectory}' and no trusted platform assembly in it.")
			: references;
	}

	/// <summary>Reference assemblies of the highest installed <c>10.0.x</c> targeting pack; empty when none is installed.</summary>
	public static ImmutableArray<MetadataReference> FromTargetingPack()
	{
		string? dotnetRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(RuntimeDirectory)));
		if (dotnetRoot is null)
		{
			return [];
		}

		string packs = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
		if (!Directory.Exists(packs))
		{
			return [];
		}

		string? best = null;
		Version? bestVersion = null;
		foreach (string pack in Directory.EnumerateDirectories(packs))
		{
			string candidate = Path.Combine(pack, "ref", TargetFrameworkFolder);
			if (Directory.Exists(candidate)
				&& TryParsePackVersion(Path.GetFileName(pack), out Version? version)
				&& (bestVersion is null || version > bestVersion))
			{
				best = candidate;
				bestVersion = version;
			}
		}

		return best is null ? [] : CreateReferences(Directory.GetFiles(best, "*.dll"));
	}

	/// <summary>Implementation assemblies of the runtime this process runs on (managed ones only).</summary>
	public static ImmutableArray<MetadataReference> FromRunningRuntime()
	{
		string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
		List<string> paths = [];
		foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			if (string.Equals(Path.GetDirectoryName(path), RuntimeDirectory, StringComparison.OrdinalIgnoreCase))
			{
				paths.Add(path);
			}
		}

		return CreateReferences(paths);
	}

	private static bool TryParsePackVersion(string directoryName, out Version? version)
	{
		int label = directoryName.IndexOf('-', StringComparison.Ordinal);
		return Version.TryParse(label < 0 ? directoryName : directoryName[..label], out version);
	}

	private static ImmutableArray<MetadataReference> CreateReferences(IEnumerable<string> paths)
	{
		ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>();
		foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
		{
			references.Add(MetadataReference.CreateFromFile(path));
		}

		return references.ToImmutable();
	}
}
