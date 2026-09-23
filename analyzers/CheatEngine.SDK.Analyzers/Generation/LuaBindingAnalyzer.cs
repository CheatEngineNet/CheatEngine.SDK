using System.Collections.Immutable;

using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.WellKnown;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Generation;

/// <summary>
///     The generator-input rules for <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>. CESDK2001: a
///     <c>[LuaFunction]</c> or
///     <c>[LuaGlobal]</c> member exists but the compilation does not allow unsafe code. CESDK2002: the type that
///     declares such a member cannot receive a generated part. CESDK2003: a <c>[LuaFunction]</c> method cannot be
///     exported by a generated thunk. CESDK2004: a <c>[LuaGlobal]</c> method cannot receive a generated body.
///     CESDK2005: two otherwise valid functions of one binding type export the same Lua name. CESDK2010 to CESDK2013:
///     an optional argument outside the trailing run, an invalid optional or variadic result shape, a look-alike of an
///     SDK Lua contract type, and <c>LuaOptional&lt;T&gt;</c> where it is not supported (including on
///     <c>[LuaMethod]</c> and <c>[LuaProperty]</c> members, which do not support it yet).
/// </summary>
/// <remarks>
///     <para>
///         These are exactly the cases in which the LuaBindings generator stays silent (or, for CESDK2001, silent for
///         every
///         binding of the compilation regardless of shape): the generator never reports, this analyzer does, by linking
///         the
///         generator's own shape-validation source (<c>Parsing/LuaFunctionShape.cs</c>, <c>Parsing/LuaGlobalShape.cs</c>,
///         <c>Parsing/ContainingTypeShape.cs</c>) instead of a separately hand-written copy.
///     </para>
///     <para>
///         Stateless and safe for concurrent execution. Everything that lives as long as a compilation is created in the
///         compilation-start action. When neither <c>CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute</c> nor
///         <c>CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute</c> can be resolved, nothing is registered. CESDK2001,
///         CESDK2002
///         and CESDK2004 are reported from a symbol action and show up while typing. CESDK2005 is the one exception:
///         duplicate exported names need every valid sibling member of a containing type, so a second compilation-end
///         registration (<see cref="LuaFunctionDuplicateState" />) decides it. Its own descriptor carries the
///         <c>CompilationEnd</c> tag; local shape rules remain IDE-live. Generated code is neither analysed nor counted.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LuaBindingAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
	{
		get;
	} =
	[
		DiagnosticDescriptors.UnsafeBlocksRequired,
		DiagnosticDescriptors.InvalidLuaBindingContainingType,
		DiagnosticDescriptors.InvalidLuaFunction,
		DiagnosticDescriptors.InvalidLuaGlobal,
		DiagnosticDescriptors.DuplicateLuaName,
		DiagnosticDescriptors.NonTrailingOptionalLuaArgument,
		DiagnosticDescriptors.InvalidOptionalOrVariadicLuaResult,
		DiagnosticDescriptors.LookAlikeLuaContractType,
		DiagnosticDescriptors.UnsupportedLuaOptionalPosition
	];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		INamedTypeSymbol? luaFunctionAttribute =
			SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaFunctionAttribute);
		INamedTypeSymbol? luaGlobalAttribute =
			SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaGlobalAttribute);
		if (luaFunctionAttribute is null && luaGlobalAttribute is null)
		{
			return;
		}

		LuaBindingContractSymbols symbols = new(
			luaFunctionAttribute,
			luaGlobalAttribute,
			SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaMarshallerAttribute),
			SdkSymbolResolver.Lua(context.Compilation, WellKnownTypeNames.ILuaMarshaller),
			SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaClassAttribute),
			SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaMethodAttribute),
			SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaPropertyAttribute),
			SdkSymbolResolver.Lua(context.Compilation, WellKnownTypeNames.LuaState),
			SdkSymbolResolver.Lua(context.Compilation, WellKnownTypeNames.LuaOptional),
			SdkSymbolResolver.Lua(context.Compilation, WellKnownTypeNames.LuaOperationStatus));

		// Mirrors CheatEngine.SDK.SourceGenerators.LuaBindings.Model.CompilationFacts.From: a non-C# compilation (never seen
		// here, the analyzer is C#-only) would read as "unsafe not allowed" too.
		bool allowsUnsafe = context.Compilation.Options is CSharpCompilationOptions { AllowUnsafe: true };

		LuaFunctionDuplicateState duplicateNames = new();
		context.RegisterSymbolAction(
			symbolContext => AnalyzeMethod(symbolContext, symbols, allowsUnsafe, duplicateNames), SymbolKind.Method);
		if (symbols.LuaMethodAttribute is not null || symbols.LuaPropertyAttribute is not null)
		{
			context.RegisterSymbolAction(symbolContext => AnalyzeObjectMember(symbolContext, symbols),
				SymbolKind.Method, SymbolKind.Property);
		}

		context.RegisterCompilationEndAction(duplicateNames.Report);
	}

	// [LuaMethod] and [LuaProperty] members do not support LuaOptional<T> yet: CESDK2013 names the reason instead of the
	// generic CESDK2006 "unsupported type" (the object-binding analyzer leaves such a position to this rule).
	private static void AnalyzeObjectMember(SymbolAnalysisContext context, LuaBindingContractSymbols symbols)
	{
		if (symbols.LuaOptional is null)
		{
			return;
		}

		bool usesOptional;
		switch (context.Symbol)
		{
			case IMethodSymbol method when symbols.LuaMethodAttribute is not null
										   && FindAttribute(method, symbols.LuaMethodAttribute) is not null:
				usesOptional = LuaContractTypes.Is(method.ReturnType, symbols.LuaOptional);
				foreach (IParameterSymbol parameter in method.Parameters)
				{
					usesOptional |= LuaContractTypes.Is(parameter.Type, symbols.LuaOptional);
				}

				break;
			case IPropertySymbol property when symbols.LuaPropertyAttribute is not null
											   && FindAttribute(property, symbols.LuaPropertyAttribute) is not null:
				usesOptional = LuaContractTypes.Is(property.Type, symbols.LuaOptional);
				break;
			default:
				return;
		}

		if (usesOptional)
		{
			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnsupportedLuaOptionalPosition,
				FirstLocation(context.Symbol), context.Symbol.Name,
				"must not use LuaOptional<T>: [LuaMethod] and [LuaProperty] members do not support optional values yet"));
		}
	}

	private static void AnalyzeMethod(SymbolAnalysisContext context, LuaBindingContractSymbols symbols,
		bool allowsUnsafe, LuaFunctionDuplicateState duplicateNames)
	{
		IMethodSymbol method = (IMethodSymbol) context.Symbol;
		AttributeData? luaFunction = symbols.LuaFunctionAttribute is null
			? null
			: FindAttribute(method, symbols.LuaFunctionAttribute);
		AttributeData? luaGlobal = symbols.LuaGlobalAttribute is null
			? null
			: FindAttribute(method, symbols.LuaGlobalAttribute);
		if (luaFunction is null && luaGlobal is null)
		{
			return;
		}

		string name = method.Name;
		Location location = FirstLocation(method);

		if (!allowsUnsafe && luaFunction is not null)
		{
			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnsafeBlocksRequired, location, name));
		}

		ContainingTypeIssues typeIssues = ContainingTypeShape.Inspect(method.ContainingType, context.CancellationToken);
		foreach (ContainingTypeIssues problem in ContainingTypeProblemText.ReportOrder)
		{
			if ((typeIssues & problem) == ContainingTypeIssues.None)
			{
				continue;
			}

			context.ReportDiagnostic(Diagnostic.Create(
				DiagnosticDescriptors.InvalidLuaBindingContainingType, location, name,
				ContainingTypeProblemText.Describe(problem)));
		}

		if (luaFunction is not null)
		{
			AnalyzeLuaFunction(context, method, luaFunction, location, typeIssues, duplicateNames, symbols);
		}

		if (luaGlobal is not null)
		{
			AnalyzeLuaGlobal(context, method, luaGlobal, location, symbols);
		}
	}

	private static void AnalyzeLuaFunction(
		SymbolAnalysisContext context,
		IMethodSymbol method,
		AttributeData attribute,
		Location location,
		ContainingTypeIssues typeIssues,
		LuaFunctionDuplicateState duplicateNames,
		LuaBindingContractSymbols symbols)
	{
		string? name = ReadName(attribute);
		LuaFunctionShapeIssues issues = LuaFunctionShape.Inspect(context.Compilation, method, symbols.LuaState,
			symbols.LuaMarshallerAttribute,
			symbols.LuaMarshallerContract, symbols.LuaOptional, out _);
		if (!LuaNames.IsValidName(name))
		{
			issues |= LuaFunctionShapeIssues.InvalidName;
		}

		foreach (LuaFunctionShapeIssues problem in LuaFunctionProblemText.ReportOrder)
		{
			if ((issues & problem) == LuaFunctionShapeIssues.None)
			{
				continue;
			}

			context.ReportDiagnostic(Diagnostic.Create(
				LuaFunctionProblemText.DescriptorFor(problem), location, method.Name,
				LuaFunctionProblemText.Describe(problem)));
		}

		// A duplicate-name verdict needs every sibling member of the containing type, not just this one method:
		// only a method with no other problem is a candidate, exactly the generator's own grouping input
		// (LuaFunctionModel.IsValid). The compilation-end action (LuaFunctionDuplicateState.Report) reports it.
		if (issues == LuaFunctionShapeIssues.None && typeIssues == ContainingTypeIssues.None)
		{
			duplicateNames.AddCandidate(method.ContainingType, name!, method.Name, location);
		}
	}

	private static void AnalyzeLuaGlobal(SymbolAnalysisContext context, IMethodSymbol method, AttributeData attribute,
		Location location, LuaBindingContractSymbols symbols)
	{
		LuaGlobalShapeIssues issues = LuaGlobalShape.Inspect(context.Compilation, method, symbols.LuaState,
			symbols.LuaMarshallerAttribute,
			symbols.LuaMarshallerContract, symbols.LuaOptional, symbols.LuaOperationStatus, out _);
		if (!LuaNames.IsValidName(ReadName(attribute)))
		{
			issues |= LuaGlobalShapeIssues.InvalidName;
		}

		foreach (LuaGlobalShapeIssues problem in LuaGlobalProblemText.ReportOrder)
		{
			if ((issues & problem) == LuaGlobalShapeIssues.None)
			{
				continue;
			}

			context.ReportDiagnostic(Diagnostic.Create(
				LuaGlobalProblemText.DescriptorFor(problem), location, method.Name,
				LuaGlobalProblemText.Describe(problem)));
		}
	}

	// The name argument of [LuaFunction(name)]/[LuaGlobal(name)]; null while the author is typing (missing, not a
	// string, or explicitly null), which LuaNames.IsValidName also rejects. The attribute constructor's own
	// ArgumentException never runs at compile time, so an empty string reaches here too.
	private static string? ReadName(AttributeData attribute)
	{
		ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
		return arguments is [{ Kind: TypedConstantKind.Primitive, Value: string name }]
			? name
			: null;
	}

	private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol attributeClass)
	{
		foreach (AttributeData attribute in symbol.GetAttributes())
		{
			if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeClass))
			{
				return attribute;
			}
		}

		return null;
	}

	private static Location FirstLocation(ISymbol symbol)
	{
		return symbol.Locations.IsEmpty ? Location.None : symbol.Locations[0];
	}
}
