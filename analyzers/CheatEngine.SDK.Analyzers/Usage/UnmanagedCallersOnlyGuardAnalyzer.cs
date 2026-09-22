using System.Collections.Immutable;

using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.WellKnown;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CheatEngine.SDK.Analyzers.Usage;

/// <summary>
///     CESDK1004: a method or local function marked <c>[UnmanagedCallersOnly]</c> whose body is not entirely guarded by
///     a catch-all. Native code (Cheat Engine, Lua) calls such a method directly; a managed exception that unwinds out
///     of it terminates the process. <see cref="ExceptionGuard" /> holds the exact definition of "guarded".
/// </summary>
/// <remarks>
///     Stateless and safe for concurrent execution. Registers nothing unless
///     <c>System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute</c>, <c>System.Exception</c> and at least one of
///     the CheatEngine.SDK contract types resolve, so a project that does not reference CheatEngine.SDK pays one lookup
///     per compilation.
///     <c>DoesNotReturnAttribute</c> and <c>System.Environment</c> are resolved too but are optional.
///     Works on <c>IOperation</c>: method bodies and local functions, block or expression bodied. Generated code is
///     not analysed (the SDK's generators emit the guard themselves).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnmanagedCallersOnlyGuardAnalyzer : DiagnosticAnalyzer
{
	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
	{
		get;
	} =
	[
		DiagnosticDescriptors.UnguardedUnmanagedCallersOnly
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
		Compilation compilation = context.Compilation;
		bool referencesCheatEngineSdk =
			compilation.GetTypeByMetadataName(WellKnownTypeNames.CheatEnginePluginAttribute) is not null
			|| compilation.GetTypeByMetadataName(WellKnownTypeNames.CheatEnginePluginBase) is not null;
		if (!referencesCheatEngineSdk)
		{
			return;
		}

		INamedTypeSymbol? unmanagedCallersOnly =
			compilation.GetTypeByMetadataName(WellKnownTypeNames.UnmanagedCallersOnlyAttribute);
		INamedTypeSymbol? exceptionType = compilation.GetTypeByMetadataName(WellKnownTypeNames.Exception);
		if (unmanagedCallersOnly is null || exceptionType is null)
		{
			return;
		}

		// Optional: without them the guard still works, it only stops recognising [DoesNotReturn] calls as throws.
		ExceptionGuard guard = new(
			exceptionType,
			compilation.GetTypeByMetadataName(WellKnownTypeNames.DoesNotReturnAttribute),
			compilation.GetTypeByMetadataName(WellKnownTypeNames.Environment));

		context.RegisterOperationAction(
			operationContext => AnalyzeMethodBody(operationContext, unmanagedCallersOnly, guard),
			OperationKind.MethodBody);
		context.RegisterOperationAction(
			operationContext => AnalyzeLocalFunction(operationContext, unmanagedCallersOnly, guard),
			OperationKind.LocalFunction);
	}

	private static void AnalyzeMethodBody(OperationAnalysisContext context, INamedTypeSymbol unmanagedCallersOnly,
		ExceptionGuard guard)
	{
		IMethodBodyOperation body = (IMethodBodyOperation) context.Operation;
		if (context.ContainingSymbol is not IMethodSymbol method ||
		    !IsUnmanagedCallersOnly(method, unmanagedCallersOnly))
		{
			return;
		}

		if (IsUnguarded(body.BlockBody, guard) || IsUnguarded(body.ExpressionBody, guard))
		{
			Location location = body.Syntax is MethodDeclarationSyntax declaration
				? declaration.Identifier.GetLocation()
				: FirstLocation(method);
			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnguardedUnmanagedCallersOnly, location,
				method.Name));
		}
	}

	private static void AnalyzeLocalFunction(OperationAnalysisContext context, INamedTypeSymbol unmanagedCallersOnly,
		ExceptionGuard guard)
	{
		ILocalFunctionOperation localFunction = (ILocalFunctionOperation) context.Operation;
		if (!IsUnmanagedCallersOnly(localFunction.Symbol, unmanagedCallersOnly))
		{
			return;
		}

		if (IsUnguarded(localFunction.Body, guard))
		{
			Location location = localFunction.Syntax is LocalFunctionStatementSyntax declaration
				? declaration.Identifier.GetLocation()
				: FirstLocation(localFunction.Symbol);
			context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnguardedUnmanagedCallersOnly, location,
				localFunction.Symbol.Name));
		}
	}

	private static bool IsUnguarded(IBlockOperation? body, ExceptionGuard guard)
	{
		return body is not null && !guard.IsGuarded(body);
	}

	private static bool IsUnmanagedCallersOnly(IMethodSymbol method, INamedTypeSymbol unmanagedCallersOnly)
	{
		foreach (AttributeData attribute in method.GetAttributes())
		{
			if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, unmanagedCallersOnly))
			{
				return true;
			}
		}

		return false;
	}

	private static Location FirstLocation(IMethodSymbol method)
	{
		return method.Locations.IsEmpty ? Location.None : method.Locations[0];
	}
}
