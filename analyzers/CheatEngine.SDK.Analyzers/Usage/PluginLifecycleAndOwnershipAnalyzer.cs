using System.Collections.Immutable;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.WellKnown;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CheatEngine.SDK.Analyzers.Usage;

/// <summary>
///     Enforces SDK lifecycle boundaries that are visible purely in metadata: CESDK1001 rejects enabled-only calls from
///     plugin construction and initializers, CESDK1003 rejects direct disposal of explicitly borrowed values, and
///     CESDK1005 rejects <c>async void</c> lifecycle callbacks.
/// </summary>
/// <remarks>
///     <para>
///         The analyzer resolves every SDK marker from the compilation and compares symbols, never attribute spellings.
///         A project-local lookalike therefore cannot opt into a diagnostic or make a real SDK annotation disappear.
///     </para>
///     <para>
///         CESDK1003 intentionally follows only direct provenance: an annotated parameter, property access, or invocation
///         return immediately used as the receiver of <c>Dispose</c> or <c>DisposeAsync</c>. General dataflow would need a
///         documented ownership-transfer model before it could safely distinguish a copied borrowed handle from a new
///         owned wrapper.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PluginLifecycleAndOwnershipAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.RequiresPluginEnabledTooEarly,
        DiagnosticDescriptors.DisposeBorrowedValue,
        DiagnosticDescriptors.AsyncPluginLifecycle,
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
        var pluginAttribute =
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.CheatEnginePluginAttribute);
        var pluginBase = SdkSymbolResolver.Hosting(context.Compilation, WellKnownTypeNames.CheatEnginePluginBase);
        var requiresPluginEnabled =
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.RequiresPluginEnabledAttribute);
        var ceOwned = SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.CEOwnedAttribute);
        if (pluginAttribute is null || pluginBase is null || (requiresPluginEnabled is null && ceOwned is null)) return;

        PluginLifecycleContractSymbols symbols = new(pluginAttribute, pluginBase, requiresPluginEnabled, ceOwned);
        context.RegisterOperationAction(operationContext => AnalyzeInvocation(operationContext, symbols),
            OperationKind.Invocation);
        context.RegisterOperationAction(operationContext => AnalyzePropertyReference(operationContext, symbols),
            OperationKind.PropertyReference);
        context.RegisterOperationAction(operationContext => AnalyzeObjectCreation(operationContext, symbols),
            OperationKind.ObjectCreation);
        context.RegisterSymbolAction(symbolContext => AnalyzeMethod(symbolContext, symbols), SymbolKind.Method);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, PluginLifecycleContractSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (symbols.RequiresPluginEnabled is not null && IsTooEarly(context.ContainingSymbol, symbols.PluginAttribute)
                                                      && RequiresEnabled(invocation.TargetMethod,
                                                          symbols.RequiresPluginEnabled))
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RequiresPluginEnabledTooEarly,
                invocation.Syntax.GetLocation(), DisplayName(invocation.TargetMethod)));

        if (symbols.CEOwned is null || !IsDisposal(invocation.TargetMethod) || invocation.Instance is null) return;

        if (!IsExplicitlyBorrowed(invocation.Instance, symbols.CEOwned)) return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.DisposeBorrowedValue,
            invocation.Syntax.GetLocation(), invocation.Instance.Syntax.ToString()));
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context,
        PluginLifecycleContractSymbols symbols)
    {
        if (symbols.RequiresPluginEnabled is null ||
            !IsTooEarly(context.ContainingSymbol, symbols.PluginAttribute)) return;

        var property = (IPropertyReferenceOperation)context.Operation;
        if (!RequiresEnabled(property.Property, symbols.RequiresPluginEnabled)) return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RequiresPluginEnabledTooEarly,
            property.Syntax.GetLocation(), DisplayName(property.Property)));
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, PluginLifecycleContractSymbols symbols)
    {
        if (symbols.RequiresPluginEnabled is null ||
            !IsTooEarly(context.ContainingSymbol, symbols.PluginAttribute)) return;

        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Constructor is null ||
            !RequiresEnabled(creation.Constructor, symbols.RequiresPluginEnabled)) return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RequiresPluginEnabledTooEarly,
            creation.Syntax.GetLocation(), DisplayName(creation.Constructor)));
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, PluginLifecycleContractSymbols symbols)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.IsAsync || !method.ReturnsVoid ||
            !IsPluginClass(method.ContainingType, symbols.PluginAttribute)) return;

        if (!IsLifecycleOverride(method, symbols.PluginBase)) return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.AsyncPluginLifecycle, FirstLocation(method),
            method.Name));
    }

    private static bool IsTooEarly(ISymbol containingSymbol, INamedTypeSymbol pluginAttribute)
    {
        if (!IsPluginClass(containingSymbol.ContainingType, pluginAttribute)) return false;

        return containingSymbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => true,
            IFieldSymbol => true,
            IPropertySymbol => true,
            _ => false,
        };
    }

    private static bool IsPluginClass(INamedTypeSymbol? type, INamedTypeSymbol pluginAttribute)
    {
        return type is not null && HasAttribute(type, pluginAttribute);
    }

    private static bool IsLifecycleOverride(IMethodSymbol method, INamedTypeSymbol pluginBase)
    {
        if (method.Name is not "OnEnable" and not "OnDisable") return false;

        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(overridden.ContainingType, pluginBase))
                return true;

        return false;
    }

    private static bool RequiresEnabled(ISymbol symbol, INamedTypeSymbol requiresPluginEnabled)
    {
        for (var current = symbol; current is not null; current = OverriddenMember(current))
            if (HasAttribute(current, requiresPluginEnabled))
                return true;

        for (var type = symbol.ContainingType; type is not null; type = type.BaseType)
            if (HasAttribute(type, requiresPluginEnabled))
                return true;

        return false;
    }

    private static ISymbol? OverriddenMember(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { AssociatedSymbol: IPropertySymbol property } => property,
            IMethodSymbol { OverriddenMethod: { } overriddenMethod } => overriddenMethod,
            IPropertySymbol { OverriddenProperty: { } overriddenProperty } => overriddenProperty,
            _ => null,
        };
    }

    private static bool IsDisposal(IMethodSymbol method)
    {
        return method.Name is "Dispose" or "DisposeAsync" && method.Parameters.IsEmpty && !method.IsStatic;
    }

    private static bool IsExplicitlyBorrowed(IOperation operation, INamedTypeSymbol ceOwned)
    {
        var current = operation;
        while (current is IConversionOperation or IParenthesizedOperation)
            current = current switch
            {
                IConversionOperation conversion => conversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => current,
            };

        return current switch
        {
            IParameterReferenceOperation parameter => HasAttribute(parameter.Parameter, ceOwned),
            IPropertyReferenceOperation property => HasOwnedProperty(property.Property, ceOwned),
            IInvocationOperation invocation => HasOwnedReturn(invocation.TargetMethod, ceOwned),
            _ => false,
        };
    }

    private static bool HasOwnedProperty(IPropertySymbol property, INamedTypeSymbol ceOwned)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
            if (HasAttribute(current, ceOwned))
                return true;

        return false;
    }

    private static bool HasOwnedReturn(IMethodSymbol method, INamedTypeSymbol ceOwned)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
            foreach (var attribute in current.GetReturnTypeAttributes())
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, ceOwned))
                    return true;

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;

        return false;
    }

    private static string DisplayName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
    }

    private static Location FirstLocation(ISymbol symbol)
    {
        return symbol.Locations.IsEmpty ? Location.None : symbol.Locations[0];
    }

    private sealed class PluginLifecycleContractSymbols(
        INamedTypeSymbol pluginAttribute,
        INamedTypeSymbol pluginBase,
        INamedTypeSymbol? requiresPluginEnabled,
        INamedTypeSymbol? ceOwned)
    {
        public INamedTypeSymbol PluginAttribute { get; } = pluginAttribute;

        public INamedTypeSymbol PluginBase { get; } = pluginBase;

        public INamedTypeSymbol? RequiresPluginEnabled { get; } = requiresPluginEnabled;

        public INamedTypeSymbol? CEOwned { get; } = ceOwned;
    }
}
