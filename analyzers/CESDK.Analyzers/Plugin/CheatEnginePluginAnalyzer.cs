using System;
using System.Collections.Immutable;
using System.Threading;
using CESDK.Analyzers.Diagnostics;
using CESDK.Analyzers.WellKnown;
using CESDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CESDK.Analyzers.Plugin;

/// <summary>
///     The plugin shape and bootstrap rules. CESDK0001: a class marked <c>[CheatEnginePlugin]</c> that the generated
///     entry point cannot construct. CESDK0002: more than one such class. CESDK0004: a plugin assembly that declares a
///     namespace equal to or nested under <c>CESDK</c>.
/// </summary>
/// <remarks>
///     <para>
///         These are the cases in which <c>CESDK.SourceGenerators.EntryPoint</c> stays silent or in which its output
///         changes name binding; the generator never reports, this analyzer does. CESDK0001 and CESDK0002 are therefore
///         not reported when the project switches the generated entry point off
///         (<c>build_property.CesdkGenerateEntryPoint = false</c>); CESDK0004 is.
///     </para>
///     <para>
///         Stateless and safe for concurrent execution. Everything that lives as long as a compilation is created in the
///         compilation-start action. When <c>CESDK.Annotations.Plugin.CheatEnginePluginAttribute</c> or
///         <c>CESDK.Hosting.Plugin.CheatEnginePlugin</c> cannot be resolved, nothing is registered. CESDK0001 is reported
///         from a
///         symbol action and shows up while typing; CESDK0002 and CESDK0004 need the whole compilation and are
///         compilation-end diagnostics (build and full-solution analysis only). Generated code is neither analysed nor
///         counted.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CheatEnginePluginAnalyzer : DiagnosticAnalyzer
{
    private const string ReservedRootNamespace = "CESDK";

    // The MSBuild switch of the entry point generator, as the compiler sees it.
    private const string GenerateEntryPointKey = "build_property.CesdkGenerateEntryPoint";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.InvalidPluginClass,
        DiagnosticDescriptors.MultiplePluginClasses,
        DiagnosticDescriptors.ReservedNamespace
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
        var pluginAttribute = context.Compilation.GetTypeByMetadataName(WellKnownTypeNames.CheatEnginePluginAttribute);
        var pluginBase = context.Compilation.GetTypeByMetadataName(WellKnownTypeNames.CheatEnginePluginBase);
        if (pluginAttribute is null || pluginBase is null) return;

        // CESDK0001 and CESDK0002 describe what the GENERATED entry point needs. A project that switched it off
        // (CesdkGenerateEntryPoint=false, made compiler-visible by the package's props) bootstraps by hand and sets
        // its own conditions. CESDK0004 stays: Cheat Engine wants a type CESDK.CESDK in the assembly either way.
        var entryPointIsGenerated =
            !context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(GenerateEntryPointKey, out var raw)
            || !bool.TryParse(raw, out var generate)
            || generate;

        // The last two are optional: without them the matching CESDK0001 checks are stricter or skipped, never wrong.
        // pluginBase itself is only the gate above: the shared predicate recognises the base class by name.
        PluginContractSymbols symbols = new(
            pluginAttribute,
            context.Compilation.GetTypeByMetadataName(WellKnownTypeNames.SetsRequiredMembersAttribute),
            context.Compilation.GetTypeByMetadataName(WellKnownTypeNames.ObsoleteAttribute));

        PluginCompilationState state = new(entryPointIsGenerated);
        context.RegisterSymbolAction(
            symbolContext => AnalyzeNamedType(symbolContext, symbols, state, entryPointIsGenerated),
            SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeNamespaceDeclaration(nodeContext, state),
            SyntaxKind.NamespaceDeclaration,
            SyntaxKind.FileScopedNamespaceDeclaration);
        context.RegisterCompilationEndAction(state.Report);
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        PluginContractSymbols symbols,
        PluginCompilationState state,
        bool entryPointIsGenerated)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // The attribute targets classes only: on anything else the compiler already reports CS0592.
        if (type.TypeKind != TypeKind.Class ||
            FindAttribute(type, symbols.PluginAttribute) is not { } attribute) return;

        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken);
        var classLocation = GetClassLocation(type, attributeSyntax);
        state.AddPluginClass(name, classLocation);
        if (!entryPointIsGenerated) return;

        var problems = PluginShape.Inspect(
            type,
            attribute,
            symbols.SetsRequiredMembersAttribute,
            symbols.ObsoleteAttribute,
            out _);
        if (problems == PluginShapeIssues.None) return;

        foreach (var problem in PluginClassProblemText.ReportOrder)
        {
            if ((problems & problem) == PluginShapeIssues.None) continue;

            // The name is a property of the attribute application, everything else of the class declaration.
            var location = problem == PluginShapeIssues.InvalidName && attributeSyntax is not null
                ? attributeSyntax.GetLocation()
                : classLocation;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidPluginClass,
                location,
                ImmutableDictionary<string, string?>.Empty.Add(DiagnosticProperties.PluginClassProblem,
                    problem.ToString()),
                name,
                PluginClassProblemText.Describe(problem)));
        }
    }

    private static AttributeData? FindAttribute(INamedTypeSymbol type, INamedTypeSymbol attributeClass)
    {
        foreach (var attribute in type.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeClass))
                return attribute;

        return null;
    }

    // A partial class has one location per part: the part that carries the attribute is the one the user thinks of
    // as "the plugin class", and the only one that certainly is not generated code.
    private static Location GetClassLocation(INamedTypeSymbol type, SyntaxNode? attributeSyntax)
    {
        for (var node = attributeSyntax; node is not null; node = node.Parent)
            if (node is BaseTypeDeclarationSyntax declaration)
                return declaration.Identifier.GetLocation();

        return type.Locations.IsEmpty ? Location.None : type.Locations[0];
    }

    private static void AnalyzeNamespaceDeclaration(SyntaxNodeAnalysisContext context, PluginCompilationState state)
    {
        var declaration = (BaseNamespaceDeclarationSyntax)context.Node;

        // A nested declaration is under 'CESDK' exactly when its outermost declaration is: one report per outermost one.
        if (declaration.Parent is not CompilationUnitSyntax
            || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamespaceSymbol
                declared
            || !IsUnderReservedRoot(declared, context.CancellationToken))
            return;

        state.AddReservedNamespace(declared.ToDisplayString(), declaration.Name.GetLocation());
    }

    private static bool IsUnderReservedRoot(INamespaceSymbol declared, CancellationToken cancellationToken)
    {
        var root = declared;
        while (root.ContainingNamespace is { IsGlobalNamespace: false } parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            root = parent;
        }

        return string.Equals(root.Name, ReservedRootNamespace, StringComparison.Ordinal);
    }
}
