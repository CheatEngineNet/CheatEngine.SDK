using CESDK.SourceGenerators.EngineApi.Emit;
using CESDK.SourceGenerators.EngineApi.Model;
using CESDK.SourceGenerators.EngineApi.Parsing;
using Microsoft.CodeAnalysis;

namespace CESDK.SourceGenerators.EngineApi;

/// <summary>
///     Repository-internal incremental generator (does not ship): turns the curated Cheat Engine API spec, passed as
///     <c>AdditionalFiles</c> named <c>*.cesdk-api.txt</c>, into complete wrapper declarations inside
///     <c>CESDK.Engine</c>. Reuses the same call-shape emitter as <c>CESDK.SourceGenerators.LuaBindings</c>
///     (<c>CESDK.SourceGenerators.Shared.LuaEmit.LuaGlobalCallEmitter</c>), taken from the shared assembly, never from
///     that generator's assembly.
/// </summary>
/// <remarks>
///     <para>
///         One spec file maps to at most one generated file: an <c>AdditionalText</c> that does not end in
///         <c>.cesdk-api.txt</c> is ignored, and a file with zero valid entries (a broken header, every entry invalid, or
///         a
///         spec file that is a shell) produces no output and no diagnostic. This project's README documents the
///         spec grammar; <c>Model/SpecFileModel.Issues</c> exists only so that this generator's own tests can assert why
///         an
///         entry was dropped, never as a diagnostic.
///     </para>
///     <para>
///         The compiler may call <see cref="Initialize" /> on any thread; every pipeline callback is
///         <see langword="static" />, keeps no state and never throws on malformed input.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EngineApiGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var specTexts = context.AdditionalTextsProvider
            .Where(static text => SpecFileParser.IsSpecFile(text.Path))
            .WithTrackingName(EngineApiTrackingNames.SpecTextFile);

        var parsed = specTexts
            .Select(static (text, cancellationToken) =>
                SpecFileParser.Parse(text.Path, text.GetText(cancellationToken)?.ToString()))
            .WithTrackingName(EngineApiTrackingNames.ParsedSpec);

        var files = parsed
            .Collect()
            .WithTrackingName(EngineApiTrackingNames.CollectedSpecs)
            .Select(static (specs, _) => SpecFiles.AssignHintNames(specs))
            .WithTrackingName(EngineApiTrackingNames.SpecFiles)
            .SelectMany(static (specs, _) => specs.AsImmutableArray())
            .WithTrackingName(EngineApiTrackingNames.SpecFile);

        var outputs = files
            .Where(static spec => spec.Calls.Length > 0)
            .WithTrackingName(EngineApiTrackingNames.SpecFileOutput);

        context.RegisterSourceOutput(outputs, static (productionContext, spec) =>
            productionContext.AddSource(EngineApiFileEmitter.HintName(spec), EngineApiFileEmitter.Emit(spec)));
    }
}
