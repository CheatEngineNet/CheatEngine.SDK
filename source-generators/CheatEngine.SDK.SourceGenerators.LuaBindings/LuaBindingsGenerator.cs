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
///     API of <c>CheatEngine.SDK.Lua</c> and generated borrowed object handles for <c>[LuaClass]</c>,
///     <c>[LuaMethod]</c> and <c>[LuaProperty]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Output is one file per containing type and binding kind. Only <c>[LuaFunction]</c> output requires unsafe
///         code because its registration table takes thunk addresses; globals and object wrappers do not. A member the
///         generator cannot bind gets no conflicting source: the CESDK2xxx rules of
///         <c>CheatEngine.SDK.Analyzers</c> explain why. A type without any bindable member gets no file.
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

    /// <summary>Metadata name of the generated borrowed-object handle marker.</summary>
    internal const string LuaClassAttributeMetadataName = AnnotationsMetadataNames.LuaClassAttribute;

    /// <summary>Metadata name of the generated object-method marker.</summary>
    internal const string LuaMethodAttributeMetadataName = AnnotationsMetadataNames.LuaMethodAttribute;

    /// <summary>Metadata name of the generated object-property marker.</summary>
    internal const string LuaPropertyAttributeMetadataName = AnnotationsMetadataNames.LuaPropertyAttribute;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        ConfigureFunctions(context);
        ConfigureGlobals(context);
        ConfigureObjectHandles(context);
        ConfigureObjectMembers(context);
    }

    private static void ConfigureFunctions(IncrementalGeneratorInitializationContext context)
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
    }

    private static void ConfigureGlobals(IncrementalGeneratorInitializationContext context)
    {
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
            .Select(static (table, _) => table)
            .WithTrackingName(LuaBindingsTrackingNames.LuaGlobalTable);

        // A global wrapper uses no function pointer or other unsafe syntax. It must remain available to the common
        // consumer that only calls Cheat Engine Lua globals with <AllowUnsafeBlocks>false</AllowUnsafeBlocks>.
        var globalOutputs = globalTables.WithTrackingName(LuaBindingsTrackingNames.LuaGlobalOutput);

        context.RegisterSourceOutput(globalOutputs, static (productionContext, table) =>
            productionContext.AddSource(LuaGlobalFileEmitter.HintName(table), LuaGlobalFileEmitter.Emit(table)));
    }

    private static void ConfigureObjectHandles(IncrementalGeneratorInitializationContext context)
    {
        var luaClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LuaClassAttributeMetadataName,
                static (node, _) => node is StructDeclarationSyntax,
                static (attributeContext, cancellationToken) => LuaClassParser.Parse(attributeContext, cancellationToken))
            .WithTrackingName(LuaBindingsTrackingNames.LuaClass)
            .Collect()
            .WithTrackingName(LuaBindingsTrackingNames.CollectedLuaClasses)
            .Select(static (models, _) => LuaClassTables.Select(models))
            .WithTrackingName(LuaBindingsTrackingNames.LuaClasses)
            .SelectMany(static (models, _) => models.AsImmutableArray())
            .WithTrackingName(LuaBindingsTrackingNames.LuaClassOutput);

        context.RegisterSourceOutput(luaClasses, static (productionContext, model) =>
            productionContext.AddSource(LuaClassFileEmitter.HintName(model), LuaClassFileEmitter.Emit(model)));
    }

    private static void ConfigureObjectMembers(IncrementalGeneratorInitializationContext context)
    {
        var objectMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LuaMethodAttributeMetadataName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) => LuaObjectMethodParser.Parse(attributeContext, cancellationToken))
            .WithTrackingName(LuaBindingsTrackingNames.LuaMethod)
            .Collect()
            .WithTrackingName(LuaBindingsTrackingNames.CollectedLuaMethods);

        var objectProperties = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LuaPropertyAttributeMetadataName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (attributeContext, cancellationToken) => LuaObjectPropertyParser.Parse(attributeContext, cancellationToken))
            .WithTrackingName(LuaBindingsTrackingNames.LuaProperty)
            .Collect()
            .WithTrackingName(LuaBindingsTrackingNames.CollectedLuaProperties);

        var objectMemberTables = objectMethods
            .Combine(objectProperties)
            .WithTrackingName(LuaBindingsTrackingNames.LuaObjectMembersAndProperties)
            .Select(static (pair, _) => LuaObjectMembersTables.Group(pair.Left, pair.Right))
            .WithTrackingName(LuaBindingsTrackingNames.LuaObjectMembersTables)
            .SelectMany(static (tables, _) => tables.AsImmutableArray())
            .WithTrackingName(LuaBindingsTrackingNames.LuaObjectMembersOutput);

        context.RegisterSourceOutput(objectMemberTables, static (productionContext, table) =>
            productionContext.AddSource(LuaObjectMembersFileEmitter.HintName(table), LuaObjectMembersFileEmitter.Emit(table)));
    }
}
