using System.Collections.Concurrent;
using System.Collections.Generic;

using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Generation;

/// <summary>
///     What <see cref="LuaBindingAnalyzer" /> learns about the compilation's otherwise-exportable <c>[LuaFunction]</c>
///     methods and can only judge once the whole compilation has been seen: two members of the same containing type
///     registering the same Lua name (CESDK2005). The group condition cannot be decided from one method at a time,
///     because the answer depends on every sibling member of the containing type.
/// </summary>
/// <remarks>
///     One instance per compilation, created in the compilation-start action and captured by the symbol action of that
///     compilation only, never stored in an analyzer field (RS1008). Thread safety: the symbol action adds concurrently;
///     the compilation-end action runs after all of them and is the only reader. Mirrors the generator's own grouping
///     input (<c>LuaFunctionTables.Group</c> filters on <c>LuaFunctionModel.IsValid</c> before grouping by name): only a
///     method that has no other <see cref="LuaFunctionShapeIssues" /> flag and no <see cref="ContainingTypeIssues" /> is
///     recorded as a candidate, because a method that already fails for another reason was never a candidate for the
///     generator's registration table either, duplicate name or not.
/// </remarks>
internal sealed class LuaFunctionDuplicateState
{
	private readonly
		ConcurrentDictionary<(string ContainingType, string LuaName),
			ConcurrentQueue<(string MethodName, Location Location)>> _candidates = new();

	/// <summary>Records a method that would be exported if no sibling of its containing type registered the same Lua name.</summary>
	public void AddCandidate(INamedTypeSymbol containingType, string luaName, string methodName, Location location)
	{
		(string, string) key = (containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), luaName);
		ConcurrentQueue<(string, Location)> members =
			_candidates.GetOrAdd(key, static _ => new ConcurrentQueue<(string, Location)>());
		members.Enqueue((methodName, location));
	}

	/// <summary>
	///     The compilation-end action: reports CESDK2005 for every member of a group of two or more.
	/// </summary>
	public void Report(CompilationAnalysisContext context)
	{
		foreach (ConcurrentQueue<(string MethodName, Location Location)> members in _candidates.Values)
		{
			if (members.Count < 2)
			{
				continue;
			}

			foreach ((string methodName, Location location) in members)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					DiagnosticDescriptors.DuplicateLuaName,
					location,
					methodName,
					LuaNameFor(members)));
			}
		}
	}

	private string LuaNameFor(ConcurrentQueue<(string MethodName, Location Location)> members)
	{
		foreach (KeyValuePair<(string ContainingType, string LuaName),
			         ConcurrentQueue<(string MethodName, Location Location)>> pair in _candidates)
		{
			if (ReferenceEquals(pair.Value, members))
			{
				return pair.Key.LuaName;
			}
		}

		return string.Empty;
	}
}
