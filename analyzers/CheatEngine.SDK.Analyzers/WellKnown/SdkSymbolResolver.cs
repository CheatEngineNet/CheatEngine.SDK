using System;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.Analyzers.WellKnown;

/// <summary>
///     Resolves SDK contracts by both metadata name and defining assembly. A metadata name alone is only a lookup key:
///     a source declaration or an unrelated reference can use the same namespace and type name without becoming an SDK
///     annotation, host base or Lua runtime type.
/// </summary>
internal static class SdkSymbolResolver
{
	private const string AnnotationsAssemblyName = "CheatEngine.SDK.Annotations";
	private const string HostingAssemblyName = "CheatEngine.SDK.Hosting";
	private const string LuaAssemblyName = "CheatEngine.SDK.Lua";

	/// <summary>Resolves an annotation that must be defined by <c>CheatEngine.SDK.Annotations</c>.</summary>
	public static INamedTypeSymbol? Annotation(Compilation compilation, string metadataName)
	{
		return Resolve(compilation, metadataName, AnnotationsAssemblyName);
	}

	/// <summary>Resolves a plugin-host contract that must be defined by <c>CheatEngine.SDK.Hosting</c>.</summary>
	public static INamedTypeSymbol? Hosting(Compilation compilation, string metadataName)
	{
		return Resolve(compilation, metadataName, HostingAssemblyName);
	}

	/// <summary>Resolves a Lua runtime contract that must be defined by <c>CheatEngine.SDK.Lua</c>.</summary>
	public static INamedTypeSymbol? Lua(Compilation compilation, string metadataName)
	{
		return Resolve(compilation, metadataName, LuaAssemblyName);
	}

	private static INamedTypeSymbol? Resolve(Compilation compilation, string metadataName, string assemblyName)
	{
		foreach (MetadataReference reference in compilation.References)
		{
			if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly
			    || !string.Equals(assembly.Identity.Name, assemblyName, StringComparison.Ordinal))
			{
				continue;
			}

			return assembly.GetTypeByMetadataName(metadataName);
		}

		return null;
	}
}
