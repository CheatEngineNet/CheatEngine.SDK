using System.Diagnostics.CodeAnalysis;
using System.Threading;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>
///     Decides whether a generated partial part can be added to a type: the type and every type it is nested in must
///     be a partial, non-generic class, struct or record that is not <see langword="file" />-local. Symbols in, flags
///     out, no generator types: the CESDK2xxx analyzer links this file.
/// </summary>
[SuppressMessage(
    "Meziantou.Analyzer",
    "MA0182",
    Justification =
        "This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class ContainingTypeShape
{
    /// <summary>Inspects <paramref name="type" /> and its containing types; never throws on malformed symbols.</summary>
    public static ContainingTypeIssues Inspect(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var issues = ContainingTypeIssues.None;
        for (var current = type; current is not null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (current.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                issues |= ContainingTypeIssues.NotClassOrStruct;

            // True for a non-generic type nested in a generic one as well.
            if (current.IsGenericType) issues |= ContainingTypeIssues.Generic;

            if (current.IsFileLocal) issues |= ContainingTypeIssues.FileLocal;

            if (!IsPartial(current, cancellationToken)) issues |= ContainingTypeIssues.NotPartial;
        }

        return issues;
    }

    // A type is partial when a declaration says so; the compiler reports a part without the modifier (CS0260)
    // louder than any silence here could.
    private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;

        return false;
    }
}
