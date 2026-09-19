using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.LuaBindings.Parsing;

/// <summary>
///     Finds the diagnostic IDs a <c>[LuaFunction]</c> target declares for its own use: <c>[Experimental("ID")]</c> and
///     <c>[Obsolete(DiagnosticId = "ID")]</c> on the method itself and on the types it is nested in, that is on every
///     symbol the generated call site <c>global::Outer.Target(...)</c> names.
/// </summary>
/// <remarks>
///     <para>
///         The compiler reports such an ID at every use outside the marked symbol, the generated thunk's call included,
///         in a file the author cannot edit: an experimental ID is an error by default, a custom obsolete ID is not
///         covered by the fixed <c>CS0612, CS0618</c> pragma <see cref="Emit.LuaFunctionFileEmitter" /> always writes.
///         Both are warnings as far as <c>#pragma warning disable</c> is concerned, so the emitter disables exactly these
///         IDs, gathered from every bindable member of the type. <c>[Obsolete(..., error: true)]</c> is a real error and
///         stays one. Attributes are recognised by name and namespace, mirroring
///         <c>CESDK.SourceGenerators.EntryPoint.Parsing.EntryPointDeclaredDiagnosticIds</c>
///         (not shared: each generator's parsing code is its own, Roslyn-touching layer).
///     </para>
///     <para>
///         Named <c>LuaBindingsDeclaredDiagnosticIds</c>, not the bare <c>DeclaredDiagnosticIds</c>, so it greps
///         unambiguously against that unrelated, identically-shaped type.
///     </para>
/// </remarks>
internal static class LuaBindingsDeclaredDiagnosticIds
{
    private const string Separator = ", ";

    /// <summary>
    ///     The IDs of <paramref name="method" /> and the types it is nested in, outermost first, without duplicates,
    ///     joined with <c>", "</c>: the operand of a <c>#pragma warning disable</c>. Empty when none are declared.
    ///     Never throws on malformed attributes.
    /// </summary>
    public static string Collect(IMethodSymbol method)
    {
        var ids = CollectFrom(method.GetAttributes(), null);
        if (method.ContainingType is { } containing) ids = CollectFromTypeAndContainers(containing, ids);

        return ids is null ? string.Empty : string.Join(Separator, ids);
    }

    // The list is created on the first ID and threaded through the return value, so a member that declares none
    // allocates nothing.
    private static List<string>? CollectFromTypeAndContainers(INamedTypeSymbol type, List<string>? ids)
    {
        if (type.ContainingType is { } containing) ids = CollectFromTypeAndContainers(containing, ids);

        return CollectFrom(type.GetAttributes(), ids);
    }

    private static List<string>? CollectFrom(ImmutableArray<AttributeData> attributes, List<string>? ids)
    {
        foreach (var attribute in attributes)
            if (ReadDeclaredId(attribute) is { } id && IsUsableInPragma(id) && (ids is null || !ids.Contains(id)))
                (ids ??= []).Add(id);

        return ids;
    }

    private static string? ReadDeclaredId(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { Arity: 0, ContainingType: null } attributeClass) return null;

        if (string.Equals(attributeClass.Name, "ExperimentalAttribute", StringComparison.Ordinal))
            // [Experimental(string diagnosticId)]
            return IsNamespace(attributeClass.ContainingNamespace, "System", "Diagnostics", "CodeAnalysis")
                   && attribute.ConstructorArguments.Length == 1
                   && attribute.ConstructorArguments[0] is
                       { Kind: TypedConstantKind.Primitive, Value: string experimentalId }
                ? experimentalId
                : null;

        if (string.Equals(attributeClass.Name, "ObsoleteAttribute", StringComparison.Ordinal)
            && IsNamespace(attributeClass.ContainingNamespace, "System"))
            // [Obsolete(..., DiagnosticId = "ID")]
            foreach (var argument in attribute.NamedArguments)
                if (string.Equals(argument.Key, "DiagnosticId", StringComparison.Ordinal)
                    && argument.Value is { Kind: TypedConstantKind.Primitive, Value: string obsoleteId })
                    return obsoleteId;

        return null;
    }

    // Innermost name last: IsNamespace(ns, "System", "Diagnostics") matches 'System.Diagnostics'.
    private static bool IsNamespace(INamespaceSymbol? @namespace, params string[] names)
    {
        for (var i = names.Length - 1; i >= 0; i--)
        {
            if (@namespace is null || !string.Equals(@namespace.Name, names[i], StringComparison.Ordinal)) return false;

            @namespace = @namespace.ContainingNamespace;
        }

        return @namespace is { IsGlobalNamespace: true };
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
