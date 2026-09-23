using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     The <c>Microsoft.NETCore.App</c> references of the test compilations, found on the local disk only: no NuGet
///     client, no network, no package cache.
/// </summary>
/// <remarks>
///     <para>
///         The testing library's own <c>ReferenceAssemblies.Net.Net100</c> restores the package
///         <c>Microsoft.NETCore.App.Ref</c> at test time, which needs nuget.org on a machine with a cold cache.
///         <see cref="WithoutPackages" /> names the target framework and no package, so the library has nothing to
///         restore;
///         the references come from <see cref="References" /> instead.
///     </para>
///     <para>
///         Two sources, in this order. (1) The targeting pack of the .NET installation that runs the tests
///         (<c>&lt;dotnet root&gt;/packs/Microsoft.NETCore.App.Ref/&lt;10.0.x&gt;/ref/net10.0</c>): the reference
///         assemblies a <c>net10.0</c> plugin compiles against, the same files the package carries; every SDK installation
///         has it. (2) When only a runtime is installed: the implementation assemblies of the running runtime, from the
///         host's trusted-platform-assembly list.
///     </para>
///     <para>
///         Same algorithm as <c>LocalFrameworkReferences</c> of <c>CheatEngine.SDK.SourceGenerators.EntryPoint.Tests</c>;
///         the two
///         copies are candidates for one file in <c>tests/CheatEngine.SDK.Tests.Shared</c>.
///     </para>
/// </remarks>
internal static class LocalFrameworkReferences
{
	private const string TargetFramework = "net10.0";

	private static readonly Lazy<ImmutableArray<MetadataReference>> LazyReferences = new(Load);

	/// <summary>A <see cref="ReferenceAssemblies" /> without any package: resolving it touches no NuGet source.</summary>
	public static ReferenceAssemblies WithoutPackages
	{
		get;
	} = new(TargetFramework);

	/// <summary>
	///     Targeting pack when there is one, the running runtime otherwise. Loaded once per test process: sharing the
	///     instances lets Roslyn share the metadata they read.
	/// </summary>
	/// <exception cref="InvalidOperationException">Neither source yields a single assembly.</exception>
	public static ImmutableArray<MetadataReference> References => LazyReferences.Value;

	private static string RuntimeDirectory =>
		Path.GetDirectoryName(typeof(object).Assembly.Location)
		?? throw new InvalidOperationException(
			"System.Private.CoreLib has no location: single-file test hosts are not supported.");

	/// <summary>Reference assemblies of the highest installed <c>10.0.x</c> targeting pack; empty when none is installed.</summary>
	public static ImmutableArray<MetadataReference> FromTargetingPack()
	{
		// <root>/shared/Microsoft.NETCore.App/<version>/  ->  <root>/packs/Microsoft.NETCore.App.Ref/<version>/ref/net10.0/
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
			string candidate = Path.Combine(pack, "ref", TargetFramework);
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
		// The list also holds the test application's own dependencies (xUnit, Roslyn, the analyzers): only what
		// sits in the shared framework directory is Microsoft.NETCore.App. Native DLLs are not on the list.
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

	private static ImmutableArray<MetadataReference> Load()
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

	// "10.0.1", "10.0.0-rc.2.25502.107": the pre-release label does not matter for picking a pack.
	private static bool TryParsePackVersion(string directoryName, out Version? version)
	{
		int label = directoryName.IndexOf('-', StringComparison.Ordinal);
		return Version.TryParse(label < 0 ? directoryName : directoryName[..label], out version);
	}

	// Sorted: the order of references is part of a compilation, and directory enumeration order is not specified.
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
