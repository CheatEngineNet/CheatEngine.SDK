using CheatEngine.SDK.SourceGenerators.EngineApi.Emit;
using CheatEngine.SDK.SourceGenerators.EngineApi.Model;
using CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EngineApi;

/// <summary>
///     Repository-internal incremental generator (does not ship): turns the curated Cheat Engine API spec, passed as
///     <c>AdditionalFiles</c> named <c>*.cheatengine-sdk-api.txt</c>, into complete wrapper declarations inside
///     <c>CheatEngine.SDK.Engine</c>. Reuses the same call-shape emitter as
///     <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>
///     (<c>CheatEngine.SDK.SourceGenerators.Shared.LuaEmit.LuaGlobalCallEmitter</c>), taken from the shared assembly,
///     never from
///     that generator's assembly.
/// </summary>
/// <remarks>
///     <para>
///         One spec file maps to at most one generated file: an <c>AdditionalText</c> that does not end in
///         <c>.cheatengine-sdk-api.txt</c> is ignored, and a file with zero valid entries (a broken header, every entry
///         invalid, or
///         a spec file that is a shell) produces no output. Every malformed header, entry, or cross-file generated
///         identity conflict reports a <c>CESDK3xxx</c> diagnostic against the originating additional file, so a spec
///         can never silently remove an API from the build.
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

        context.RegisterSourceOutput(files, static (productionContext, spec) =>
        {
            foreach (var issue in spec.Issues)
                productionContext.ReportDiagnostic(EngineApiDiagnostics.Create(spec, issue));
        });

        var outputs = files
            .Where(static spec => spec.Calls.Length > 0 && !spec.IsSuppressed)
            .WithTrackingName(EngineApiTrackingNames.SpecFileOutput);

        context.RegisterSourceOutput(outputs, static (productionContext, spec) =>
            productionContext.AddSource(EngineApiFileEmitter.HintName(spec), EngineApiFileEmitter.Emit(spec)));
    }
}
