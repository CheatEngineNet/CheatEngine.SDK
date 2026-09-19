using System.Text;
using System.Threading;
using CESDK.SourceGenerators.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaBindings.Parsing;
using CESDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CESDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>
///     The <c>ForAttributeWithMetadataName</c> transform for <c>[LuaGlobal]</c> on a method: the only place of that
///     pipeline that touches symbols or syntax. It reduces the attributed declaration to a <see cref="LuaGlobalModel" />.
/// </summary>
internal static class LuaGlobalParser
{
    /// <summary>Builds the model of one attributed method declaration.</summary>
    public static LuaGlobalModel Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = (IMethodSymbol)context.TargetSymbol;
        var luaName = AttributeArguments.ReadName(context.Attributes);

        var issues = LuaGlobalShape.Inspect(method, out var signature);
        if (!LuaNames.IsValidName(luaName)) issues |= LuaGlobalShapeIssues.InvalidName;

        var typeIssues = ContainingTypeShape.Inspect(method.ContainingType, cancellationToken);
        var containingType = ContainingTypeParser.Parse(method.ContainingType);

        LuaGlobalCallModel? call = null;
        if (issues == LuaGlobalShapeIssues.None && typeIssues == ContainingTypeIssues.None)
            call = new LuaGlobalCallModel(
                luaName!,
                LuaGlobalCallModel.CacheFieldFor(luaName!),
                Modifiers(context.TargetNode as MethodDeclarationSyntax),
                Identifiers.Escape(method.Name),
                signature.StateParameterName,
                signature.Arguments,
                signature.Form,
                signature.Results,
                signature.ReturnKind,
                signature.ReturnIsNullable,
                method.IsExtensionMethod);

        return new LuaGlobalModel(containingType, typeIssues, issues, call, SortKey(method));
    }

    // The implementing declaration must repeat the defining declaration's accessibility, 'new', 'static' and
    // 'unsafe' exactly (CS8799, CS0763, CS0764), and must not add an accessibility to an old-style partial method
    // that has none. Read from the syntax, in a canonical order, 'partial' last.
    private static string Modifiers(MethodDeclarationSyntax? declaration)
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
                    case SyntaxKind.UnsafeKeyword:
                        isUnsafe = true;
                        break;
                }

        if (isNew) Append(modifiers, "new");

        Append(modifiers, "static");
        if (isUnsafe) Append(modifiers, "unsafe");

        Append(modifiers, "partial");
        return modifiers.ToString();
    }

    private static void Append(StringBuilder modifiers, string modifier)
    {
        if (modifiers.Length > 0) modifiers.Append(' ');

        modifiers.Append(modifier);
    }

    // Name and parameter types: overloads of one name (a copy-out and a string form of the same global) must
    // still sort deterministically.
    private static string SortKey(IMethodSymbol method)
    {
        StringBuilder key = new(method.Name);
        key.Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0) key.Append(", ");

            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None) key.Append(parameter.RefKind.ToString()).Append(' ');

            key.Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return key.Append(')').ToString();
    }
}
