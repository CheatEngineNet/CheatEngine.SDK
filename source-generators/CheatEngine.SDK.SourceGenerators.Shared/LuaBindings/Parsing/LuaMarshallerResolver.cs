using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Resolves an explicit SDK <c>[LuaMarshaller(typeof(TMarshaller))]</c> to a value-only emission model. Both the
///     annotation and <c>ILuaMarshaller&lt;T&gt;</c> are supplied as resolved SDK symbols, so a source declaration with
///     the same metadata name cannot impersonate either contract. The concrete type must also expose directly callable
///     static <c>Push</c> and <c>TryRead</c> methods: generated code deliberately names the concrete marshaller rather
///     than dispatching through the static-abstract interface.
/// </summary>
internal static class LuaMarshallerResolver
{
	/// <summary>
	///     Gets the explicit marshaller model for <paramref name="valueType" />, or reports whether an annotation was
	///     present but invalid. A missing annotation is not an error and leaves <paramref name="marshaller" /> null.
	/// </summary>
	public static bool TryResolve(Compilation? compilation, INamedTypeSymbol bindingType, ITypeSymbol valueType,
		ImmutableArray<AttributeData> attributes,
		INamedTypeSymbol? marshallerAttribute, INamedTypeSymbol? marshallerContract,
		out LuaCustomMarshallerModel? marshaller, out bool hasAttribute)
	{
		marshaller = null;
		hasAttribute = false;
		if (marshallerAttribute is null || marshallerContract is null)
		{
			return true;
		}

		AttributeData? attribute = null;
		foreach (AttributeData candidate in attributes)
		{
			if (!SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, marshallerAttribute))
			{
				continue;
			}

			attribute = candidate;
			break;
		}

		if (attribute is null)
		{
			return true;
		}

		hasAttribute = true;

		ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
		if (arguments.Length != 1 || arguments[0] is not { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol type })
		{
			return false;
		}

		if (!Implements(type, marshallerContract, valueType)
		    || !HasCallableStaticContract(compilation, bindingType, type, marshallerContract, valueType))
		{
			return false;
		}

		string valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		string marshallerTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		marshaller =
			new LuaCustomMarshallerModel(valueTypeName, marshallerTypeName, "value", valueType.IsReferenceType);
		return true;
	}

	private static bool Implements(INamedTypeSymbol candidate, INamedTypeSymbol contract, ITypeSymbol valueType)
	{
		foreach (INamedTypeSymbol implementation in candidate.AllInterfaces)
		{
			if (!SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, contract)
			    || implementation.TypeArguments.Length != 1)
			{
				continue;
			}

			if (SymbolEqualityComparer.Default.Equals(implementation.TypeArguments[0], valueType))
			{
				return true;
			}
		}

		return false;
	}

	// ILuaMarshaller<T> admits explicit static implementations. They satisfy a generic constraint, but the generated
	// source intentionally emits TMarshaller.Push(...) and TMarshaller.TryRead(...), which cannot name an explicit
	// interface member. Resolve the closed SDK contract so this remains exact if its LuaState or out-value types evolve.
	private static bool HasCallableStaticContract(Compilation? compilation, INamedTypeSymbol bindingType,
		INamedTypeSymbol candidate, INamedTypeSymbol contract, ITypeSymbol valueType)
	{
		if (compilation is not null && !compilation.IsSymbolAccessibleWithin(candidate, bindingType))
		{
			return false;
		}

		INamedTypeSymbol closedContract = contract.Construct(valueType);
		return HasCallableStaticMethod(compilation, bindingType, candidate, closedContract, "Push")
		       && HasCallableStaticMethod(compilation, bindingType, candidate, closedContract, "TryRead");
	}

	private static bool HasCallableStaticMethod(Compilation? compilation, INamedTypeSymbol bindingType,
		INamedTypeSymbol candidate, INamedTypeSymbol closedContract, string name)
	{
		IMethodSymbol? required = null;
		foreach (ISymbol member in closedContract.GetMembers(name))
		{
			if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				required = method;
				break;
			}
		}

		if (required is null)
		{
			return false;
		}

		foreach (ISymbol member in candidate.GetMembers(name))
		{
			if (member is not IMethodSymbol method
			    || method.MethodKind != MethodKind.Ordinary
			    || !method.IsStatic
			    || method.IsAbstract
			    || method.Arity != 0
			    || (compilation is not null && !compilation.IsSymbolAccessibleWithin(method, bindingType))
			    || !HasMatchingSignature(method, required))
			{
				continue;
			}

			return true;
		}

		return false;
	}

	private static bool HasMatchingSignature(IMethodSymbol candidate, IMethodSymbol required)
	{
		if (candidate.ReturnsVoid != required.ReturnsVoid
		    || !SymbolEqualityComparer.Default.Equals(candidate.ReturnType, required.ReturnType)
		    || candidate.Parameters.Length != required.Parameters.Length)
		{
			return false;
		}

		for (int i = 0; i < candidate.Parameters.Length; i++)
		{
			IParameterSymbol actual = candidate.Parameters[i];
			IParameterSymbol expected = required.Parameters[i];
			if (actual.RefKind != expected.RefKind
			    || !SymbolEqualityComparer.Default.Equals(actual.Type, expected.Type))
			{
				return false;
			}
		}

		return true;
	}
}
