using System.Collections.Immutable;
using CESDK.Analyzers.Diagnostics;
using CESDK.Analyzers.WellKnown;
using CESDK.SourceGenerators.Shared.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CESDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CESDK.Analyzers.Generation;

/// <summary>
///     The generator-input rules for <c>CESDK.SourceGenerators.LuaBindings</c>. CESDK2001: a <c>[LuaFunction]</c> or
///     <c>[LuaGlobal]</c> member exists but the compilation does not allow unsafe code. CESDK2002: the type that
///     declares such a member cannot receive a generated part. CESDK2003: a <c>[LuaFunction]</c> method cannot be
///     exported by a generated thunk. CESDK2004: a <c>[LuaGlobal]</c> method cannot receive a generated body.
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
///         compilation-start action. When neither <c>CESDK.Annotations.Lua.LuaFunctionAttribute</c> nor
///         <c>CESDK.Annotations.Lua.LuaGlobalAttribute</c> can be resolved, nothing is registered. CESDK2001, CESDK2002
///         and
///         CESDK2004, and eleven of CESDK2003's twelve <c>LuaFunctionShapeIssues</c> flags, are reported from a symbol
///         action and show up while typing. CESDK2003's <see cref="LuaFunctionShapeIssues.DuplicateName" /> is the one
///         exception: it needs every sibling member of a containing type, not just the one method a symbol action is
///         given,
///         so it is decided by a second, compilation-end registration (<see cref="LuaFunctionDuplicateState" />), the
///         same
///         technique <c>PluginCompilationState</c> uses for CESDK0002. RS1037 then requires the <c>InvalidLuaFunction</c>
///         descriptor to carry the <c>CompilationEnd</c> tag (any report of an ID from a compilation-end action does),
///         which in an IDE defers the whole rule (the still-live eleven flags included) to build or full-solution
///         analysis; see that descriptor's own remarks. Generated code is neither analysed nor counted.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LuaBindingAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.UnsafeBlocksRequired,
        DiagnosticDescriptors.InvalidLuaBindingContainingType,
        DiagnosticDescriptors.InvalidLuaFunction,
        DiagnosticDescriptors.InvalidLuaGlobal
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
        var luaFunctionAttribute = context.Compilation.GetTypeByMetadataName(WellKnownTypeNames.LuaFunctionAttribute);
        var luaGlobalAttribute = context.Compilation.GetTypeByMetadataName(WellKnownTypeNames.LuaGlobalAttribute);
        if (luaFunctionAttribute is null && luaGlobalAttribute is null) return;

        LuaBindingContractSymbols symbols = new(luaFunctionAttribute, luaGlobalAttribute);

        // Mirrors CESDK.SourceGenerators.LuaBindings.Model.CompilationFacts.From: a non-C# compilation (never seen
        // here, the analyzer is C#-only) would read as "unsafe not allowed" too.
        var allowsUnsafe = context.Compilation.Options is CSharpCompilationOptions { AllowUnsafe: true };

        LuaFunctionDuplicateState duplicateNames = new();
        context.RegisterSymbolAction(
            symbolContext => AnalyzeMethod(symbolContext, symbols, allowsUnsafe, duplicateNames), SymbolKind.Method);
        context.RegisterCompilationEndAction(duplicateNames.Report);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, LuaBindingContractSymbols symbols,
        bool allowsUnsafe, LuaFunctionDuplicateState duplicateNames)
    {
        var method = (IMethodSymbol)context.Symbol;
        var luaFunction = symbols.LuaFunctionAttribute is null
            ? null
            : FindAttribute(method, symbols.LuaFunctionAttribute);
        var luaGlobal = symbols.LuaGlobalAttribute is null ? null : FindAttribute(method, symbols.LuaGlobalAttribute);
        if (luaFunction is null && luaGlobal is null) return;

        var name = method.Name;
        var location = FirstLocation(method);

        if (!allowsUnsafe)
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnsafeBlocksRequired, location, name));

        var typeIssues = ContainingTypeShape.Inspect(method.ContainingType, context.CancellationToken);
        foreach (var problem in ContainingTypeProblemText.ReportOrder)
        {
            if ((typeIssues & problem) == ContainingTypeIssues.None) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidLuaBindingContainingType, location, name,
                ContainingTypeProblemText.Describe(problem)));
        }

        if (luaFunction is not null)
            AnalyzeLuaFunction(context, method, luaFunction, location, typeIssues, duplicateNames);

        if (luaGlobal is not null) AnalyzeLuaGlobal(context, method, luaGlobal, location);
    }

    private static void AnalyzeLuaFunction(
        SymbolAnalysisContext context,
        IMethodSymbol method,
        AttributeData attribute,
        Location location,
        ContainingTypeIssues typeIssues,
        LuaFunctionDuplicateState duplicateNames)
    {
        var name = ReadName(attribute);
        var issues = LuaFunctionShape.Inspect(method, out _);
        if (!LuaNames.IsValidName(name)) issues |= LuaFunctionShapeIssues.InvalidName;

        foreach (var problem in LuaFunctionProblemText.ReportOrder)
        {
            if ((issues & problem) == LuaFunctionShapeIssues.None) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidLuaFunction, location, method.Name,
                LuaFunctionProblemText.Describe(problem)));
        }

        // A duplicate-name verdict needs every sibling member of the containing type, not just this one method:
        // only a method with no other problem is a candidate, exactly the generator's own grouping input
        // (LuaFunctionModel.IsValid). The compilation-end action (LuaFunctionDuplicateState.Report) reports it.
        if (issues == LuaFunctionShapeIssues.None && typeIssues == ContainingTypeIssues.None)
            duplicateNames.AddCandidate(method.ContainingType, name!, method.Name, location);
    }

    private static void AnalyzeLuaGlobal(SymbolAnalysisContext context, IMethodSymbol method, AttributeData attribute,
        Location location)
    {
        var issues = LuaGlobalShape.Inspect(method, out _);
        if (!LuaNames.IsValidName(ReadName(attribute))) issues |= LuaGlobalShapeIssues.InvalidName;

        foreach (var problem in LuaGlobalProblemText.ReportOrder)
        {
            if ((issues & problem) == LuaGlobalShapeIssues.None) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidLuaGlobal, location, method.Name, LuaGlobalProblemText.Describe(problem)));
        }
    }

    // The name argument of [LuaFunction(name)]/[LuaGlobal(name)]; null while the author is typing (missing, not a
    // string, or explicitly null), which LuaNames.IsValidName also rejects. The attribute constructor's own
    // ArgumentException never runs at compile time, so an empty string reaches here too.
    private static string? ReadName(AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        return arguments is [{ Kind: TypedConstantKind.Primitive, Value: string name }]
            ? name
            : null;
    }

    private static AttributeData? FindAttribute(IMethodSymbol method, INamedTypeSymbol attributeClass)
    {
        foreach (var attribute in method.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeClass))
                return attribute;

        return null;
    }

    private static Location FirstLocation(IMethodSymbol method)
    {
        return method.Locations.IsEmpty ? Location.None : method.Locations[0];
    }
}
