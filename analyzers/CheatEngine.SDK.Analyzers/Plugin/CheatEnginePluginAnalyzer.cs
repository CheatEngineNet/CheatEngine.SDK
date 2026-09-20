using System;
using System.Collections.Immutable;
using System.Threading;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.WellKnown;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Plugin;

/// <summary>
///     The plugin shape and bootstrap rules. CESDK0001: a class marked <c>[CheatEnginePlugin]</c> that the generated
///     entry point cannot construct. CESDK0002: more than one such class. CESDK0003: an incomplete manual bootstrap.
///     CESDK0004: a generated plugin assembly that declares a namespace equal to or nested under <c>CESDK</c>.
///     CESDK0005: source that collides with the generated <c>CESDK.CESDK</c> identity.
/// </summary>
/// <remarks>
///     <para>
///         These are the cases in which <c>CheatEngine.SDK.SourceGenerators.EntryPoint</c> stays silent or in which its output
///         changes name binding; the generator never reports, this analyzer does. CESDK0001, CESDK0002, CESDK0004 and
///         CESDK0005 explain generated output, whereas CESDK0003 explains the manual replacement when the project sets
///         <c>build_property.CheatEngineSdkGenerateEntryPoint = false</c>.
///     </para>
///     <para>
///         Stateless and safe for concurrent execution. Everything that lives as long as a compilation is created in the
///         compilation-start action. When <c>CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute</c> or
///         <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c> cannot be resolved, nothing is registered. CESDK0001 is reported
///         from a symbol action and shows up while typing; the remaining rules need the whole compilation and are
///         compilation-end diagnostics (build and full-solution analysis only). Generated code is neither analysed nor
///         counted.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CheatEnginePluginAnalyzer : DiagnosticAnalyzer
{
    // The namespace of the type CESDK.CESDK that Cheat Engine looks up in every plugin assembly. The SDK's own namespaces
    // (CheatEngine.SDK.*) are not reserved: only a root segment of exactly this name is.
    private const string ReservedRootNamespace = "CESDK";

    // The MSBuild switch of the entry point generator, as the compiler sees it.
    private const string GenerateEntryPointKey = "build_property.CheatEngineSdkGenerateEntryPoint";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.InvalidPluginClass,
        DiagnosticDescriptors.MultiplePluginClasses,
        DiagnosticDescriptors.InvalidManualBootstrap,
        DiagnosticDescriptors.ReservedNamespace,
        DiagnosticDescriptors.GeneratedEntryPointCollision
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
        var pluginAttribute = SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.CheatEnginePluginAttribute);
        var pluginBase = SdkSymbolResolver.Hosting(context.Compilation, WellKnownTypeNames.CheatEnginePluginBase);
        if (pluginAttribute is null || pluginBase is null) return;

        // CESDK0001, CESDK0002, CESDK0004 and CESDK0005 describe what the GENERATED entry point needs. The direct
        // package build asset makes the property compiler-visible and supplies true by default. Without that explicit
        // contract (for example through an indirect package reference), this analyzer must stay out of the way rather
        // than inventing either a generated or manual bootstrap obligation. An explicit false transfers ownership of
        // CESDK.CESDK to the author, which CESDK0003 validates at compilation end.
        bool? entryPointIsGenerated = null;
        if (context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(GenerateEntryPointKey, out var raw)
            && bool.TryParse(raw, out var generate))
            entryPointIsGenerated = generate;

        // The last two are optional: without them the matching CESDK0001 checks are stricter or skipped, never wrong.
        PluginContractSymbols symbols = new(
            pluginAttribute,
            pluginBase,
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
        bool? entryPointIsGenerated)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (IsEntryPointType(type))
            state.AddEntryPointType(
                type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                FirstLocation(type),
                IsManualBootstrap(type));

        // The attribute targets classes only: on anything else the compiler already reports CS0592.
        if (type.TypeKind != TypeKind.Class ||
            FindAttribute(type, symbols.PluginAttribute) is not { } attribute) return;

        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken);
        var classLocation = GetClassLocation(type, attributeSyntax);
        state.AddPluginClass(name, classLocation);
        if (entryPointIsGenerated is not true) return;

        var problems = PluginShape.Inspect(
            type,
            attribute,
            symbols.PluginBase,
            symbols.SetsRequiredMembersAttribute,
            symbols.ObsoleteAttribute,
            out _,
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

    private static bool IsEntryPointType(INamedTypeSymbol type)
    {
        return type is
        {
            Name: ReservedRootNamespace,
            Arity: 0,
            ContainingType: null,
            ContainingNamespace:
            {
                Name: ReservedRootNamespace,
                ContainingNamespace.IsGlobalNamespace: true
            }
        };
    }

    private static bool IsManualBootstrap(INamedTypeSymbol type)
    {
        if (!type.IsStatic) return false;

        foreach (var member in type.GetMembers("CEPluginInitialize"))
        {
            if (member is not IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: true,
                    IsGenericMethod: false,
                    DeclaredAccessibility: Accessibility.Public,
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    ReturnType.SpecialType: SpecialType.System_Int32,
                    Parameters:
                    [
                        { RefKind: RefKind.None, Type.SpecialType: SpecialType.System_IntPtr },
                        { RefKind: RefKind.None, Type.SpecialType: SpecialType.System_Int32 }
                    ]
                })
                continue;

            return true;
        }

        return false;
    }

    private static Location FirstLocation(ISymbol symbol)
    {
        return symbol.Locations.IsEmpty ? Location.None : symbol.Locations[0];
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
