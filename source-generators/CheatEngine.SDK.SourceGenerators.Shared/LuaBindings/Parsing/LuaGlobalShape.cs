using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Decides whether a <c>[LuaGlobal]</c> method can receive a generated body and classifies its signature into one
///     of the call forms. Symbols in, flags and a value-only signature out: the CESDK2xxx analyzer links this file
///     (with <see cref="LuaValueKindMapper" />, <see cref="ContainingTypeShape" /> and <c>LuaEmit/LuaNames.cs</c>) so
///     that a method the generator skips is exactly a method the analyzer reports.
/// </summary>
/// <remarks>
///     <para>
///         The rules: an ordinary, static, non-generic, non-async method that is the defining declaration of a partial
///         method without an implementing part. Parameters, in order: an optional leading <c>LuaState</c> (the body then
///         uses it instead of acquiring a state), then the arguments (by value, of a built-in marshalled kind or an
///         explicitly selected valid <c>[LuaMarshaller]</c>, none with a C# default value or <see langword="params" />;
///         a trailing run of them may be <c>LuaOptional&lt;T&gt;</c>), then the results: <see langword="out" />
///         parameters of the same conversion contract other than <c>ReadOnlySpan&lt;byte&gt;</c>, or a copy-out pair
///         <c>Span&lt;byte&gt; destination, out int written</c>; after them, <see langword="out" />
///         <c>LuaOptional&lt;T&gt;</c> results; last, on the Outcome form only, one variadic pair
///         <c>Span&lt;T&gt; values, out int count</c>.
///     </para>
///     <para>
///         A <c>LuaOperationStatus</c> return always makes the method a non-throwing Outcome form, whether or not it
///         has <see langword="out" /> results. Otherwise, any <see langword="out" /> result makes the method a
///         non-throwing Try form, which returns <see langword="bool" />. No result makes it the throwing form, whose
///         return type is <see langword="void" />
///         or a value of that conversion contract other than <c>ReadOnlySpan&lt;byte&gt;</c> (a <see langword="bool" />
///         return without
///         results is therefore a throwing wrapper that reads a Lua boolean). A Try form without a result cannot be
///         written.
///     </para>
///     <para>
///         <c>LuaOptional&lt;T&gt;</c> and <c>LuaOperationStatus</c> are recognised by symbol identity (the types
///         <c>CheatEngine.SDK.Lua</c> defines, <see cref="LuaContractTypes" />). A same-named type from the consumer's
///         source or another assembly is a look-alike: flagged, never taken as the contract.
///     </para>
/// </remarks>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaGlobalShape
{
	/// <summary>Inspects <paramref name="method" /> against resolved SDK Lua binding contracts.</summary>
	/// <param name="compilation">The consumer compilation that must be able to call a custom marshaller directly.</param>
	/// <param name="method">The attributed method.</param>
	/// <param name="luaState">The real Lua runtime state symbol, or <see langword="null" /> when it is unavailable.</param>
	/// <param name="luaMarshallerAttribute">The real SDK marshaller annotation, or <see langword="null" />.</param>
	/// <param name="luaMarshallerContract">The real SDK static marshaller contract, or <see langword="null" />.</param>
	/// <param name="luaOptional">The real SDK <c>LuaOptional&lt;T&gt;</c>, or <see langword="null" />.</param>
	/// <param name="luaOperationStatus">The real SDK <c>LuaOperationStatus</c>, or <see langword="null" />.</param>
	/// <param name="signature">
	///     What could be classified; complete only when the result is
	///     <see cref="LuaGlobalShapeIssues.None" />.
	/// </param>
	public static LuaGlobalShapeIssues Inspect(Compilation compilation, IMethodSymbol method,
		INamedTypeSymbol? luaState, INamedTypeSymbol? luaMarshallerAttribute,
		INamedTypeSymbol? luaMarshallerContract, INamedTypeSymbol? luaOptional,
		INamedTypeSymbol? luaOperationStatus, out LuaGlobalSignature signature)
	{
		return InspectCore(compilation, method,
			new Contracts(luaState, luaMarshallerAttribute, luaMarshallerContract, luaOptional, luaOperationStatus),
			out signature);
	}

	/// <summary>
	///     Inspects <paramref name="method" />, resolving <c>LuaOptional&lt;T&gt;</c> and <c>LuaOperationStatus</c> from
	///     <c>CheatEngine.SDK.Lua</c> in <paramref name="compilation" />.
	/// </summary>
	/// <param name="compilation">The consumer compilation that must be able to call a custom marshaller directly.</param>
	/// <param name="method">The attributed method.</param>
	/// <param name="luaState">The real Lua runtime state symbol, or <see langword="null" /> when it is unavailable.</param>
	/// <param name="luaMarshallerAttribute">The real SDK marshaller annotation, or <see langword="null" />.</param>
	/// <param name="luaMarshallerContract">The real SDK static marshaller contract, or <see langword="null" />.</param>
	/// <param name="signature">
	///     What could be classified; complete only when the result is
	///     <see cref="LuaGlobalShapeIssues.None" />.
	/// </param>
	public static LuaGlobalShapeIssues Inspect(Compilation compilation, IMethodSymbol method,
		INamedTypeSymbol? luaState,
		INamedTypeSymbol? luaMarshallerAttribute, INamedTypeSymbol? luaMarshallerContract,
		out LuaGlobalSignature signature)
	{
		return Inspect(compilation, method, luaState, luaMarshallerAttribute, luaMarshallerContract,
			LuaContractTypes.Resolve(compilation, LuaContractTypes.LuaOptionalMetadataName),
			LuaContractTypes.Resolve(compilation, LuaContractTypes.LuaOperationStatusMetadataName), out signature);
	}

	/// <summary>
	///     Compatibility overload for validation that accepts only built-in scalar marshallers and has no compilation:
	///     <c>LuaOptional&lt;T&gt;</c> and <c>LuaOperationStatus</c> cannot be resolved, so every use of them is reported as
	///     a look-alike. The LuaBindings generator and its analyzer pass the resolved contracts.
	/// </summary>
	public static LuaGlobalShapeIssues Inspect(IMethodSymbol method, INamedTypeSymbol? luaState,
		out LuaGlobalSignature signature)
	{
		return InspectCore(null, method, new Contracts(luaState, null, null, null, null), out signature);
	}

	private static LuaGlobalShapeIssues InspectCore(Compilation? compilation, IMethodSymbol method,
		Contracts contracts, out LuaGlobalSignature signature)
	{
		LuaGlobalShapeIssues issues = InspectMethod(method);

		ParameterWalk walk = new(compilation, method.ContainingType, contracts);
		issues |= walk.Run(method.Parameters);

		EquatableArray<LuaResultModel> results = new(walk.Results.ToImmutable());
		LuaCallForm form;
		if (LuaContractTypes.Is(method.ReturnType, contracts.LuaOperationStatus))
		{
			form = LuaCallForm.Outcome;
		}
		else
		{
			form = results.IsEmpty ? LuaCallForm.Throwing : LuaCallForm.Try;
		}

		if (walk.HasVariadicResult && form != LuaCallForm.Outcome)
		{
			issues |= LuaGlobalShapeIssues.VariadicResultOutsideOutcome;
		}

		issues |= InspectReturn(compilation, method, form, contracts, out LuaValueKind? returnKind,
			out bool returnIsNullable, out LuaCustomMarshallerModel? returnMarshaller);

		signature = new LuaGlobalSignature(
			walk.StateParameterName,
			new EquatableArray<LuaArgumentModel>(walk.Arguments.ToImmutable()),
			form,
			results,
			returnKind,
			returnIsNullable,
			returnMarshaller);
		return issues;
	}

	private static LuaGlobalShapeIssues InspectMethod(IMethodSymbol method)
	{
		LuaGlobalShapeIssues issues = LuaGlobalShapeIssues.None;
		if (method.MethodKind != MethodKind.Ordinary)
		{
			issues |= LuaGlobalShapeIssues.NotOrdinaryMethod;
		}

		if (!method.IsStatic)
		{
			issues |= LuaGlobalShapeIssues.NotStatic;
		}

		if (!method.IsPartialDefinition)
		{
			issues |= LuaGlobalShapeIssues.NotPartialDefinition;
		}
		else if (method.PartialImplementationPart is not null)
		{
			issues |= LuaGlobalShapeIssues.AlreadyImplemented;
		}

		if (method.IsGenericMethod)
		{
			issues |= LuaGlobalShapeIssues.Generic;
		}

		if (method.IsAsync)
		{
			issues |= LuaGlobalShapeIssues.Async;
		}

		return issues;
	}

	private static LuaGlobalShapeIssues InspectReturn(Compilation? compilation, IMethodSymbol method,
		LuaCallForm form, Contracts contracts, out LuaValueKind? returnKind, out bool returnIsNullable,
		out LuaCustomMarshallerModel? returnMarshaller)
	{
		returnKind = null;
		returnIsNullable = false;
		returnMarshaller = null;
		bool byRef = method.ReturnsByRef || method.ReturnsByRefReadonly;

		if (form == LuaCallForm.Outcome)
		{
			return !byRef ? LuaGlobalShapeIssues.None : LuaGlobalShapeIssues.TryFormReturnNotBool;
		}

		if (LuaContractTypes.IsLookAlike(method.ReturnType, contracts.LuaOperationStatus,
			    LuaContractTypes.LuaOperationStatusMetadataName))
		{
			return LuaGlobalShapeIssues.LookAlikeContractType;
		}

		if (form == LuaCallForm.Try)
		{
			return method.ReturnType.SpecialType == SpecialType.System_Boolean && !byRef
				? LuaGlobalShapeIssues.None
				: LuaGlobalShapeIssues.TryFormReturnNotBool;
		}

		return method.ReturnsVoid
			? LuaGlobalShapeIssues.None
			: InspectThrowingReturn(compilation, method, contracts, byRef, out returnKind, out returnIsNullable,
				out returnMarshaller);
	}

	// The throwing form's single Lua result, read into the return value.
	private static LuaGlobalShapeIssues InspectThrowingReturn(Compilation? compilation, IMethodSymbol method,
		Contracts contracts, bool byRef, out LuaValueKind? returnKind, out bool returnIsNullable,
		out LuaCustomMarshallerModel? returnMarshaller)
	{
		returnKind = null;
		returnIsNullable = false;
		returnMarshaller = null;
		switch (LuaValueKindMapper.ClassifyOptional(method.ReturnType, contracts.LuaOptional, out _))
		{
			case LuaOptionalUse.LookAlike:
				return LuaGlobalShapeIssues.LookAlikeContractType;
			case LuaOptionalUse.Supported:
			case LuaOptionalUse.Unsupported:
				return LuaGlobalShapeIssues.OptionalNotSupportedHere;
		}

		if (byRef)
		{
			return LuaGlobalShapeIssues.UnsupportedReturnType;
		}

		if (LuaValueKindMapper.IsReadOnlySpanOfByte(method.ReturnType))
		{
			return LuaGlobalShapeIssues.SpanResult;
		}

		if (!LuaMarshallerResolver.TryResolve(compilation, method.ContainingType, method.ReturnType,
			    method.GetReturnTypeAttributes(), contracts.LuaMarshallerAttribute, contracts.LuaMarshallerContract,
			    out returnMarshaller, out _)
		    || (returnMarshaller is not null && method.ReturnType.IsRefLikeType))
		{
			return LuaGlobalShapeIssues.UnsupportedReturnType;
		}

		if (returnMarshaller is not null)
		{
			return LuaGlobalShapeIssues.None;
		}

		if (!LuaValueKindMapper.TryMap(method.ReturnType, out LuaValueKind kind, out returnIsNullable) ||
		    !LuaValueKinds.CanBeResult(kind))
		{
			return LuaGlobalShapeIssues.UnsupportedReturnType;
		}

		returnKind = kind;
		return LuaGlobalShapeIssues.None;
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

	// The resolved SDK symbols a signature is checked against.
	private sealed class Contracts(
		INamedTypeSymbol? luaState,
		INamedTypeSymbol? luaMarshallerAttribute,
		INamedTypeSymbol? luaMarshallerContract,
		INamedTypeSymbol? luaOptional,
		INamedTypeSymbol? luaOperationStatus)
	{
		public INamedTypeSymbol? LuaState
		{
			get;
		} = luaState;

		public INamedTypeSymbol? LuaMarshallerAttribute
		{
			get;
		} = luaMarshallerAttribute;

		public INamedTypeSymbol? LuaMarshallerContract
		{
			get;
		} = luaMarshallerContract;

		public INamedTypeSymbol? LuaOptional
		{
			get;
		} = luaOptional;

		public INamedTypeSymbol? LuaOperationStatus
		{
			get;
		} = luaOperationStatus;
	}

	// The parameter list, left to right: the leading state, the arguments (optional ones last), then the results
	// (required, optional, variadic).
	private sealed class ParameterWalk(Compilation? compilation, INamedTypeSymbol bindingType, Contracts contracts)
	{
		private bool _inResults;
		private bool _sawOptionalArgument;
		private bool _sawOptionalResult;

		public string StateParameterName
		{
			get;
			private set;
		} = string.Empty;

		public bool HasVariadicResult
		{
			get;
			private set;
		}

		public ImmutableArray<LuaArgumentModel>.Builder Arguments
		{
			get;
		} =
			ImmutableArray.CreateBuilder<LuaArgumentModel>();

		public ImmutableArray<LuaResultModel>.Builder Results
		{
			get;
		} = ImmutableArray.CreateBuilder<LuaResultModel>();

		public LuaGlobalShapeIssues Run(ImmutableArray<IParameterSymbol> parameters)
		{
			LuaGlobalShapeIssues issues = LuaGlobalShapeIssues.None;
			for (int i = 0; i < parameters.Length; i++)
			{
				IParameterSymbol parameter = parameters[i];
				if (parameter.IsParams)
				{
					issues |= LuaGlobalShapeIssues.ParamsParameter;
				}

				if (parameter.IsOptional || parameter.HasExplicitDefaultValue)
				{
					issues |= LuaGlobalShapeIssues.OptionalParameter;
				}

				if (parameter.RefKind == RefKind.Out)
				{
					issues |= AddOutResult(parameter);
				}
				else if (parameter.RefKind != RefKind.None)
				{
					issues |= LuaGlobalShapeIssues.ByRefParameter;
				}
				else if (LuaValueKindMapper.IsSpanOfByte(parameter.Type))
				{
					issues |= AddCopyOutResult(parameters, ref i);
				}
				else if (LuaValueKindMapper.IsSpanOfOther(parameter.Type, out ITypeSymbol? element))
				{
					issues |= AddVariadicResult(parameters, element, ref i);
				}
				else
				{
					issues |= AddArgument(parameter, i);
				}
			}

			return issues;
		}

		// A result after the variadic pair, or a required result after an optional one.
		private LuaGlobalShapeIssues EnterResult(bool isOptional)
		{
			_inResults = true;
			LuaGlobalShapeIssues issues = HasVariadicResult
				? LuaGlobalShapeIssues.VariadicResultNotLast
				: LuaGlobalShapeIssues.None;
			if (!isOptional && _sawOptionalResult)
			{
				issues |= LuaGlobalShapeIssues.OptionalResultNotTrailing;
			}

			return issues;
		}

		private LuaGlobalShapeIssues AddOutResult(IParameterSymbol parameter)
		{
			LuaOptionalUse optional =
				LuaValueKindMapper.ClassifyOptional(parameter.Type, contracts.LuaOptional, out LuaValueKind inner);
			LuaGlobalShapeIssues issues = EnterResult(optional != LuaOptionalUse.NotOptional);
			switch (optional)
			{
				case LuaOptionalUse.LookAlike:
					return issues | LuaGlobalShapeIssues.LookAlikeContractType;
				case LuaOptionalUse.Unsupported:
					return issues | LuaGlobalShapeIssues.OptionalNotSupportedHere;
				case LuaOptionalUse.Supported:
					_sawOptionalResult = true;
					if (HasMarshallerAttribute(parameter, contracts.LuaMarshallerAttribute))
					{
						return issues | LuaGlobalShapeIssues.OptionalNotSupportedHere;
					}

					Results.Add(LuaResultModel.Optional(inner, Identifiers.Escape(parameter.Name)));
					return issues;
			}

			if (LuaValueKindMapper.IsReadOnlySpanOfByte(parameter.Type))
			{
				return issues | LuaGlobalShapeIssues.SpanResult;
			}

			if (!LuaMarshallerResolver.TryResolve(compilation, bindingType, parameter.Type, parameter.GetAttributes(),
				    contracts.LuaMarshallerAttribute, contracts.LuaMarshallerContract,
				    out LuaCustomMarshallerModel? customMarshaller, out _)
			    || (customMarshaller is not null && parameter.Type.IsRefLikeType))
			{
				return issues | LuaGlobalShapeIssues.UnsupportedResultType;
			}

			if (customMarshaller is not null)
			{
				Results.Add(LuaResultModel.Custom(customMarshaller, Identifiers.Escape(parameter.Name)));
				return issues;
			}

			if (!LuaValueKindMapper.TryMap(parameter.Type, out LuaValueKind kind, out bool isNullable) ||
			    !LuaValueKinds.CanBeResult(kind))
			{
				return issues | LuaGlobalShapeIssues.UnsupportedResultType;
			}

			Results.Add(LuaResultModel.Value(kind, Identifiers.Escape(parameter.Name), isNullable));
			return issues;
		}

		// A copy-out result is the destination and the count together: 'Span<byte> destination, out int written'.
		private LuaGlobalShapeIssues AddCopyOutResult(ImmutableArray<IParameterSymbol> parameters, ref int index)
		{
			LuaGlobalShapeIssues issues = EnterResult(false);
			if (index + 1 >= parameters.Length
			    || parameters[index + 1] is
				    not { RefKind: RefKind.Out, Type.SpecialType: SpecialType.System_Int32 } written)
			{
				return issues | LuaGlobalShapeIssues.UnsupportedResultType;
			}

			bool destinationIsScoped = parameters[index].ScopedKind != ScopedKind.None;
			Results.Add(LuaResultModel.CopyOut(Identifiers.Escape(parameters[index].Name),
				Identifiers.Escape(written.Name), destinationIsScoped));
			index++;
			return issues;
		}

		// The variadic tail is the span and the count together: 'Span<T> values, out int count'.
		private LuaGlobalShapeIssues AddVariadicResult(ImmutableArray<IParameterSymbol> parameters,
			ITypeSymbol element, ref int index)
		{
			LuaGlobalShapeIssues issues = EnterResult(true);
			if (HasVariadicResult)
			{
				issues |= LuaGlobalShapeIssues.MultipleVariadicResults;
			}

			HasVariadicResult = true;
			if (index + 1 >= parameters.Length
			    || parameters[index + 1] is
				    not { RefKind: RefKind.Out, Type.SpecialType: SpecialType.System_Int32 } count)
			{
				return issues | LuaGlobalShapeIssues.UnsupportedResultType;
			}

			IParameterSymbol values = parameters[index];
			index++;
			if (!LuaValueKindMapper.TryMap(element, out LuaValueKind kind, out _)
			    || !LuaValueKinds.CanBeVariadicElement(kind))
			{
				return issues | LuaGlobalShapeIssues.UnsupportedVariadicElement;
			}

			Results.Add(LuaResultModel.Variadic(kind, Identifiers.Escape(values.Name), Identifiers.Escape(count.Name),
				values.ScopedKind != ScopedKind.None));
			return issues;
		}

		// A by-value parameter: an argument, or the leading state.
		private LuaGlobalShapeIssues AddArgument(IParameterSymbol parameter, int index)
		{
			LuaGlobalShapeIssues issues =
				_inResults ? LuaGlobalShapeIssues.ResultBeforeArgument : LuaGlobalShapeIssues.None;
			if (LuaValueKindMapper.IsLuaState(parameter.Type, contracts.LuaState))
			{
				if (index != 0)
				{
					return issues | LuaGlobalShapeIssues.StateParameterNotFirst;
				}

				StateParameterName = Identifiers.Escape(parameter.Name);
				return issues;
			}

			switch (LuaValueKindMapper.ClassifyOptional(parameter.Type, contracts.LuaOptional, out LuaValueKind inner))
			{
				case LuaOptionalUse.LookAlike:
					return issues | LuaGlobalShapeIssues.LookAlikeContractType;
				case LuaOptionalUse.Unsupported:
					_sawOptionalArgument = true;
					return issues | LuaGlobalShapeIssues.OptionalNotSupportedHere;
				case LuaOptionalUse.Supported:
					_sawOptionalArgument = true;
					if (HasMarshallerAttribute(parameter, contracts.LuaMarshallerAttribute))
					{
						return issues | LuaGlobalShapeIssues.OptionalNotSupportedHere;
					}

					Arguments.Add(LuaArgumentModel.Optional(Identifiers.Escape(parameter.Name), inner));
					return issues;
			}

			if (_sawOptionalArgument)
			{
				issues |= LuaGlobalShapeIssues.OptionalArgumentNotTrailing;
			}

			if (!LuaMarshallerResolver.TryResolve(compilation, bindingType, parameter.Type, parameter.GetAttributes(),
				    contracts.LuaMarshallerAttribute, contracts.LuaMarshallerContract,
				    out LuaCustomMarshallerModel? customMarshaller, out _))
			{
				return issues | LuaGlobalShapeIssues.UnsupportedParameterType;
			}

			if (customMarshaller is not null)
			{
				bool customIsScoped = parameter.ScopedKind != ScopedKind.None;
				Arguments.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), LuaValueKind.Int32,
					false, customIsScoped, CustomMarshaller: customMarshaller));
				return issues;
			}

			if (!LuaValueKindMapper.TryMap(parameter.Type, out LuaValueKind kind, out bool isNullable))
			{
				return issues | LuaGlobalShapeIssues.UnsupportedParameterType;
			}

			bool isScoped = parameter.ScopedKind != ScopedKind.None;
			Arguments.Add(new LuaArgumentModel(Identifiers.Escape(parameter.Name), kind, isNullable, isScoped));
			return issues;
		}
	}
}
