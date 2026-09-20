using CheatEngine.SDK.SourceGenerators.EntryPoint.Emit;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Model;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint;

/// <summary>
///     Emits the managed entry point Cheat Engine looks for (<c>CESDK.CESDK.CEPluginInitialize</c>) and a file-local
///     plugin factory into the assembly that declares the <c>[CheatEnginePlugin]</c> class.
/// </summary>
/// <remarks>
///     <para>
///         Output is produced only when generation is switched on (MSBuild property
///         <c>CheatEngineSdkGenerateEntryPoint</c>,
///         default
///         <c>false</c> unless the direct package build asset makes it compiler-visible) and exactly one valid plugin
///         class exists. In every other case the generator emits nothing and
///         reports nothing: the diagnostics (CESDK0001 and CESDK0002) belong to <c>CheatEngine.SDK.Analyzers</c>.
///     </para>
///     <para>
///         The compiler may call <see cref="Initialize" /> on any thread; the pipeline callbacks are static, keep no state
///         and never throw on malformed input.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EntryPointGenerator : IIncrementalGenerator
{
    /// <summary>Metadata name of the marker attribute (declared by <c>CheatEngine.SDK.Annotations</c>).</summary>
    internal const string PluginAttributeMetadataName = AnnotationsMetadataNames.CheatEnginePluginAttribute;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Discovery is attribute-driven only (never a base-type scan): the compiler indexes attribute names, and the
        // predicate is purely syntactic. The transform is the single place where symbols are read.
        var plugin = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PluginAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    PluginParser.Parse(attributeContext, cancellationToken))
            .WithTrackingName(EntryPointTrackingNames.Plugin);

        var plugins = plugin
            .Collect()
            .WithTrackingName(EntryPointTrackingNames.CollectedPlugins)
            .Select(static (models, _) => new EquatableArray<PluginModel>(models))
            .WithTrackingName(EntryPointTrackingNames.Plugins);

        // Reduced to a value before it is combined: the options provider object itself never compares equal.
        var options = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => EntryPointOptions.From(provider.GlobalOptions))
            .WithTrackingName(EntryPointTrackingNames.Options);

        // A hand-written CESDK.CESDK is a source-identity collision, even when it is not itself a plugin class. Keep
        // this as a scalar projection so an unrelated compilation edit can leave the final BootstrapModel unchanged.
        var entryPointTypeCollision = context.CompilationProvider
            .Select(static (compilation, _) => EntryPointGeneratedIdentity.HasEntryPointTypeCollision(compilation))
            .WithTrackingName(EntryPointTrackingNames.EntryPointTypeCollision);

        // One more projection instead of deciding inside the output: the source output then depends on three strings
        // only, so a second (invalid) plugin class or an unrelated option never re-emits the file.
        var bootstrap = plugins
            .Combine(options)
            .WithTrackingName(EntryPointTrackingNames.PluginsAndOptions)
            .Combine(entryPointTypeCollision)
            .WithTrackingName(EntryPointTrackingNames.PluginsOptionsAndCollision)
            .Select(static (pair, _) => BootstrapModel.Select(pair.Left.Left, pair.Left.Right, pair.Right))
            .WithTrackingName(EntryPointTrackingNames.Bootstrap);

        context.RegisterSourceOutput(bootstrap, static (productionContext, model) =>
        {
            if (model is not null) productionContext.AddSource(BootstrapEmitter.HintName, BootstrapEmitter.Emit(model));
        });
    }
}
