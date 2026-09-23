using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Abi;

namespace CheatEngine.SDK.Repository.Tests.SourceScanning;

/// <summary>
///     A small heuristic reader of <c>[Trait("Qualification", "Qxx")]</c> attributes on a named test method, read as
///     text (no Roslyn package). It looks at the attribute block directly above the method declaration, which is this
///     repository's consistent style: one blank line separates test methods, and every attribute of a method sits on
///     its own line immediately above it.
/// </summary>
internal static partial class TestMethodTraits
{
	/// <summary>
	///     The <c>Qualification</c> trait values declared immediately above <paramref name="methodName" /> in the file at
	///     <paramref name="repositoryRelativePath" />, or <see langword="null" /> when the method does not exist there.
	/// </summary>
	internal static IReadOnlyList<string>? Of(string repositoryRelativePath, string methodName)
	{
		string source = RepositoryDocument.ReadNormalizedText(repositoryRelativePath);
		Match method = MethodDeclaration(methodName).Match(source);
		if (!method.Success)
		{
			return null;
		}

		int start = source.LastIndexOf("\n\n", method.Index, StringComparison.Ordinal);
		start = start < 0 ? 0 : start + 2;
		string attributeBlock = source[start..method.Index];
		return [.. QualificationTrait().Matches(attributeBlock).Select(static match => match.Groups["id"].Value)];
	}

	private static Regex MethodDeclaration(string methodName)
	{
		return new Regex($@"\bvoid\s+{Regex.Escape(methodName)}\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	}

	[GeneratedRegex(@"Trait\(\s*""Qualification""\s*,\s*""(?<id>[^""]+)""\s*\)", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex QualificationTrait();
}
