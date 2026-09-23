using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Decides whether a <c>[LuaFunction]</c> method can be wrapped by a generated thunk and classifies its signature.
///     Symbols in, flags and a value-only signature out: the CESDK2xxx analyzer links this file (with
///     <see cref="LuaValueKindMapper" />, <see cref="ContainingTypeShape" /> and <c>LuaEmit/LuaNames.cs</c>) so that a
///     method the generator skips is exactly a method the analyzer reports.
/// </summary>
/// <remarks>
///     The rules, in the order the flags are set: an ordinary, static, non-generic, non-<see langword="async" /> method
///     (an <c>async void</c> method returns <see langword="void" /> like any other and would otherwise pass every other
///     check, but the thunk calls it synchronously and cannot catch what its continuation throws); parameters by value,
///     none optional or <see langword="params" />, each of a built-in marshalled kind or carrying an explicit valid
///     <c>[LuaMarshaller]</c>, except a leading <c>LuaState</c> that receives the callback's state; a return type that
///     is <see langword="void" /> or has the same conversion contract. The name and the containing type are checked by
///     the caller (<c>LuaNames.IsValidName</c>, <see cref="ContainingTypeShape" />);
///     duplicate names are a group rule (<c>LuaFunctionTables</c>).
/// </remarks>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaFunctionShape
{
	/// <summary>Inspects <paramref name="method" /> against resolved SDK Lua binding contracts.</summary>
	/// <param name="compilation">The consumer compilation that must be able to call a custom marshaller directly.</param>
	/// <param name="method">The attributed method.</param>
	/// <param name="luaState">The real Lua runtime state symbol, or <see langword="null" /> when it is unavailable.</param>
	/// <param name="luaMarshallerAttribute">The real SDK marshaller annotation, or <see langword="null" />.</param>
	/// <param name="luaMarshallerContract">The real SDK static marshaller contract, or <see langword="null" />.</param>
	/// <param name="signature">
	///     What could be classified; complete only when the result is
	///     <see cref="LuaFunctionShapeIssues.None" />.
	/// </param>
	public static LuaFunctionShapeIssues Inspect(Compilation compilation, IMethodSymbol method,
		INamedTypeSymbol? luaState,
		INamedTypeSymbol? luaMarshallerAttribute, INamedTypeSymbol? luaMarshallerContract,
		out LuaFunctionSignature signature)
	{
		return InspectCore(compilation, method, luaState, luaMarshallerAttribute, luaMarshallerContract,
			LuaContractTypes.Resolve(compilation, LuaContractTypes.LuaOptionalMetadataName), out signature);
	}

	/// <summary>Inspects <paramref name="method" /> against resolved SDK Lua binding contracts.</summary>
	/// <param name="compilation">The consumer compilation that must be able to call a custom marshaller directly.</param>
	/// <param name="method">The attributed method.</param>
	/// <param name="luaState">The real Lua runtime state symbol, or <see langword="null" /> when it is unavailable.</param>
	/// <param name="luaMarshallerAttribute">The real SDK marshaller annotation, or <see langword="null" />.</param>
	/// <param name="luaMarshallerContract">The real SDK static marshaller contract, or <see langword="null" />.</param>
	/// <param name="luaOptional">The real SDK <c>LuaOptional&lt;T&gt;</c>, or <see langword="null" />.</param>
	/// <param name="signature">
	///     What could be classified; complete only when the result is
	///     <see cref="LuaFunctionShapeIssues.None" />.
	/// </param>
	public static LuaFunctionShapeIssues Inspect(Compilation compilation, IMethodSymbol method,
		INamedTypeSymbol? luaState, INamedTypeSymbol? luaMarshallerAttribute,
		INamedTypeSymbol? luaMarshallerContract, INamedTypeSymbol? luaOptional,
		out LuaFunctionSignature signature)
	{
		return InspectCore(compilation, method, luaState, luaMarshallerAttribute, luaMarshallerContract,
			luaOptional, out signature);
	}

	/// <summary>
	///     Compatibility overload for consumers that only validate the built-in scalar contract. The LuaBindings
	///     generator and its analyzer call the overload that resolves <c>[LuaMarshaller]</c> and
	///     <c>LuaOptional&lt;T&gt;</c> explicitly; without a compilation every <c>LuaOptional</c> is a look-alike.
	/// </summary>
	public static LuaFunctionShapeIssues Inspect(IMethodSymbol method, INamedTypeSymbol? luaState,
		out LuaFunctionSignature signature)
	{
		return InspectCore(null, method, luaState, null, null, null, out signature);
	}

	private static LuaFunctionShapeIssues InspectCore(Compilation? compilation, IMethodSymbol method,
		INamedTypeSymbol? luaState, INamedTypeSymbol? luaMarshallerAttribute,
		INamedTypeSymbol? luaMarshallerContract, INamedTypeSymbol? luaOptional,
		out LuaFunctionSignature signature)
	{
		LuaFunctionShapeIssues issues = LuaFunctionShapeIssues.None;

		if (method.MethodKind != MethodKind.Ordinary)
		{
			issues |= LuaFunctionShapeIssues.NotOrdinaryMethod;
		}

		if (!method.IsStatic)
		{
			issues |= LuaFunctionShapeIssues.NotStatic;
		}

		if (method.IsGenericMethod)
		{
			issues |= LuaFunctionShapeIssues.Generic;
		}

		if (method.IsAsync)
		{
			issues |= LuaFunctionShapeIssues.Async;
		}

		issues |= InspectParameters(compilation, method, luaState, luaMarshallerAttribute, luaMarshallerContract,
			luaOptional, out bool passesState, out EquatableArray<LuaArgumentModel> arguments);
		issues |= InspectReturn(compilation, method, luaMarshallerAttribute, luaMarshallerContract, luaOptional,
			out LuaValueKind? returnKind,
			out LuaCustomMarshallerModel? returnMarshaller);

		signature = new LuaFunctionSignature(passesState, arguments, returnKind, returnMarshaller);
		return issues;
	}

	private static LuaFunctionShapeIssues InspectParameters(Compilation? compilation, IMethodSymbol method,
		INamedTypeSymbol? luaState,
		INamedTypeSymbol? luaMarshallerAttribute, INamedTypeSymbol? luaMarshallerContract,
		INamedTypeSymbol? luaOptional,
		out bool passesState,
		out EquatableArray<LuaArgumentModel> arguments)
	{
		LuaFunctionShapeIssues issues = LuaFunctionShapeIssues.None;
		bool sawOptional = false;
		passesState = false;
		ImmutableArray<LuaArgumentModel>.Builder builder =
			ImmutableArray.CreateBuilder<LuaArgumentModel>(method.Parameters.Length);
		for (int i = 0; i < method.Parameters.Length; i++)
		{
			IParameterSymbol parameter = method.Parameters[i];
			issues |= InspectModifiers(parameter);
			if (LuaValueKindMapper.IsLuaState(parameter.Type, luaState))
			{
				if (i == 0)
				{
					passesState = true;
				}
				else
				{
					issues |= LuaFunctionShapeIssues.StateParameterNotFirst;
				}

				continue;
			}

			LuaOptionalUse optional = LuaValueKindMapper.ClassifyOptional(parameter.Type, luaOptional,
				out LuaValueKind optionalKind);
			if (optional != LuaOptionalUse.NotOptional)
			{
				sawOptional = true;
				issues |= optional switch
				{
					LuaOptionalUse.LookAlike => LuaFunctionShapeIssues.LookAlikeContractType,
					LuaOptionalUse.Supported when !HasMarshallerAttribute(parameter, luaMarshallerAttribute) =>
						LuaFunctionShapeIssues.None,
					_ => LuaFunctionShapeIssues.OptionalNotSupportedHere
				};
				builder.Add(LuaArgumentModel.Optional(Identifiers.Escape(parameter.Name), optionalKind));
				continue;
			}

			if (sawOptional)
			{
				issues |= LuaFunctionShapeIssues.OptionalArgumentNotTrailing;
			}

			issues |= AddRequiredArgument(compilation, method, parameter, luaMarshallerAttribute,
				luaMarshallerContract, builder);
		}

		arguments = new EquatableArray<LuaArgumentModel>(builder.ToImmutable());
		return issues;
	}

	// By-reference, params and C# default values are refused on every parameter, optional or not.
	private static LuaFunctionShapeIssues InspectModifiers(IParameterSymbol parameter)
	{
		LuaFunctionShapeIssues issues = LuaFunctionShapeIssues.None;
		if (parameter.RefKind != RefKind.None)
		{
			issues |= LuaFunctionShapeIssues.ByRefParameter;
		}

		if (parameter.IsParams)
		{
			issues |= LuaFunctionShapeIssues.ParamsParameter;
		}

		if (parameter.IsOptional || parameter.HasExplicitDefaultValue)
		{
			issues |= LuaFunctionShapeIssues.OptionalParameter;
		}

		return issues;
	}

	// A required argument: an explicitly marshalled value or a built-in kind.
	private static LuaFunctionShapeIssues AddRequiredArgument(Compilation? compilation, IMethodSymbol method,
		IParameterSymbol parameter, INamedTypeSymbol? luaMarshallerAttribute,
		INamedTypeSymbol? luaMarshallerContract, ImmutableArray<LuaArgumentModel>.Builder builder)
	{
		if (!LuaMarshallerResolver.TryResolve(compilation, method.ContainingType, parameter.Type,
			    parameter.GetAttributes(), luaMarshallerAttribute, luaMarshallerContract,
			    out LuaCustomMarshallerModel? customMarshaller, out _))
		{
			return LuaFunctionShapeIssues.UnsupportedParameterType;
		}

		if (customMarshaller is not null)
		{
			builder.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), LuaValueKind.Int32,
				false, CustomMarshaller: customMarshaller));
			return LuaFunctionShapeIssues.None;
		}

		if (!LuaValueKindMapper.TryMap(parameter.Type, out LuaValueKind kind, out bool isNullable))
		{
			return LuaFunctionShapeIssues.UnsupportedParameterType;
		}

		builder.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), kind, isNullable));
		return LuaFunctionShapeIssues.None;
	}

	private static LuaFunctionShapeIssues InspectReturn(Compilation? compilation, IMethodSymbol method,
		INamedTypeSymbol? luaMarshallerAttribute,
		INamedTypeSymbol? luaMarshallerContract, INamedTypeSymbol? luaOptional, out LuaValueKind? returnKind,
		out LuaCustomMarshallerModel? returnMarshaller)
	{
		returnKind = null;
		returnMarshaller = null;
		if (method.ReturnsVoid)
		{
			return LuaFunctionShapeIssues.None;
		}

		switch (LuaValueKindMapper.ClassifyOptional(method.ReturnType, luaOptional, out _))
		{
			case LuaOptionalUse.LookAlike:
				return LuaFunctionShapeIssues.LookAlikeContractType;
			case LuaOptionalUse.Supported:
			case LuaOptionalUse.Unsupported:
				// A thunk returns one value or none: an optional return would be two contracts in one declaration.
				return LuaFunctionShapeIssues.OptionalNotSupportedHere;
		}

		if (method is not { ReturnsByRef: false, ReturnsByRefReadonly: false })
		{
			return LuaFunctionShapeIssues.UnsupportedReturnType;
		}

		if (!LuaMarshallerResolver.TryResolve(compilation, method.ContainingType, method.ReturnType,
			    method.GetReturnTypeAttributes(), luaMarshallerAttribute, luaMarshallerContract,
			    out returnMarshaller, out _)
		    || (returnMarshaller is not null && method.ReturnType.IsRefLikeType))
		{
			return LuaFunctionShapeIssues.UnsupportedReturnType;
		}

		if (returnMarshaller is not null)
		{
			return LuaFunctionShapeIssues.None;
		}

		if (!LuaValueKindMapper.TryMap(method.ReturnType, out LuaValueKind kind, out _))
		{
			return LuaFunctionShapeIssues.UnsupportedReturnType;
		}

		returnKind = kind;
		return LuaFunctionShapeIssues.None;
	}

	// Whether one of the parameter's attributes is the resolved SDK [LuaMarshaller].
	private static bool HasMarshallerAttribute(IParameterSymbol parameter, INamedTypeSymbol? luaMarshallerAttribute)
	{
		if (luaMarshallerAttribute is null)
		{
			return false;
		}

		foreach (AttributeData attribute in parameter.GetAttributes())
		{
			if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, luaMarshallerAttribute))
			{
				return true;
			}
		}

		return false;
	}
}
