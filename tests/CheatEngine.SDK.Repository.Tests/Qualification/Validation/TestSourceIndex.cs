using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     A text index of the <c>Qualification</c> traits in the test sources. A trait counts as evidence only on a test:
///     in the attribute block directly above a method signature, or in the attribute block of a top-level test class,
///     where it applies, as in xUnit, to every <c>[Fact]</c>/<c>[Theory]</c> method the class declares. A trait anywhere
///     else (a nested type, a class without test methods, no declaration below it) is reported as a violation.
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

	// [Fact], [Theory], [Fact(...)], [Xunit.Theory], [SomeFact] and attribute lists such as [Fact, Trait(...)].
	private static readonly Regex TestAttribute = new(
		"[\\[,]\\s*(?:[A-Za-z_][A-Za-z0-9_]*\\.)*[A-Za-z0-9_]*(?:Fact|Theory)(?:Attribute)?\\s*[\\](,]",
		RegexOptions.CultureInvariant, RegexTimeout);

	private TestSourceIndex(IReadOnlyList<TraitUse> traits, IReadOnlyList<string> violations)
	{
		Traits = traits;
		Violations = violations;
	}

	/// <summary>Every Qualification trait that applies to a test method, one entry per method and value.</summary>
	internal IReadOnlyList<TraitUse> Traits
	{
		get;
	}

	/// <summary>Traits that apply to no test method (nested type, class without tests, no declaration).</summary>
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

	/// <summary>Indexes one source given as lines, reported under <paramref name="file" />.</summary>
	internal static TestSourceIndex ScanSource(string file, string[] lines)
	{
		List<TraitUse> traits = [];
		List<string> violations = [];
		ScanFile(file, lines, traits, violations);
		return new TestSourceIndex(traits, violations);
	}

	/// <summary>
	///     Returns the Qualification traits that apply to <paramref name="methodName" /> declared in type
	///     <paramref name="className" /> of <paramref name="file" />, or <see langword="null" /> when the method is absent.
	/// </summary>
	internal static IReadOnlyList<string>? TraitsOfMethod(string file, string className, string methodName)
	{
		string path = QualificationDocuments.Absolute(file);
		return File.Exists(path) ? TraitsOfMethod(File.ReadAllLines(path), className, methodName) : null;
	}

	/// <summary>
	///     Returns the Qualification traits in the attribute block of the method and, when it is a <c>[Fact]</c> or
	///     <c>[Theory]</c>, those of its top-level class; <see langword="null" /> when the method is absent.
	/// </summary>
	internal static IReadOnlyList<string>? TraitsOfMethod(string[] lines, string className, string methodName)
	{
		foreach (MethodSite method in Methods(lines))
		{
			if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
				!string.Equals(method.TypeName, className, StringComparison.Ordinal))
			{
				continue;
			}

			string block = AttributeBlockAbove(lines, method.Index);
			List<string> values = TraitValues(block);
			if (IsTestMethod(block))
			{
				values.AddRange(ClassTraits(lines, className));
			}

			return values;
		}

		return null;
	}

	private static void ScanFile(string file, string[] lines, List<TraitUse> traits, List<string> violations)
	{
		string? currentType = null;
		List<ClassTrait> classTraits = [];
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

				if (TypeDeclaration.IsMatch(lines[declaration]))
				{
					string? typeName = TopLevelTypeName(lines[declaration]);
					if (typeName is null)
					{
						violations.Add(location + " Qualification trait '" + value +
									   "' is on a nested type; class-level traits apply to top-level test classes only.");
					}
					else
					{
						classTraits.Add(new ClassTrait(typeName, value, index + 1, location));
					}

					continue;
				}

				Match method = MethodDeclaration.Match(lines[declaration]);
				if (!method.Success)
				{
					violations.Add(location + " Qualification trait '" + value +
								   "' is not on a test method or a top-level test class.");
					continue;
				}

				traits.Add(new TraitUse(file, currentType ?? string.Empty, method.Groups["name"].Value, value,
					index + 1));
			}
		}

		if (classTraits.Count > 0)
		{
			ApplyClassTraits(file, lines, classTraits, traits, violations);
		}
	}

	// xUnit applies a class-level trait to every test method of the class: one TraitUse per [Fact]/[Theory] method.
	private static void ApplyClassTraits(string file, string[] lines, List<ClassTrait> classTraits,
		List<TraitUse> traits, List<string> violations)
	{
		List<MethodSite> methods = Methods(lines);
		foreach (ClassTrait classTrait in classTraits)
		{
			int applied = 0;
			foreach (MethodSite method in methods)
			{
				if (string.Equals(method.TypeName, classTrait.TypeName, StringComparison.Ordinal) &&
					IsTestMethod(AttributeBlockAbove(lines, method.Index)))
				{
					traits.Add(new TraitUse(file, classTrait.TypeName, method.Name, classTrait.Value, classTrait.Line));
					applied++;
				}
			}

			if (applied == 0)
			{
				violations.Add(classTrait.Location + " Qualification trait '" + classTrait.Value + "' is on class " +
							   classTrait.TypeName + ", which declares no [Fact] or [Theory] method.");
			}
		}
	}

	// Every method declaration with the top-level type that contains it (methods of nested types count for the
	// top-level type, as the attribute scan has always done).
	private static List<MethodSite> Methods(string[] lines)
	{
		List<MethodSite> methods = [];
		string? currentType = null;
		for (int index = 0; index < lines.Length; index++)
		{
			currentType = TopLevelTypeName(lines[index]) ?? currentType;
			if (currentType is null || TypeDeclaration.IsMatch(lines[index]))
			{
				continue;
			}

			Match method = MethodDeclaration.Match(lines[index]);
			if (method.Success)
			{
				methods.Add(new MethodSite(currentType, method.Groups["name"].Value, index));
			}
		}

		return methods;
	}

	private static List<string> ClassTraits(string[] lines, string className)
	{
		for (int index = 0; index < lines.Length; index++)
		{
			if (string.Equals(TopLevelTypeName(lines[index]), className, StringComparison.Ordinal))
			{
				return TraitValues(AttributeBlockAbove(lines, index));
			}
		}

		return [];
	}

	private static bool IsTestMethod(string attributeBlock)
	{
		return TestAttribute.IsMatch(attributeBlock);
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

	/// <summary>One Qualification trait applied to one test method.</summary>
	/// <param name="File">The repository-relative source file.</param>
	/// <param name="ClassName">The top-level class that declares the method.</param>
	/// <param name="MethodName">The test method.</param>
	/// <param name="Value">The trait value, a matrix row id.</param>
	/// <param name="Line">The 1-based line of the trait (on the method or on its class).</param>
	internal sealed record TraitUse(string File, string ClassName, string MethodName, string Value, int Line)
	{
		/// <summary>The <c>Class.Method</c> form used by Automated evidence.</summary>
		internal string Test => ClassName + "." + MethodName;
	}

	private sealed record MethodSite(string TypeName, string Name, int Index);

	private sealed record ClassTrait(string TypeName, string Value, int Line, string Location);
}
