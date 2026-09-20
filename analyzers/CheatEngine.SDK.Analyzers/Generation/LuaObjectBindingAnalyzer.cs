using System;
using System.Collections.Immutable;
using System.Threading;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.WellKnown;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Generation;

/// <summary>
///     Validates the generated borrowed-handle surface: CESDK2006 reports LuaClass, LuaMethod and LuaProperty forms
///     that the LuaBindings generator cannot emit, while CESDK2007 identifies source members that collide with generated
///     identities before generated code reaches the compiler.
/// </summary>
/// <remarks>
///     The rule is deliberately local. CESDK2005 is the sole Lua compilation-end rule because duplicate exported names
///     need cross-member collection; every generated-identity collision is detectable by inspecting the declaring type.
///     All annotation decisions use resolved symbols rather than namespace-and-name matching.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LuaObjectBindingAnalyzer : DiagnosticAnalyzer
{
    private const string PartialKeyword = "partial";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.InvalidLuaAnnotationTarget,
        DiagnosticDescriptors.GeneratedLuaIdentityCollision,
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
        LuaObjectContractSymbols symbols = new(
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaClassAttribute),
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaMethodAttribute),
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaPropertyAttribute),
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaFunctionAttribute),
            SdkSymbolResolver.Annotation(context.Compilation, WellKnownTypeNames.LuaGlobalAttribute),
            SdkSymbolResolver.Lua(context.Compilation, WellKnownTypeNames.LuaState),
            context.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1"),
            context.Compilation.GetTypeByMetadataName("CheatEngine.SDK.Engine.Objects.CEObject"));
        if (!symbols.HasAnyLuaObjectAnnotation) return;

        context.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, symbols), SymbolKind.NamedType);
        context.RegisterSymbolAction(symbolContext => AnalyzeMethod(symbolContext, symbols), SymbolKind.Method);
        context.RegisterSymbolAction(symbolContext => AnalyzeProperty(symbolContext, symbols), SymbolKind.Property);
    }

    private static void AnalyzeType(SymbolAnalysisContext context, LuaObjectContractSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (symbols.LuaClassAttribute is not null
            && FindAttribute(type, symbols.LuaClassAttribute) is { } luaClassAttribute)
        {
            var problem = LuaClassProblem(type, luaClassAttribute, context.CancellationToken);
            if (problem.Length > 0)
                ReportInvalid(context, type, problem);
            else
                ReportLuaClassIdentityCollisions(context, type, symbols.CEObject);
        }

        // LuaFunction and LuaGlobal may live on an ordinary partial type. Their generated thunks, registration methods
        // and cache fields still have to remain distinct from source members even when the type is not a LuaClass.
        ReportLuaFunctionIdentityCollisions(context, type, symbols);
        ReportLuaGlobalIdentityCollisions(context, type, symbols);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, LuaObjectContractSymbols symbols)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (symbols.LuaMethodAttribute is not null
            && FindAttribute(method, symbols.LuaMethodAttribute) is { } luaMethodAttribute)
        {
            var problem = LuaMethodProblem(method, luaMethodAttribute, symbols, context.CancellationToken);
            if (problem.Length > 0) ReportInvalid(context, method, problem);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, LuaObjectContractSymbols symbols)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (symbols.LuaPropertyAttribute is null
            || FindAttribute(property, symbols.LuaPropertyAttribute) is not { } luaPropertyAttribute)
            return;

        var problem = LuaPropertyProblem(property, luaPropertyAttribute, symbols, context.CancellationToken);
        if (problem.Length > 0) ReportInvalid(context, property, problem);
    }

    // CESDK2007 needs all members of the type but no other compilation-wide information, so it remains an IDE-live rule.
    private static void ReportLuaClassIdentityCollisions(SymbolAnalysisContext context, INamedTypeSymbol type,
        INamedTypeSymbol? ceObject)
    {
        if (LuaClassGeneratedNames.IsGeneratedType(type.Name)) ReportCollision(context, type, type.Name);

        foreach (var member in type.GetMembers())
        {
            if (LuaClassGeneratedNames.IsGeneratedMember(member.Name))
                ReportCollision(context, member, member.Name);
            else if (LuaClassGeneratedNames.IsGeneratedAccessorCollision(member, ceObject))
                ReportCollision(context, member, "Handle");
        }

        foreach (var member in type.GetMembers())
            if (member is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method
                && method.Name is "op_Equality" or "op_Inequality")
                ReportCollision(context, member, "operator " +
                                                 (string.Equals(method.Name, "op_Equality", StringComparison.Ordinal)
                                                     ? "=="
                                                     : "!="));

        if (ceObject is null) return;

        foreach (var constructor in type.InstanceConstructors)
            if (constructor.Parameters.Length == 1
                && constructor.Parameters[0].RefKind == RefKind.None
                && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, ceObject))
                ReportCollision(context, constructor, type.Name + "(CEObject)");
    }

    private static void ReportNamedMemberCollisions(SymbolAnalysisContext context, INamedTypeSymbol type,
        string sourceName, string generatedIdentity)
    {
        foreach (var member in type.GetMembers(sourceName)) ReportCollision(context, member, generatedIdentity);
    }

    private static void ReportCollision(SymbolAnalysisContext context, ISymbol member, string generatedIdentity)
    {
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GeneratedLuaIdentityCollision,
            FirstLocation(member), member.Name, generatedIdentity));
    }

    private static void ReportLuaFunctionIdentityCollisions(SymbolAnalysisContext context, INamedTypeSymbol type,
        LuaObjectContractSymbols symbols)
    {
        if (symbols.LuaFunctionAttribute is null) return;

        var containsValidFunction = false;
        var containingIssues = ContainingTypeShape.Inspect(type, context.CancellationToken);
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method
                || FindAttribute(method, symbols.LuaFunctionAttribute) is not { } attribute)
                continue;

            var name = ReadName(attribute);
            var issues = LuaFunctionShape.Inspect(method, symbols.LuaState, out _);
            if (!LuaNames.IsValidName(name)) issues |= LuaFunctionShapeIssues.InvalidName;
            if (issues != LuaFunctionShapeIssues.None || containingIssues != ContainingTypeIssues.None) continue;

            containsValidFunction = true;
            ReportNamedMemberCollisions(context, type, LuaThunkModel.ThunkNameFor(name!),
                LuaThunkModel.ThunkNameFor(name!));
        }

        if (!containsValidFunction) return;

        ReportNamedMemberCollisions(context, type, LuaRegistrationEmitter.RegisterMethodName,
            LuaRegistrationEmitter.RegisterMethodName);
        ReportNamedMemberCollisions(context, type, LuaRegistrationEmitter.UnregisterMethodName,
            LuaRegistrationEmitter.UnregisterMethodName);
    }

    private static void ReportLuaGlobalIdentityCollisions(SymbolAnalysisContext context, INamedTypeSymbol type,
        LuaObjectContractSymbols symbols)
    {
        if (symbols.LuaGlobalAttribute is null) return;

        var containingIssues = ContainingTypeShape.Inspect(type, context.CancellationToken);
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method
                || FindAttribute(method, symbols.LuaGlobalAttribute) is not { } attribute)
                continue;

            var name = ReadName(attribute);
            var issues = LuaGlobalShape.Inspect(method, symbols.LuaState, out _);
            if (!LuaNames.IsValidName(name)) issues |= LuaGlobalShapeIssues.InvalidName;
            if (issues != LuaGlobalShapeIssues.None || containingIssues != ContainingTypeIssues.None) continue;

            ReportLuaGlobalLocalCollisions(context, method);
            var generatedName = LuaGlobalCallModel.CacheFieldFor(name!);
            ReportNamedMemberCollisions(context, type, generatedName, generatedName);
        }
    }

    private static void ReportLuaGlobalLocalCollisions(SymbolAnalysisContext context, IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
            if (IsLuaGlobalGeneratedLocalName(parameter.Name))
                ReportCollision(context, parameter, "generated local " + parameter.Name);
    }

    private static string LuaClassProblem(INamedTypeSymbol type, AttributeData attribute,
        CancellationToken cancellationToken)
    {
        if (!LuaNames.IsValidName(ReadName(attribute))) return "the Lua class name must be a Lua identifier";
        if (type.TypeKind != TypeKind.Struct) return "[LuaClass] is supported only on a struct";
        if (type.IsRecord) return "the borrowed handle struct must not be a record struct";
        if (type.IsRefLikeType) return "the borrowed handle struct must not be ref-like";
        if (!type.IsReadOnly) return "the borrowed handle struct must be readonly";
        if (type.Arity != 0) return "the borrowed handle struct must not be generic";
        if (type.IsFileLocal) return "the borrowed handle struct must not be file-local";
        if (!IsPartial(type, cancellationToken)) return "the borrowed handle struct must be partial";

        return ContainingLuaClassProblem(type.ContainingType, cancellationToken);
    }

    private static string ContainingLuaClassProblem(INamedTypeSymbol? containing, CancellationToken cancellationToken)
    {
        for (; containing is not null; containing = containing.ContainingType)
        {
            if (containing.IsGenericType) return "no containing type of the borrowed handle struct may be generic";
            if (containing.IsFileLocal) return "no containing type of the borrowed handle struct may be file-local";
            if (!IsPartial(containing, cancellationToken))
                return "every containing type of the borrowed handle struct must be partial";
        }

        return string.Empty;
    }

    private static string LuaMethodProblem(IMethodSymbol method, AttributeData attribute,
        LuaObjectContractSymbols symbols,
        CancellationToken cancellationToken)
    {
        var classProblem = LuaClassProblemForMember(method.ContainingType, symbols, cancellationToken);
        if (classProblem.Length > 0) return classProblem;
        if (!LuaNames.IsValidName(ReadName(attribute))) return "the Lua method name must be a Lua identifier";
        if (method.MethodKind != MethodKind.Ordinary) return "the member must be an ordinary method";
        if (method.IsStatic) return "the generated object method needs an instance receiver";
        if (method.IsGenericMethod) return "the generated object method must not be generic";
        if (method.IsAsync) return "the generated object method must not be async";
        if (!method.IsPartialDefinition) return "the member must be the defining declaration of a partial method";
        if (method.PartialImplementationPart is not null) return "the partial method already has an implementation";
        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
            return "ref and ref readonly returns are not supported";

        var hasOutResult = false;
        foreach (var parameter in method.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsGeneratedLocalName(parameter.Name)) return "the parameter name collides with a generated local";
            if (parameter.RefKind == RefKind.Out)
            {
                hasOutResult = true;
                if (!IsScalar(parameter.Type, allowReadOnlySpan: false, symbols.ReadOnlySpan))
                    return "out results must be supported scalar values";

                continue;
            }

            if (hasOutResult) return "arguments must precede every out result";
            if (parameter.RefKind != RefKind.None) return "ref, in and ref readonly parameters are not supported";
            if (parameter.IsParams || parameter.HasExplicitDefaultValue)
                return "optional and params parameters are not supported";
            if (symbols.LuaState is not null && SymbolEqualityComparer.Default.Equals(parameter.Type, symbols.LuaState))
                return "LuaMethod does not take a LuaState parameter";
            if (!IsScalar(parameter.Type, allowReadOnlySpan: true, symbols.ReadOnlySpan))
                return "parameters must be supported scalar values";
        }

        return LuaMethodReturnProblem(method, hasOutResult, symbols.ReadOnlySpan);
    }

    private static string LuaMethodReturnProblem(IMethodSymbol method, bool hasOutResult,
        INamedTypeSymbol? readOnlySpan)
    {
        if (hasOutResult)
            return method.ReturnType.SpecialType == SpecialType.System_Boolean
                ? string.Empty
                : "a method with out results must return bool";

        return method.ReturnsVoid || IsScalar(method.ReturnType, allowReadOnlySpan: false, readOnlySpan)
            ? string.Empty
            : "the throwing form must return void or a supported scalar value";
    }

    private static string LuaPropertyProblem(IPropertySymbol property, AttributeData attribute,
        LuaObjectContractSymbols symbols,
        CancellationToken cancellationToken)
    {
        var classProblem = LuaClassProblemForMember(property.ContainingType, symbols, cancellationToken);
        if (classProblem.Length > 0) return classProblem;
        if (!LuaNames.IsValidName(ReadName(attribute))) return "the Lua property name must be a Lua identifier";
        if (property.IsStatic) return "the generated object property needs an instance receiver";
        if (property.RefKind != RefKind.None)
            return "ref and ref readonly properties are not supported";
        if (!IsScalar(property.Type, allowReadOnlySpan: false, symbols.ReadOnlySpan))
            return "the property type must be a supported scalar value";
        if (!IsBodylessPartialProperty(property, cancellationToken))
            return "the member must be a partial property with bodyless get and/or set accessors";

        return string.Empty;
    }

    private static string LuaClassProblemForMember(INamedTypeSymbol containingType, LuaObjectContractSymbols symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.LuaClassAttribute is null
            || FindAttribute(containingType, symbols.LuaClassAttribute) is not { } luaClassAttribute)
            return "the containing type must carry [LuaClass]";

        return LuaClassProblem(containingType, luaClassAttribute, cancellationToken);
    }

    private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var hasDeclaration = false;
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration) return false;

            hasDeclaration = true;
            var hasPartialModifier = false;
            foreach (var modifier in declaration.Modifiers)
                if (string.Equals(modifier.ValueText, PartialKeyword, StringComparison.Ordinal))
                {
                    hasPartialModifier = true;
                    break;
                }

            if (!hasPartialModifier) return false;
        }

        return hasDeclaration;
    }

    private static bool IsBodylessPartialProperty(IPropertySymbol property, CancellationToken cancellationToken)
    {
        var hasDeclaration = false;
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax declaration) return false;

            hasDeclaration = true;
            var hasPartialModifier = false;
            foreach (var modifier in declaration.Modifiers)
                if (string.Equals(modifier.ValueText, PartialKeyword, StringComparison.Ordinal))
                {
                    hasPartialModifier = true;
                    break;
                }

            if (!hasPartialModifier || declaration.AccessorList is null ||
                declaration.AccessorList.Accessors.Count == 0)
                return false;

            if (declaration.ExplicitInterfaceSpecifier is not null) return false;

            foreach (var accessor in declaration.AccessorList.Accessors)
                if (accessor.Kind() is not SyntaxKind.GetAccessorDeclaration and not SyntaxKind.SetAccessorDeclaration
                    || accessor.Body is not null || accessor.ExpressionBody is not null
                    || !HasSupportedAccessorModifiers(accessor))
                    return false;
        }

        return hasDeclaration;
    }

    private static bool HasSupportedAccessorModifiers(AccessorDeclarationSyntax accessor)
    {
        foreach (var modifier in accessor.Modifiers)
            if (modifier.Kind() is not (SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword
                or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword))
                return false;

        return true;
    }

    private static bool IsScalar(ITypeSymbol type, bool allowReadOnlySpan, INamedTypeSymbol? readOnlySpan)
    {
        if (type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Single
            or SpecialType.System_Double or SpecialType.System_Boolean or SpecialType.System_UIntPtr
            or SpecialType.System_String)
            return true;

        if (!allowReadOnlySpan || readOnlySpan is null || type is not INamedTypeSymbol { IsGenericType: true } named
            || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, readOnlySpan)
            || named.TypeArguments.Length != 1)
            return false;

        return named.TypeArguments[0].SpecialType == SpecialType.System_Byte;
    }

    private static bool IsGeneratedLocalName(string name)
    {
        return name is "__ceState" or "__ceOperation" or "__ceTop" or "__ceStatus" or "__ceResult";
    }

    private static bool IsLuaGlobalGeneratedLocalName(string name)
    {
        return name is "__L" or "__operation" or "__top" or "__ok" or "__status" or "__result";
    }

    private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol attributeClass)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeClass))
                return attribute;

        return null;
    }

    private static string? ReadName(AttributeData attribute)
    {
        return attribute.ConstructorArguments is [{ Kind: TypedConstantKind.Primitive, Value: string name }]
            ? name
            : null;
    }

    private static void ReportInvalid(SymbolAnalysisContext context, ISymbol symbol, string problem)
    {
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InvalidLuaAnnotationTarget,
            FirstLocation(symbol), symbol.Name, problem));
    }

    private static Location FirstLocation(ISymbol symbol)
    {
        return symbol.Locations.IsEmpty ? Location.None : symbol.Locations[0];
    }

    private sealed class LuaObjectContractSymbols(
        INamedTypeSymbol? luaClassAttribute,
        INamedTypeSymbol? luaMethodAttribute,
        INamedTypeSymbol? luaPropertyAttribute,
        INamedTypeSymbol? luaFunctionAttribute,
        INamedTypeSymbol? luaGlobalAttribute,
        INamedTypeSymbol? luaState,
        INamedTypeSymbol? readOnlySpan,
        INamedTypeSymbol? ceObject)
    {
        public INamedTypeSymbol? LuaClassAttribute { get; } = luaClassAttribute;

        public INamedTypeSymbol? LuaMethodAttribute { get; } = luaMethodAttribute;

        public INamedTypeSymbol? LuaPropertyAttribute { get; } = luaPropertyAttribute;

        public INamedTypeSymbol? LuaFunctionAttribute { get; } = luaFunctionAttribute;

        public INamedTypeSymbol? LuaGlobalAttribute { get; } = luaGlobalAttribute;

        public INamedTypeSymbol? LuaState { get; } = luaState;

        public INamedTypeSymbol? ReadOnlySpan { get; } = readOnlySpan;

        public INamedTypeSymbol? CEObject { get; } = ceObject;

        public bool HasAnyLuaObjectAnnotation => LuaClassAttribute is not null || LuaMethodAttribute is not null
                                                                               || LuaPropertyAttribute is not null ||
                                                                               LuaFunctionAttribute is not null
                                                                               || LuaGlobalAttribute is not null;
    }
}
