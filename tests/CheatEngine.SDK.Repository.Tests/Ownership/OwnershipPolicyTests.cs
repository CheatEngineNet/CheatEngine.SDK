using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Ownership;

/// <summary>
///     Ownership rules of the shipping SDK sources that a type check cannot express: no finalizer ever repairs a forgotten
///     cleanup (audit A08-25), and Cheat Engine's global that deletes every registered symbol is never bound, because it
///     is not a per-plugin cleanup path (audit A14-22).
/// </summary>
public sealed class OwnershipPolicyTests
{
	private const string DeleteAllRegisteredSymbols = "deleteAll" + "RegisteredSymbols";

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	// A destructor declaration: '~Name(' at the start of a code line, outside comments.
	private static readonly Regex FinalizerDeclaration = new(
		"^\\s*~\\s*[A-Za-z_][A-Za-z0-9_]*\\s*\\(", RegexOptions.CultureInvariant, RegexTimeout);

	[Fact]
	public void No_shipping_library_declares_a_finalizer()
	{
		List<string> offenders = [];
		foreach (string file in ShippingSources())
		{
			offenders.AddRange(FindFinalizers(file, File.ReadAllLines(Absolute(file))));
		}

		Assert.True(offenders.Count == 0,
			"A finalizer must never touch Cheat Engine, the GUI or a Lua state: " + string.Join(", ", offenders));
	}

	[Fact]
	public void No_shipping_library_binds_delete_all_registered_symbols()
	{
		List<string> offenders = [];
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*"))
		{
			bool shippingCode = (file.StartsWith("libs/", StringComparison.Ordinal) ||
			                     file.StartsWith("src/", StringComparison.Ordinal)) &&
			                    file.EndsWith(".cs", StringComparison.Ordinal);
			bool generatorSpec = file.StartsWith("source-generators/", StringComparison.Ordinal) &&
			                     file.Contains("/Specs/", StringComparison.Ordinal);
			if ((shippingCode || generatorSpec) &&
			    File.ReadAllText(Absolute(file)).Contains(DeleteAllRegisteredSymbols, StringComparison.Ordinal))
			{
				offenders.Add(file);
			}
		}

		Assert.True(offenders.Count == 0,
			DeleteAllRegisteredSymbols +
			" removes every plugin's and script's symbols; bind per-plugin cleanup instead: " +
			string.Join(", ", offenders));
	}

	[Fact]
	public void Finalizer_scan_finds_a_destructor_and_ignores_comments_and_operators()
	{
		string[] lines =
		[
			"public sealed class Holder",
			"{",
			"\t~Holder()",
			"\t{",
			"\t}",
			"\t// ~Commented() is not a declaration",
			"\t/// <see cref=\"Holder.~Holder\" />",
			"\tint Flip(int value) => ~value;",
			"}"
		];

		Assert.Equal(["libs/Sample/Holder.cs:3"], FindFinalizers("libs/Sample/Holder.cs", lines));
	}

	private static IEnumerable<string> ShippingSources()
	{
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs"))
		{
			if (file.StartsWith("libs/", StringComparison.Ordinal) || file.StartsWith("src/", StringComparison.Ordinal))
			{
				yield return file;
			}
		}
	}

	private static List<string> FindFinalizers(string file, string[] lines)
	{
		List<string> hits = [];
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
			{
				continue;
			}

			if (FinalizerDeclaration.IsMatch(line))
			{
				hits.Add(file + ":" + (index + 1).ToString(CultureInfo.InvariantCulture));
			}
		}

		return hits;
	}

	private static string Absolute(string relative)
	{
		return Path.Combine(RepositoryRoot.Path, relative);
	}
}
