using System;

using CheatEngine.SDK.SourceGenerators.Shared;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Parsing;

/// <summary>
///     Checks the one top-level type identity the entry-point generator owns. The host requires
///     <c>CESDK.CESDK</c>; emitting a second type with that identity would make an otherwise valid plugin assembly
///     fail compilation.
/// </summary>
internal static class EntryPointGeneratedIdentity
{
	/// <summary>
	///     Gets whether the current assembly declares the non-generic, non-file-local <c>CESDK.CESDK</c> type identity
	///     that generated code would declare.
	/// </summary>
	public static bool HasEntryPointTypeCollision(Compilation compilation)
	{
		foreach (INamespaceSymbol @namespace in compilation.GlobalNamespace.GetNamespaceMembers())
		{
			if (!string.Equals(@namespace.Name, ManagedEntryPointNames.Namespace, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (INamedTypeSymbol type in @namespace.GetTypeMembers(ManagedEntryPointNames.TypeName))
			{
				if (type.Arity == 0
					&& !type.IsFileLocal
					&& SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
				{
					return true;
				}
			}
		}

		return false;
	}
}
