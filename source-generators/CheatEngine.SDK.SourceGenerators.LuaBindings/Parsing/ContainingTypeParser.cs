using System.Collections.Generic;
using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>
///     Reduces the type that declares a binding to the strings the file emitter needs (
///     <see cref="ContainingTypeModel" />).
/// </summary>
internal static class ContainingTypeParser
{
    // Namespace as written in the generated file: no 'global::' (a namespace declaration cannot carry it), keyword
    // parts escaped.
    private static readonly SymbolDisplayFormat NamespaceFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    // The dotted name the hint name is derived from: plain identifiers, no escapes, no 'global::'.
    private static readonly SymbolDisplayFormat HintFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    /// <summary>
    ///     Builds the model of <paramref name="type" />. Works on any type, valid or not; validity is
    ///     <see cref="ContainingTypeShape" />'s business.
    /// </summary>
    public static ContainingTypeModel Parse(INamedTypeSymbol type)
    {
        List<TypeDeclarationModel> chain = [];
        for (var current = type; current is not null; current = current.ContainingType)
            chain.Add(new TypeDeclarationModel(Keyword(current), Identifiers.Escape(current.Name),
                IsReadOnly(current)));

        chain.Reverse();

        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToDisplayString(NamespaceFormat)
            : string.Empty;

        return new ContainingTypeModel(
            ns,
            new EquatableArray<TypeDeclarationModel>(ImmutableArray.CreateRange(chain)),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(HintFormat));
    }

    // The keyword a further part must repeat. Interfaces and the like are rejected by the shape; 'class' is a
    // harmless fallback that keeps the parser total.
    private static string Keyword(INamedTypeSymbol type)
    {
        return (type.TypeKind, type.IsRecord) switch
        {
            (TypeKind.Struct, true) => "record struct",
            (TypeKind.Struct, false) => "struct",
            (_, true) => "record",
            _ => "class"
        };
    }

    // Do not use a symbol-name heuristic: a partial readonly struct has the modifier on every declaration, and the
    // generated part must repeat it. The syntax walk keeps this code compatible with the netstandard Roslyn host.
    private static bool IsReadOnly(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                return true;

        return false;
    }
}
