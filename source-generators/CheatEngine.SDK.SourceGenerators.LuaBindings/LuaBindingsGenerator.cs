using CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings;

/// <summary>
///     Emits, into the consumer's assembly, the Lua side of its bindings: a <c>lua_CFunction</c> thunk and a
///     registration pair per type for every <c>[LuaFunction]</c> static method, and the body of every
///     <c>[LuaGlobal]</c> static partial method with a scalar signature. The emitted code is written against the public
///     API of <c>CheatEngine.SDK.Lua</c>.
/// </summary>
/// <remarks>
///     <para>
///         Output is one file per containing type and binding kind, and only when the compilation allows unsafe code
///         (the registration table takes the address of the thunks). A member the generator cannot bind gets no output
///         and no diagnostic: the CESDK2xxx rules of <c>CheatEngine.SDK.Analyzers</c>, which link the same shape-validation source,
///         explain why. A type without any bindable member gets no file.
///     </para>
///     <para>
///         The compiler may call <see cref="Initialize" /> on any thread; the pipeline callbacks are static, keep no state
///         and never throw on malformed input.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class LuaBindingsGenerator : IIncrementalGenerator
{
    /// <summary>Metadata name of the function-export attribute (declared by <c>CheatEngine.SDK.Annotations</c>).</summary>
    internal const string LuaFunctionAttributeMetadataName = AnnotationsMetadataNames.LuaFunctionAttribute;

    /// <summary>Metadata name of the global-binding attribute (declared by <c>CheatEngine.SDK.Annotations</c>).</summary>
    internal const string LuaGlobalAttributeMetadataName = AnnotationsMetadataNames.LuaGlobalAttribute;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Reduced to a value before it is combined: the Compilation itself never equals its predecessor.
        var facts = context.CompilationProvider
            .Select(static (compilation, _) => CompilationFacts.From(compilation))
            .WithTrackingName(LuaBindingsTrackingNames.Facts);

        // Discovery is attribute-driven only; the predicate is purely syntactic and excludes what can never be
        // bound (accessors, local functions, lambdas, properties). The transform is the single place where
        // symbols are read; the grouping is where the per-type rules live.
        var functionTables = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LuaFunctionAttributeMetadataName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    LuaFunctionParser.Parse(attributeContext, cancellationToken))
            .WithTrackingName(LuaBindingsTrackingNames.LuaFunction)
            .Collect()
            .WithTrackingName(LuaBindingsTrackingNames.CollectedLuaFunctions)
            .Select(static (models, _) => LuaFunctionTables.Group(models))
            .WithTrackingName(LuaBindingsTrackingNames.LuaFunctionTables)
            .SelectMany(static (tables, _) => tables.AsImmutableArray())
            .WithTrackingName(LuaBindingsTrackingNames.LuaFunctionTable);

        var functionOutputs = functionTables
            .Combine(facts)
            .WithTrackingName(LuaBindingsTrackingNames.LuaFunctionTableAndFacts)
            .Where(static pair => pair.Right.AllowUnsafeBlocks)
            .WithTrackingName(LuaBindingsTrackingNames.LuaFunctionTableAllowed)
            .Select(static (pair, _) => pair.Left)
            .WithTrackingName(LuaBindingsTrackingNames.LuaFunctionOutput);

        context.RegisterSourceOutput(functionOutputs, static (productionContext, table) =>
            productionContext.AddSource(LuaFunctionFileEmitter.HintName(table), LuaFunctionFileEmitter.Emit(table)));

        var globalTables = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LuaGlobalAttributeMetadataName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    LuaGlobalParser.Parse(attributeContext, cancellationToken))
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobal)
            .Collect()
            .WithTrackingName(LuaBindingsTrackingNames.CollectedLuaGlobals)
            .Select(static (models, _) => LuaGlobalTables.Group(models))
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobalTables)
            .SelectMany(static (tables, _) => tables.AsImmutableArray())
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobalTable);

        var globalOutputs = globalTables
            .Combine(facts)
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobalTableAndFacts)
            .Where(static pair => pair.Right.AllowUnsafeBlocks)
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobalTableAllowed)
            .Select(static (pair, _) => pair.Left)
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobalOutput);

        context.RegisterSourceOutput(globalOutputs, static (productionContext, table) =>
            productionContext.AddSource(LuaGlobalFileEmitter.HintName(table), LuaGlobalFileEmitter.Emit(table)));
    }
}
