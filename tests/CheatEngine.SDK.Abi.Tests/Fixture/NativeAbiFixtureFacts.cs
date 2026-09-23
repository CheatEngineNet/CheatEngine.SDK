namespace CheatEngine.SDK.Abi.Tests.Fixture;

/// <summary>
///     Locates and parses the <c>key=value</c> facts emitted by <c>tests/native-abi-fixture/build.ps1</c>. The native CI
///     job builds them and the Debug build-test job passes their path with <see cref="FactsPathEnvironmentVariable" />
///     and sets <see cref="RequiredEnvironmentVariable" /> to <c>true</c>; an ordinary local run has neither and the
///     fixture-dependent tests return after asserting that documented opt-out (never <c>Assert.Skip</c>).
/// </summary>
internal static class NativeAbiFixtureFacts
{
	/// <summary>Name of the CI-provided absolute path to the validated native fixture facts file.</summary>
	internal const string FactsPathEnvironmentVariable = "CE77_NATIVE_ABI_FACTS_PATH";

	/// <summary>Name of the opt-in gate that makes the native fixture facts mandatory.</summary>
	internal const string RequiredEnvironmentVariable = "CE77_NATIVE_ABI_REQUIRED";

	/// <summary>The fixture schema this test assembly compares against.</summary>
	internal const string ExpectedSchema = "3";

	/// <summary>
	///     Loads the facts named by the environment, or returns <see langword="null" /> when no path is supplied and
	///     required mode is off. Throws when required mode is on without a path, or when a supplied file is missing.
	/// </summary>
	internal static Dictionary<string, string>? LoadFromEnvironment()
	{
		string? factsPath = ResolveFactsPath(
			Environment.GetEnvironmentVariable(FactsPathEnvironmentVariable),
			Environment.GetEnvironmentVariable(RequiredEnvironmentVariable));
		return factsPath is null ? null : ReadFacts(factsPath);
	}

	/// <summary>Applies the opt-in rule: a supplied path wins; required mode without a path is an error.</summary>
	internal static string? ResolveFactsPath(string? factsPath, string? requiredMode)
	{
		if (!string.IsNullOrWhiteSpace(factsPath))
		{
			return factsPath;
		}

		if (string.Equals(requiredMode, "true", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"'{RequiredEnvironmentVariable}=true' requires '{FactsPathEnvironmentVariable}' to name a validated native ABI fixture facts file.");
		}

		return null;
	}

	/// <summary>Parses a facts file: one <c>key=value</c> per non-blank line, keys unique.</summary>
	internal static Dictionary<string, string> ReadFacts(string factsPath)
	{
		if (!File.Exists(factsPath))
		{
			throw new FileNotFoundException($"The native ABI fixture facts file '{factsPath}' was not found.",
				factsPath);
		}

		Dictionary<string, string> facts = new(StringComparer.Ordinal);
		foreach (string line in File.ReadLines(factsPath))
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			int separator = line.IndexOf('=', StringComparison.Ordinal);
			Assert.True(separator > 0, $"The native ABI fixture fact '{line}' is not key=value.");
			string key = line[..separator];
			string value = line[(separator + 1)..];
			Assert.True(facts.TryAdd(key, value), $"The native ABI fixture emitted duplicate fact '{key}'.");
		}

		return facts;
	}
}
