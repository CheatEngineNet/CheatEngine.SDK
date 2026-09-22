using CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;
using CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Emit;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract;

/// <summary>
///     Repository-only incremental generator for the managed view of the C11 protected-Lua operation contract.
/// </summary>
/// <remarks>
///     <para>
///         The generator reads exactly the <c>protected-operations.json</c> <see cref="AdditionalText" /> supplied by
///         <c>CheatEngine.SDK.Lua.Interop</c>. It performs no file, environment, network, clock, or culture-dependent
///         access. It does not generate, compile, or otherwise own C11 source.
///     </para>
///     <para>
///         Invalid JSON or an incompatible operation catalogue produces a located <c>CESDK4001</c> diagnostic and no
///         source. More than one matching catalogue produces <c>CESDK4002</c> on every participating additional file.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class LuaBridgeContractGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<CatalogInput> inputs = context.AdditionalTextsProvider
			.Where(static text => ProtectedOperationCatalogParser.IsCatalogFile(text.Path))
			.Select(static (text, cancellationToken) =>
				new CatalogInput(text.Path, text.GetText(cancellationToken)?.ToString() ?? string.Empty));

		IncrementalValuesProvider<CatalogParseResult> parsed = inputs.Select(static (input, _) =>
			ProtectedOperationCatalogParser.Parse(input));
		IncrementalValueProvider<LuaBridgeContractGenerationPlan> plans = parsed.Collect()
			.Select(static (catalogs, _) => LuaBridgeContractGenerationPlan.Create(catalogs));

		context.RegisterSourceOutput(plans, static (productionContext, plan) =>
		{
			for (int i = 0; i < plan.Diagnostics.Length; i++)
			{
				productionContext.ReportDiagnostic(LuaBridgeContractDiagnostics.Create(plan.Diagnostics[i]));
			}

			if (plan.Catalog is not null)
			{
				productionContext.AddSource(LuaProtectedOperationEmitter.HintName,
					LuaProtectedOperationEmitter.Emit(plan.Catalog));
			}
		});
	}
}
