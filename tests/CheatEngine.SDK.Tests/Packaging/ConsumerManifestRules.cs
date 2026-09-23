using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     What a clean package consumer's manifests must and must not say (audit ch.21: a consumer that works thanks to a file
///     found in the development folder is not a qualified consumer). Pure functions of the manifest text, each returning
///     one message per problem, so <see cref="CleanConsumerIsolationTests" /> applies them to the real consumer and
///     <see cref="ConsumerManifestRuleTests" /> proves on synthetic manifests that each rule fails on the leak it exists for.
/// </summary>
internal static class ConsumerManifestRules
{
	private const string AdditionalProbingPaths = "additionalProbingPaths";

	/// <summary>
	///     Problems with the <c>CheatEngine.SDK/&lt;version&gt;</c> library of a <c>.deps.json</c>: it must exist, be
	///     <c>package</c>-typed, live under <c>cheatengine.sdk/&lt;lower-case version&gt;</c> and carry the SHA-512 of the
	///     package under test (for an unsigned package, the NuGet content hash of exactly that file).
	/// </summary>
	public static List<string> SdkLibraryProblems(string depsJson, string packageVersion, string packageSha512Base64)
	{
		List<string> problems = [];
		string key = $"{UmbrellaPackage.Id}/{packageVersion}";
		using JsonDocument document = JsonDocument.Parse(depsJson);
		if (!document.RootElement.TryGetProperty("libraries", out JsonElement libraries)
			|| !libraries.TryGetProperty(key, out JsonElement library))
		{
			problems.Add($"library '{key}' is absent");
			return problems;
		}

		string type = StringProperty(library, "type");
		string path = StringProperty(library, "path");
		string sha512 = StringProperty(library, "sha512");
		string expectedPath = $"{UmbrellaPackage.ExtractionFolderName}/{packageVersion.ToLowerInvariant()}";
		string expectedSha512 = "sha512-" + packageSha512Base64;
		if (!string.Equals(type, "package", StringComparison.Ordinal))
		{
			problems.Add($"library '{key}' has type '{type}', expected 'package'");
		}

		if (!string.Equals(path, expectedPath, StringComparison.Ordinal))
		{
			problems.Add($"library '{key}' has path '{path}', expected '{expectedPath}'");
		}

		if (!string.Equals(sha512, expectedSha512, StringComparison.Ordinal))
		{
			problems.Add($"library '{key}' has sha512 '{sha512}', expected '{expectedSha512}' (the package under test)");
		}

		return problems;
	}

	/// <summary>Every <c>CheatEngine.SDK*</c> library of a <c>.deps.json</c> that is <c>project</c>-typed.</summary>
	public static List<string> ProjectTypedSdkLibraries(string depsJson)
	{
		List<string> offenders = [];
		using JsonDocument document = JsonDocument.Parse(depsJson);
		if (!document.RootElement.TryGetProperty("libraries", out JsonElement libraries))
		{
			return offenders;
		}

		foreach (JsonProperty library in libraries.EnumerateObject())
		{
			if (library.Name.StartsWith(UmbrellaPackage.Id, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(StringProperty(library.Value, "type"), "project", StringComparison.Ordinal))
			{
				offenders.Add($"library '{library.Name}' is project-typed");
			}
		}

		return offenders;
	}

	/// <summary>
	///     Every way <paramref name="manifestText" /> reaches a development workspace: the repository root written with
	///     <c>\</c>, JSON-escaped <c>\\</c> or <c>/</c> separators (case-insensitive), or an <c>additionalProbingPaths</c>
	///     entry, which is how a <c>runtimeconfig</c> points the host at a developer's NuGet folder.
	/// </summary>
	public static List<string> WorkspaceLeaks(string manifestText, string repositoryRoot)
	{
		List<string> offenders = [];
		string backslashed = repositoryRoot.TrimEnd('\\', '/').Replace('/', '\\');
		string[] forms =
		[
			backslashed,
			backslashed.Replace("\\", "\\\\", StringComparison.Ordinal),
			backslashed.Replace('\\', '/')
		];
		foreach (string form in forms)
		{
			if (manifestText.Contains(form, StringComparison.OrdinalIgnoreCase))
			{
				offenders.Add($"contains the repository root as '{form}'");
			}
		}

		if (manifestText.Contains(AdditionalProbingPaths, StringComparison.Ordinal))
		{
			offenders.Add($"declares {AdditionalProbingPaths}");
		}

		return offenders;
	}

	/// <summary>
	///     Whether a deployed file is a Lua runtime (<c>lua*.dll</c>, case-insensitive). The bridge,
	///     <c>cheatengine-sdk-lua-bridge.dll</c>, is not one: it binds to the host's already-loaded Lua.
	/// </summary>
	public static bool IsLuaRuntimeFileName(string fileName)
	{
		return fileName.StartsWith("lua", StringComparison.OrdinalIgnoreCase)
			   && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
	}

	private static string StringProperty(JsonElement element, string name)
	{
		return element.ValueKind == JsonValueKind.Object
			   && element.TryGetProperty(name, out JsonElement value)
			   && value.ValueKind == JsonValueKind.String
			? value.GetString()!
			: "";
	}
}
