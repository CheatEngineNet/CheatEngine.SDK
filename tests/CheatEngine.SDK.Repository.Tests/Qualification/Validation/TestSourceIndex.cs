using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     A text index of the <c>Qualification</c> traits in the test sources. A trait counts as evidence only on a test
///     method: it sits in the attribute block directly above the method signature. A class-level trait is reported as a
///     violation, because it would qualify every method of the class at once.
/// </summary>
internal sealed class TestSourceIndex
{
	internal const string TraitName = "Qualification";

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	// Split so that this file never contains a literal trait the index would pick up.
	private static readonly Regex TraitPattern = new(
		"Trait\\s*\\(\\s*\"" + TraitName + "\"\\s*,\\s*\"(?<value>[^\"]*)\"\\s*\\)",
		RegexOptions.CultureInvariant, RegexTimeout);

	private static readonly Regex TypeDeclaration = new(
		"\\b(?:class|record|struct|interface)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
		RegexOptions.CultureInvariant, RegexTimeout);

	private static readonly Regex MethodDeclaration = new(
		"^\\s*(?:(?:public|internal|private|protected|static|async|unsafe|override|virtual|sealed|new)\\s+)+[A-Za-z_][A-Za-z0-9_<>,.?\\[\\] ]*\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(",
		RegexOptions.CultureInvariant, RegexTimeout);

	private TestSourceIndex(IReadOnlyList<TraitUse> traits, IReadOnlyList<string> violations)
	{
		Traits = traits;
		Violations = violations;
	}

	/// <summary>Every method-level Qualification trait found.</summary>
	internal IReadOnlyList<TraitUse> Traits
	{
		get;
	}

	/// <summary>Traits that are not on a method (class-level, or not followed by a declaration).</summary>
	internal IReadOnlyList<string> Violations
	{
		get;
	}

	/// <summary>Scans <c>tests/**/*.cs</c> below the repository root, skipping build output.</summary>
	internal static TestSourceIndex Scan()
	{
		List<TraitUse> traits = [];
		List<string> violations = [];
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs"))
		{
			if (!file.StartsWith("tests/", StringComparison.Ordinal))
			{
				continue;
			}

			string[] lines = File.ReadAllLines(QualificationDocuments.Absolute(file));
			ScanFile(file, lines, traits, violations);
		}

		return new TestSourceIndex(traits, violations);
	}

	/// <summary>
	///     Returns the Qualification traits in the attribute block of <paramref name="methodName" /> declared in type
	///     <paramref name="className" /> of <paramref name="file" />, or <see langword="null" /> when the method is absent.
	/// </summary>
	internal static IReadOnlyList<string>? TraitsOfMethod(string file, string className, string methodName)
	{
		string path = QualificationDocuments.Absolute(file);
		if (!File.Exists(path))
		{
			return null;
		}

		string[] lines = File.ReadAllLines(path);
		string? currentType = null;
		for (int index = 0; index < lines.Length; index++)
		{
			currentType = TopLevelTypeName(lines[index]) ?? currentType;
			Match method = MethodDeclaration.Match(lines[index]);
			if (method.Success && string.Equals(method.Groups["name"].Value, methodName, StringComparison.Ordinal) &&
				string.Equals(currentType, className, StringComparison.Ordinal))
			{
				return TraitValues(AttributeBlockAbove(lines, index));
			}
		}

		return null;
	}

	private static void ScanFile(string file, string[] lines, List<TraitUse> traits, List<string> violations)
	{
		string? currentType = null;
		for (int index = 0; index < lines.Length; index++)
		{
			currentType = TopLevelTypeName(lines[index]) ?? currentType;
			foreach (Match trait in TraitPattern.Matches(lines[index]))
			{
				string value = trait.Groups["value"].Value;
				string location = file + ":" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
				int declaration = NextDeclaration(lines, index);
				if (declaration < 0)
				{
					violations.Add(location + " Qualification trait '" + value + "' is not followed by a declaration.");
					continue;
				}

				Match method = MethodDeclaration.Match(lines[declaration]);
				if (!method.Success || TypeDeclaration.IsMatch(lines[declaration]))
				{
					violations.Add(location + " Qualification trait '" + value +
								   "' is not on a test method (method-level traits only).");
					continue;
				}

				traits.Add(new TraitUse(file, currentType ?? string.Empty, method.Groups["name"].Value, value,
					index + 1));
			}
		}
	}

	// Test sources use file-scoped namespaces, so a top-level type declaration starts in column 0; nested types are
	// indented and do not change the class that owns the following test methods.
	private static string? TopLevelTypeName(string line)
	{
		if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith("//", StringComparison.Ordinal) ||
			line.StartsWith('['))
		{
			return null;
		}

		Match type = TypeDeclaration.Match(line);
		return type.Success ? type.Groups["name"].Value : null;
	}

	private static int NextDeclaration(string[] lines, int attributeLine)
	{
		for (int index = attributeLine + 1; index < lines.Length; index++)
		{
			string trimmed = lines[index].Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
			{
				return -1;
			}

			bool isDeclaration = !trimmed.StartsWith('[') &&
								 (MethodDeclaration.IsMatch(lines[index]) || TypeDeclaration.IsMatch(lines[index]));
			if (isDeclaration || !IsAttributeOrContinuation(trimmed))
			{
				return index;
			}
		}

		return -1;
	}

	// The attribute block is the run of lines directly above the declaration that are attributes or their wrapped
	// arguments. It stops at a blank line, a comment or the end of the previous member.
	private static string AttributeBlockAbove(string[] lines, int declaration)
	{
		int start = declaration;
		for (int index = declaration - 1; index >= 0; index--)
		{
			string trimmed = lines[index].Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal) ||
				(!trimmed.StartsWith('[') && (trimmed.EndsWith('}') || trimmed.EndsWith(';') || trimmed.EndsWith('{'))))
			{
				break;
			}

			start = index;
		}

		return string.Join('\n', lines[start..declaration]);
	}

	private static bool IsAttributeOrContinuation(string trimmed)
	{
		return trimmed.StartsWith('[') || trimmed.StartsWith('"') || trimmed.StartsWith("Justification",
			StringComparison.Ordinal) || trimmed.EndsWith(']') || trimmed.EndsWith(',') || trimmed.EndsWith('=');
	}

	private static List<string> TraitValues(string attributeBlock)
	{
		List<string> values = [];
		foreach (Match trait in TraitPattern.Matches(attributeBlock))
		{
			values.Add(trait.Groups["value"].Value);
		}

		return values;
	}

	/// <summary>One method-level Qualification trait.</summary>
	internal sealed record TraitUse(string File, string ClassName, string MethodName, string Value, int Line)
	{
		/// <summary>The <c>Class.Method</c> form used by Automated evidence.</summary>
		internal string Test => ClassName + "." + MethodName;
	}
}
