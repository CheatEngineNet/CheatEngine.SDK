namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>
///     The three records of intentional breaks against CheatEngine.SDK 1.0.0 agree: the ApiCompat baseline suppressions
///     of <c>src/CheatEngine.SDK/CompatibilitySuppressions.xml</c> (what the pack-time package validation reports), the
///     <c>*REMOVED*</c> lines of the <c>PublicAPI.Unshipped.txt</c> files (what the build's RS0017 reports), and the
///     reviewed list of changes ApiCompat cannot see. Suppressions that touch a type the Client consumes are listed in
///     <c>eng/api/client-induced-breaks.txt</c>.
/// </summary>
/// <remarks>
///     A suppression and a removed line are related by declaring type and member name (see
///     <see cref="PublicApiDeclarations" />), not by overload: removing one overload of a method whose other overload was
///     also removed cannot be told apart, which is acceptable because both are declared breaks. A DocId is mapped as
///     follows: <c>M:T.#ctor(...)</c> to the constructor line <c>T.&lt;SimpleName&gt;(</c>, <c>M:T.get_N</c>,
///     <c>M:T.set_N</c> and <c>P:T.N</c> to <c>T.N.get</c>/<c>.set</c>/<c>.init</c>, <c>F:T.N</c> to <c>T.N -&gt;</c> or the
///     enum member <c>T.N = v -&gt;</c>, and <c>T:X</c> to the type line <c>X</c>. Only rule ids with such a mapping are
///     accepted (CP0001, CP0002, CP0011); any other rule (for example CP0005, CP0006, CP0009 or CP0019) needs a reviewed
///     extension of this class, because its trace in the PublicAPI files differs. This is SDK-side C0 evidence for Q48; it
///     never closes Q48 at C1/C3 and never closes F05.
/// </remarks>
[Trait("Qualification", "Q48")]
public sealed class CompatibilitySuppressionTests
{
	private static readonly HashSet<string> s_mappedRuleIds = new(StringComparer.Ordinal) { "CP0001", "CP0002", "CP0011" };

	[Fact]
	public void Every_suppression_is_a_baseline_suppression_of_one_library_against_itself()
	{
		IReadOnlySet<string> assemblies = ApiContractFiles.PackageAssemblies;
		foreach (CompatibilitySuppression suppression in ApiContractFiles.ReadSuppressions())
		{
			string where = $"{suppression.DiagnosticId} {suppression.Target}";
			Assert.True(suppression.IsBaselineSuppression, $"{where} is not a baseline suppression.");
			Assert.True(string.Equals(suppression.Left, suppression.Right, StringComparison.Ordinal),
				$"{where} compares {suppression.Left} with {suppression.Right}.");
			Assert.True(s_mappedRuleIds.Contains(suppression.DiagnosticId),
				$"{where}: {suppression.DiagnosticId} is not an accepted rule id ({string.Join(", ", s_mappedRuleIds)}). Review the break, then extend {nameof(CompatibilitySuppressionTests)}.");
			Assert.True(assemblies.Contains(AssemblyOf(suppression)),
				$"{where} names '{suppression.Left}', which is not lib/net10.0/<embedded assembly>.dll.");
		}
	}

	[Fact]
	public void Every_baseline_suppression_matches_a_removed_public_api_line()
	{
		Dictionary<string, PublicApiLibrary> libraries = LibrariesByName();
		foreach (CompatibilitySuppression suppression in ApiContractFiles.ReadSuppressions())
		{
			string assembly = AssemblyOf(suppression);
			Assert.True(libraries.TryGetValue(assembly, out PublicApiLibrary? library),
				$"{suppression.Target}: '{assembly}' has no PublicAPI files, so the break cannot be declared there.");

			IReadOnlySet<string> candidates = PublicApiDeclarations.CandidateKeysOf(suppression.Target);
			bool declared = false;
			foreach (string removed in library.Removed)
			{
				declared |= candidates.Contains(PublicApiDeclarations.KeyOf(removed));
			}

			Assert.True(declared,
				$"{suppression.DiagnosticId} {suppression.Target} has no '*REMOVED*' line in {library.UnshippedPath} (looked for {string.Join(", ", candidates)}).");
		}
	}

	[Fact]
	public void Every_removed_public_api_line_is_suppressed_or_declared_invisible_to_apicompat()
	{
		HashSet<string> invisible = new(StringComparer.Ordinal);
		foreach ((string removedLine, _) in ApiContractFiles.ReadInvisibleChanges())
		{
			invisible.Add(removedLine);
		}

		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			HashSet<string> suppressedKeys = SuppressedKeys(library.Name);
			foreach (string removed in library.Removed)
			{
				bool suppressed = suppressedKeys.Contains(PublicApiDeclarations.KeyOf(removed));
				bool declaredInvisible = invisible.Contains(PublicApiLibrary.RemovedPrefix + removed);
				Assert.True(suppressed || declaredInvisible,
					$"{library.UnshippedPath}: '*REMOVED*{removed}' has no ApiCompat suppression. Regenerate CompatibilitySuppressions.xml, or add the line to {ApiContractFiles.InvisibleChangesFile} with the reason ApiCompat cannot see it.");
				Assert.False(suppressed && declaredInvisible,
					$"'*REMOVED*{removed}' is suppressed, so it must not also be listed in {ApiContractFiles.InvisibleChangesFile}.");
			}
		}
	}

	[Fact]
	public void Every_invisible_change_names_a_current_removed_line_with_a_reason()
	{
		HashSet<string> removedLines = new(StringComparer.Ordinal);
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			foreach (string removed in library.Removed)
			{
				removedLines.Add(PublicApiLibrary.RemovedPrefix + removed);
			}
		}

		foreach ((string removedLine, string reason) in ApiContractFiles.ReadInvisibleChanges())
		{
			Assert.True(removedLines.Contains(removedLine),
				$"{ApiContractFiles.InvisibleChangesFile}: '{removedLine}' is not a '*REMOVED*' line of any PublicAPI.Unshipped.txt (stale entry).");
			Assert.False(string.IsNullOrWhiteSpace(reason),
				$"{ApiContractFiles.InvisibleChangesFile}: '{removedLine}' needs ' | <reason>'.");
		}
	}

	[Fact]
	public void Suppressions_touching_client_consumed_types_are_listed_as_induced_client_breaks()
	{
		HashSet<string> consumed = new(StringComparer.Ordinal);
		foreach ((string typeName, _) in ApiContractFiles.ReadClientConsumedTypes())
		{
			consumed.Add(typeName);
		}

		List<string> expected = [];
		foreach (CompatibilitySuppression suppression in ApiContractFiles.ReadSuppressions())
		{
			if (consumed.Contains(PublicApiDeclarations.ContainingTypeOf(suppression.Target)))
			{
				expected.Add(suppression.InducedBreakLine);
			}
		}

		expected.Sort(StringComparer.Ordinal);
		IReadOnlyList<string> listed = ApiContractFiles.ReadEntries(ApiContractFiles.ClientInducedBreaksFile);
		Assert.True(expected.SequenceEqual(listed, StringComparer.Ordinal),
			$"{ApiContractFiles.ClientInducedBreaksFile} must list exactly (ordinal order):{Environment.NewLine}{string.Join(Environment.NewLine, expected)}");
	}

	[Fact]
	public void Every_client_consumed_type_resolves_in_the_declared_api_or_is_marked_unresolved()
	{
		HashSet<string> declaredTypes = new(StringComparer.Ordinal);
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			foreach (string line in library.Shipped.Concat(library.Declared))
			{
				if (PublicApiDeclarations.IsTypeLine(line))
				{
					declaredTypes.Add(PublicApiDeclarations.KeyOf(line));
				}
			}
		}

		IReadOnlyList<(string TypeName, bool MarkedUnresolved)> consumed = ApiContractFiles.ReadClientConsumedTypes();
		Assert.NotEmpty(consumed);
		Assert.Equal(consumed.Count, consumed.Select(static c => c.TypeName).Distinct(StringComparer.Ordinal).Count());
		foreach ((string typeName, bool markedUnresolved) in consumed)
		{
			bool resolves = declaredTypes.Contains(typeName);
			Assert.True(resolves != markedUnresolved,
				markedUnresolved
					? $"{typeName} is marked '# unresolved' but is declared now: remove the marker."
					: $"{typeName} is neither in the 1.0.0 surface nor in the declared API: fix the name or mark it '# unresolved (reason)'.");
		}
	}

	private static string AssemblyOf(CompatibilitySuppression suppression)
	{
		const string prefix = "lib/net10.0/";
		string left = suppression.Left;
		return left.StartsWith(prefix, StringComparison.Ordinal) && left.EndsWith(".dll", StringComparison.Ordinal)
			? left[prefix.Length..^".dll".Length]
			: left;
	}

	private static Dictionary<string, PublicApiLibrary> LibrariesByName()
	{
		Dictionary<string, PublicApiLibrary> libraries = new(StringComparer.Ordinal);
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			libraries.Add(library.Name, library);
		}

		return libraries;
	}

	private static HashSet<string> SuppressedKeys(string assembly)
	{
		HashSet<string> keys = new(StringComparer.Ordinal);
		foreach (CompatibilitySuppression suppression in ApiContractFiles.ReadSuppressions())
		{
			if (string.Equals(AssemblyOf(suppression), assembly, StringComparison.Ordinal))
			{
				keys.UnionWith(PublicApiDeclarations.CandidateKeysOf(suppression.Target));
			}
		}

		return keys;
	}
}
