using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     The <c>CheatEngine.SDK.Lua</c> contract types a binding signature can name besides the marshalled scalars:
///     <c>LuaOptional&lt;T&gt;</c> and <c>LuaOperationStatus</c>. They are recognised by symbol identity only, resolved
///     from the <c>CheatEngine.SDK.Lua</c> assembly itself; a type that merely has the same namespace, name and arity (in
///     the consumer's source or in another assembly) is a look-alike, reported and never taken as the contract.
/// </summary>
/// <remarks>
///     <c>Compilation.GetTypeByMetadataName</c> is not used: it returns the compilation's own type first and
///     <see langword="null" /> when several references define the name, so a source look-alike would hide the real
///     type. <c>Compilation.GetTypesByMetadataName</c> returns every candidate and the defining assembly decides
///     (https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.compilation.gettypesbymetadataname).
/// </remarks>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaContractTypes
{
	/// <summary>The assembly that defines every Lua contract type.</summary>
	public const string LuaAssemblyName = "CheatEngine.SDK.Lua";

	/// <summary>Metadata name of <c>CheatEngine.SDK.Lua.Marshalling.LuaOptional&lt;T&gt;</c>.</summary>
	public const string LuaOptionalMetadataName = "CheatEngine.SDK.Lua.Marshalling.LuaOptional`1";

	/// <summary>Metadata name of <c>CheatEngine.SDK.Lua.Calls.LuaOperationStatus</c>.</summary>
	public const string LuaOperationStatusMetadataName = "CheatEngine.SDK.Lua.Calls.LuaOperationStatus";

	/// <summary>
	///     The type named <paramref name="metadataName" /> defined by <c>CheatEngine.SDK.Lua</c> (a reference or the
	///     compilation itself), or <see langword="null" /> when that assembly does not define it.
	/// </summary>
	public static INamedTypeSymbol? Resolve(Compilation compilation, string metadataName)
	{
		if (compilation is null)
		{
			throw new ArgumentNullException(nameof(compilation));
		}

		foreach (INamedTypeSymbol type in compilation.GetTypesByMetadataName(metadataName))
		{
			if (string.Equals(type.ContainingAssembly?.Name, LuaAssemblyName, StringComparison.Ordinal))
			{
				return type;
			}
		}

		return null;
	}

	/// <summary>Whether <paramref name="type" /> is (a construction of) the resolved <paramref name="expected" />.</summary>
	public static bool Is(ITypeSymbol type, INamedTypeSymbol? expected)
	{
		return expected is not null
			   && type is INamedTypeSymbol named
			   && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expected);
	}

	/// <summary>
	///     Whether <paramref name="type" /> has the namespace, name and arity of <paramref name="metadataName" /> but is not
	///     the resolved <paramref name="expected" /> (also when the real type could not be resolved at all).
	/// </summary>
	public static bool IsLookAlike(ITypeSymbol type, INamedTypeSymbol? expected, string metadataName)
	{
		return type is INamedTypeSymbol named
			   && !Is(type, expected)
			   && HasMetadataName(named.OriginalDefinition, metadataName);
	}

	/// <summary>Whether a top-level <paramref name="type" /> has the full metadata name <paramref name="metadataName" />.</summary>
	public static bool HasMetadataName(INamedTypeSymbol type, string metadataName)
	{
		if (type.ContainingType is not null)
		{
			return false;
		}

		int end = metadataName.Length;
		int dot = metadataName.LastIndexOf('.');
		if (!NameEquals(type.MetadataName, metadataName, dot + 1, end))
		{
			return false;
		}

		INamespaceSymbol? current = type.ContainingNamespace;
		end = dot;
		while (current is not null && !current.IsGlobalNamespace)
		{
			if (end <= 0)
			{
				return false;
			}

			dot = metadataName.LastIndexOf('.', end - 1);
			if (!NameEquals(current.Name, metadataName, dot + 1, end))
			{
				return false;
			}

			end = dot;
			current = current.ContainingNamespace;
		}

		return end == -1;
	}

	private static bool NameEquals(string name, string metadataName, int start, int end)
	{
		return name.Length == end - start
			   && string.CompareOrdinal(name, 0, metadataName, start, name.Length) == 0;
	}
}
