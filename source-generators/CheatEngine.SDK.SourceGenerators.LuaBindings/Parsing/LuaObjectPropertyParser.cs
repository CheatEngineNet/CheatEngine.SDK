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
    public static LuaObjectPropertyModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
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
                    && LuaClassParser.IsGeneratedHandle(property.ContainingType, compilation, cancellationToken)
                    && described;

        if (valid)
            return model with { LuaName = luaName!, IsValid = true };

        return new LuaObjectPropertyModel(
            ContainingTypeParser.Parse(property.ContainingType),
            luaName ?? string.Empty,
            string.Empty,
            Identifiers.Escape(property.Name),
            default,
            false,
            false,
            false,
            property.Name,
            false);
    }

    private static bool TryDescribe(IPropertySymbol property, PropertyDeclarationSyntax? declaration,
        out LuaObjectPropertyModel model)
    {
        var kind = default(LuaValueKind);
        var isNullable = false;
        var typeIsSupported = LuaValueKindMapper.TryMap(property.Type, out kind, out isNullable)
                              && LuaValueKinds.CanBeResult(kind);
        var valid = declaration is not null
                    && !property.IsStatic
                    && !property.IsIndexer
                    && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && declaration.AccessorList is not null
                    && property.PartialImplementationPart is null
                    && typeIsSupported;
        var hasGetter = false;
        var hasSetter = false;

        if (declaration?.AccessorList is { } accessorList)
        {
            foreach (var accessor in accessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null) valid = false;

                switch (accessor.Kind())
                {
                    case SyntaxKind.GetAccessorDeclaration:
                        hasGetter = true;
                        break;
                    case SyntaxKind.SetAccessorDeclaration:
                        hasSetter = true;
                        break;
                    default:
                        valid = false;
                        break;
                }
            }
        }

        if (!hasGetter && !hasSetter) valid = false;

        model = new LuaObjectPropertyModel(
            ContainingTypeParser.Parse(property.ContainingType),
            string.Empty,
            Modifiers(declaration),
            Identifiers.Escape(property.Name),
            kind,
            isNullable,
            hasGetter,
            hasSetter,
            property.Name,
            valid);
        return valid;
    }

    private static string Modifiers(PropertyDeclarationSyntax? declaration)
    {
        StringBuilder modifiers = new();
        var isNew = false;
        var isUnsafe = false;
        if (declaration is not null)
            foreach (var token in declaration.Modifiers)
                switch (token.Kind())
                {
                    case SyntaxKind.PublicKeyword:
                    case SyntaxKind.InternalKeyword:
                    case SyntaxKind.ProtectedKeyword:
                    case SyntaxKind.PrivateKeyword:
                        Append(modifiers, token.ValueText);
                        break;
                    case SyntaxKind.NewKeyword:
                        isNew = true;
                        break;
                    case SyntaxKind.ReadOnlyKeyword:
                        Append(modifiers, token.ValueText);
                        break;
                    case SyntaxKind.UnsafeKeyword:
                        isUnsafe = true;
                        break;
                }

        if (isNew) Append(modifiers, "new");

        if (isUnsafe) Append(modifiers, "unsafe");

        Append(modifiers, "partial");
        return modifiers.ToString();
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0) builder.Append(' ');

        builder.Append(value);
    }
}
