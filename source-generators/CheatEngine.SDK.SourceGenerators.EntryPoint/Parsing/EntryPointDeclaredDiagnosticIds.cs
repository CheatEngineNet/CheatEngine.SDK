using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Parsing;

/// <summary>
///     Finds the diagnostic IDs a plugin class declares for its own use: <c>[Experimental("ID")]</c> and
///     <c>[Obsolete(DiagnosticId = "ID")]</c> on the class, on the types it is nested in and on its parameterless
///     selected real parameterless constructor, that is on every symbol the generated <c>new global::Outer.Plugin()</c>
///     names.
/// </summary>
/// <remarks>
///     <para>
///         The compiler reports such an ID at every use outside the marked symbol, the generated factory included, in a
///         file the author cannot edit: an experimental ID is an error by default, a custom obsolete ID is not covered by
///         the fixed <c>CS0612, CS0618</c> pragma. Both are warnings as far as <c>#pragma warning disable</c> is
///         concerned, so the emitter disables exactly these IDs. <c>[Obsolete(..., error: true)]</c> is a real error and
///         stays one. Attributes are compared to the BCL symbols resolved for this compilation, never by a user-controlled
///         name/namespace spelling.
///     </para>
///     <para>
///         Named <c>EntryPointDeclaredDiagnosticIds</c>, not the bare <c>DeclaredDiagnosticIds</c>, so it greps
///         unambiguously against the unrelated, identically-shaped
///         <c>CheatEngine.SDK.SourceGenerators.LuaBindings.Parsing.LuaBindingsDeclaredDiagnosticIds</c>: each generator's own,
///         not-shared, Roslyn-touching parsing code (see that type's remarks).
///     </para>
/// </remarks>
internal static class EntryPointDeclaredDiagnosticIds
{
    private const string Separator = ", ";

    /// <summary>
    ///     The IDs, outermost type first and constructor last, without duplicates, joined with <c>", "</c>: the operand
    ///     of a <c>#pragma warning disable</c>. Empty when the class declares none. Never throws on malformed attributes.
    /// </summary>
    public static string Collect(
        INamedTypeSymbol type,
        IMethodSymbol? parameterlessConstructor,
        INamedTypeSymbol? experimentalAttribute,
        INamedTypeSymbol? obsoleteAttribute)
    {
        List<string>? ids = null;
        CollectFromTypeAndContainers(type, experimentalAttribute, obsoleteAttribute, ref ids);

        if (parameterlessConstructor is not null)
            CollectFrom(
                parameterlessConstructor.GetAttributes(),
                experimentalAttribute,
                obsoleteAttribute,
                ref ids);

        return ids is null ? string.Empty : string.Join(Separator, ids);
    }

    private static void CollectFromTypeAndContainers(
        INamedTypeSymbol type,
        INamedTypeSymbol? experimentalAttribute,
        INamedTypeSymbol? obsoleteAttribute,
        ref List<string>? ids)
    {
        if (type.ContainingType is { } containing)
            CollectFromTypeAndContainers(containing, experimentalAttribute, obsoleteAttribute, ref ids);

        CollectFrom(type.GetAttributes(), experimentalAttribute, obsoleteAttribute, ref ids);
    }

    private static void CollectFrom(
        ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol? experimentalAttribute,
        INamedTypeSymbol? obsoleteAttribute,
        ref List<string>? ids)
    {
        foreach (var attribute in attributes)
            if (ReadDeclaredId(attribute, experimentalAttribute, obsoleteAttribute) is { } id
                && IsUsableInPragma(id)
                && (ids is null || !ids.Contains(id)))
                (ids ??= []).Add(id);
    }

    private static string? ReadDeclaredId(
        AttributeData attribute,
        INamedTypeSymbol? experimentalAttribute,
        INamedTypeSymbol? obsoleteAttribute)
    {
        if (experimentalAttribute is not null
            && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, experimentalAttribute))
            // [Experimental(string diagnosticId)]
            return attribute.ConstructorArguments.Length == 1
                   && attribute.ConstructorArguments[0] is
                       { Kind: TypedConstantKind.Primitive, Value: string experimentalId }
                ? experimentalId
                : null;

        if (obsoleteAttribute is not null
            && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute))
            // [Obsolete(..., DiagnosticId = "ID")]
            foreach (var argument in attribute.NamedArguments)
                if (string.Equals(argument.Key, "DiagnosticId", System.StringComparison.Ordinal)
                    && argument.Value is { Kind: TypedConstantKind.Primitive, Value: string obsoleteId })
                    return obsoleteId;

        return null;
    }

    // The ID goes into a '#pragma warning disable' line as it is, so it must be one identifier token there: no
    // white space, line break or comment marker (text injection), no C# or preprocessor keyword. The compiler already
    // rejects an [Experimental] ID that is not an identifier (CS9211); whatever is dropped here stays a loud error.
    private static bool IsUsableInPragma(string id)
    {
        return SyntaxFacts.IsValidIdentifier(id)
               && SyntaxFacts.GetKeywordKind(id) == SyntaxKind.None
               && SyntaxFacts.GetPreprocessorKeywordKind(id) == SyntaxKind.None;
    }
}
