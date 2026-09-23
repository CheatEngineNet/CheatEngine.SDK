using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.WellKnown;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CheatEngine.SDK.Analyzers.Plugin;

/// <summary>
///     CESDK0006: a method or local function whose <c>[UnmanagedCallersOnly]</c> attribute exports a constant
///     <c>EntryPoint</c> starting with <c>CEPlugin_</c>, the prefix of the three classic Cheat Engine native plugin
///     exports (<c>CEPlugin_GetVersion</c>, <c>CEPlugin_InitializePlugin</c>, <c>CEPlugin_DisablePlugin</c>).
/// </summary>
/// <remarks>
///     <para>
///         Only a NativeAOT publication of the consumer's own assembly turns such a method into a DLL export
///         (https://learn.microsoft.com/dotnet/core/deploying/native-aot/interop#native-exports); the SDK package itself
///         never adds one. A NativeAOT plugin DLL is not a supported CheatEngine.SDK profile: Cheat Engine unloads plugins
///         with <c>FreeLibrary</c>, which .NET does not support for NativeAOT libraries, and CheatEngine.SDK plugins load
///         through the managed hostfxr profile only (audit F02, ADR-02).
///     </para>
///     <para>
///         Only the <c>CEPlugin_</c> prefix is recognised (ordinal, case-sensitive, like the host's export lookup). The
///         historical unprefixed names the host also tries are deliberately not flagged: they are ordinary words
///         (<c>GetVersion</c>, <c>InitializePlugin</c>) that would produce false positives. Like the other plugin rules
///         it registers nothing in a compilation that does not reference CheatEngine.SDK. Stateless, no I/O, safe for
///         concurrent execution; generated code is not analysed.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClassicNativeExportAnalyzer : DiagnosticAnalyzer
{
	/// <summary>The prefix of every classic native plugin export name.</summary>
	internal const string ClassicExportPrefix = "CEPlugin_";

	private const string EntryPointArgument = "EntryPoint";

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
	{
		get;
	} =
	[
		DiagnosticDescriptors.ClassicNativePluginExport
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
		INamedTypeSymbol? unmanagedCallersOnly =
			compilation.GetTypeByMetadataName(WellKnownTypeNames.UnmanagedCallersOnlyAttribute);
		if (!referencesCheatEngineSdk || unmanagedCallersOnly is null)
		{
			return;
		}

		context.RegisterSymbolAction(
			symbolContext => Analyze(symbolContext.ReportDiagnostic, (IMethodSymbol) symbolContext.Symbol,
				unmanagedCallersOnly, symbolContext.CancellationToken),
			SymbolKind.Method);
		context.RegisterOperationAction(
			operationContext => Analyze(operationContext.ReportDiagnostic,
				((ILocalFunctionOperation) operationContext.Operation).Symbol, unmanagedCallersOnly,
				operationContext.CancellationToken),
			OperationKind.LocalFunction);
	}

	private static void Analyze(Action<Diagnostic> report, IMethodSymbol method, INamedTypeSymbol unmanagedCallersOnly,
		CancellationToken cancellationToken)
	{
		foreach (AttributeData attribute in method.GetAttributes())
		{
			if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, unmanagedCallersOnly))
			{
				continue;
			}

			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (!string.Equals(argument.Key, EntryPointArgument, StringComparison.Ordinal) ||
					argument.Value.Value is not string entryPoint ||
					!entryPoint.StartsWith(ClassicExportPrefix, StringComparison.Ordinal))
				{
					continue;
				}

				Location location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
									?? (method.Locations.IsEmpty ? Location.None : method.Locations[0]);
				report(Diagnostic.Create(DiagnosticDescriptors.ClassicNativePluginExport, location, method.Name,
					entryPoint));
			}
		}
	}
}
