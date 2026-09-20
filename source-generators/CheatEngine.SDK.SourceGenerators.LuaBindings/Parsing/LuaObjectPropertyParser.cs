using System.Text;
using System.Threading;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>Parses the supported bodyless partial-property form of <c>[LuaProperty]</c>.</summary>
internal static class LuaObjectPropertyParser
{
    /// <summary>Builds one value-only property model; malformed properties are not emitted.</summary>
    public static LuaObjectPropertyModel Parse(GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var property = (IPropertySymbol)context.TargetSymbol;
        var declaration = context.TargetNode as PropertyDeclarationSyntax;
        var compilation = context.SemanticModel.Compilation;
        var isSdkAttribute = LuaBindingSymbols.ContainsSdkAttribute(context.Attributes, compilation,
            LuaBindingsGenerator.LuaPropertyAttributeMetadataName);
        var luaName = LuaBindingSymbols.ReadSdkAttributeName(context.Attributes, compilation,
            LuaBindingsGenerator.LuaPropertyAttributeMetadataName);
        var described = TryDescribe(property, declaration, out var model);
        var valid = isSdkAttribute && LuaNames.IsValidName(luaName)
                                   && LuaClassParser.IsGeneratedHandle(property.ContainingType, compilation,
                                       cancellationToken)
                                   && described;

        if (valid)
            return model with { LuaName = luaName!, IsValid = true };

        return new LuaObjectPropertyModel(
            ContainingTypeParser.Parse(property.ContainingType),
            luaName ?? string.Empty,
            string.Empty,
            Identifiers.Escape(property.Name),
            default,
            IsNullable: false,
            HasGetter: false,
            string.Empty,
            HasSetter: false,
            string.Empty,
            property.Name,
            IsValid: false);
    }

    private static bool TryDescribe(IPropertySymbol property, PropertyDeclarationSyntax? declaration,
        out LuaObjectPropertyModel model)
    {
        var kind = default(LuaValueKind);
        var isNullable = false;
        var modifiers = string.Empty;
        var typeIsSupported = LuaValueKindMapper.TryMap(property.Type, out kind, out isNullable)
                              && LuaValueKinds.CanBeResult(kind);
        var definitionIsSupported = IsSupportedDefinition(property, declaration, typeIsSupported,
            out modifiers);
        var accessorsAreSupported = TryDescribeAccessors(declaration, out var hasGetter, out var getterModifiers,
            out var hasSetter, out var setterModifiers);
        var valid = definitionIsSupported && accessorsAreSupported;

        model = new LuaObjectPropertyModel(
            ContainingTypeParser.Parse(property.ContainingType),
            string.Empty,
            modifiers,
            Identifiers.Escape(property.Name),
            kind,
            isNullable,
            hasGetter,
            getterModifiers,
            hasSetter,
            setterModifiers,
            property.Name,
            valid);
        return valid;
    }

    private static bool IsSupportedDefinition(IPropertySymbol property, PropertyDeclarationSyntax? declaration,
        bool typeIsSupported, out string modifiers)
    {
        modifiers = string.Empty;
        return declaration is not null
               && !property.IsStatic
               && !property.IsIndexer
               && property.RefKind == RefKind.None
               && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
               && declaration.AccessorList is not null
               && declaration.ExplicitInterfaceSpecifier is null
               && property.PartialImplementationPart is null
               && typeIsSupported
               && TryModifiers(declaration, out modifiers);
    }

    private static bool TryDescribeAccessors(PropertyDeclarationSyntax? declaration, out bool hasGetter,
        out string getterModifiers, out bool hasSetter, out string setterModifiers)
    {
        hasGetter = false;
        getterModifiers = string.Empty;
        hasSetter = false;
        setterModifiers = string.Empty;
        if (declaration?.AccessorList is not { } accessorList) return false;

        foreach (var accessor in accessorList.Accessors)
            if (!TryDescribeAccessor(accessor, ref hasGetter, ref getterModifiers, ref hasSetter,
                    ref setterModifiers))
                return false;

        return hasGetter || hasSetter;
    }

    private static bool TryDescribeAccessor(AccessorDeclarationSyntax accessor, ref bool hasGetter,
        ref string getterModifiers, ref bool hasSetter, ref string setterModifiers)
    {
        if (accessor.Body is not null || accessor.ExpressionBody is not null
                                      || !TryAccessorModifiers(accessor, out var modifiers))
            return false;

        switch (accessor.Kind())
        {
            case SyntaxKind.GetAccessorDeclaration when !hasGetter:
                hasGetter = true;
                getterModifiers = modifiers;
                return true;
            case SyntaxKind.SetAccessorDeclaration when !hasSetter:
                hasSetter = true;
                setterModifiers = modifiers;
                return true;
            default:
                return false;
        }
    }

    private static bool TryModifiers(PropertyDeclarationSyntax declaration, out string modifiers)
    {
        StringBuilder builder = new();
        foreach (var token in declaration.Modifiers)
        {
            if (!IsSupportedPropertyModifier(token.Kind()))
            {
                modifiers = string.Empty;
                return false;
            }

            Append(builder, token.ValueText);
        }

        modifiers = builder.ToString();
        return true;
    }

    private static bool IsSupportedPropertyModifier(SyntaxKind kind)
    {
        return kind is SyntaxKind.PublicKeyword
            or SyntaxKind.InternalKeyword
            or SyntaxKind.ProtectedKeyword
            or SyntaxKind.PrivateKeyword
            or SyntaxKind.NewKeyword
            or SyntaxKind.ReadOnlyKeyword
            or SyntaxKind.UnsafeKeyword
            or SyntaxKind.VirtualKeyword
            or SyntaxKind.OverrideKeyword
            or SyntaxKind.SealedKeyword
            or SyntaxKind.RequiredKeyword
            or SyntaxKind.PartialKeyword;
    }

    private static bool TryAccessorModifiers(AccessorDeclarationSyntax accessor, out string modifiers)
    {
        StringBuilder builder = new();
        foreach (var token in accessor.Modifiers)
            switch (token.Kind())
            {
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.InternalKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PrivateKeyword:
                    Append(builder, token.ValueText);
                    break;
                default:
                    modifiers = string.Empty;
                    return false;
            }

        modifiers = builder.ToString();
        return true;
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0) builder.Append(' ');

        builder.Append(value);
    }
}
