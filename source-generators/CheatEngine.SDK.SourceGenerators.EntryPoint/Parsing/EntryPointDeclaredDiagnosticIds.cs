using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Parsing;

/// <summary>
///     Finds the diagnostic IDs a plugin class declares for its own use: <c>[Experimental("ID")]</c> and
///     <c>[Obsolete(DiagnosticId = "ID")]</c> on the class, on the types it is nested in and on its parameterless
///     constructor, that is on every symbol the generated <c>new global::Outer.Plugin()</c> names.
/// </summary>
/// <remarks>
///     <para>
///         The compiler reports such an ID at every use outside the marked symbol, the generated factory included, in a
///         file the author cannot edit: an experimental ID is an error by default, a custom obsolete ID is not covered by
///         the fixed <c>CS0612, CS0618</c> pragma. Both are warnings as far as <c>#pragma warning disable</c> is
///         concerned, so the emitter disables exactly these IDs. <c>[Obsolete(..., error: true)]</c> is a real error and
///         stays one. Attributes are recognised by name and namespace, like the plugin base class (see
///         <see cref="PluginShape" />).
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
    public static string Collect(INamedTypeSymbol type)
    {
        List<string>? ids = null;
        CollectFromTypeAndContainers(type, ref ids);

        foreach (var constructor in type.InstanceConstructors)
            if (constructor.Parameters.IsEmpty)
            {
                CollectFrom(constructor.GetAttributes(), ref ids);
                break;
            }

        return ids is null ? string.Empty : string.Join(Separator, ids);
    }

    private static void CollectFromTypeAndContainers(INamedTypeSymbol type, ref List<string>? ids)
    {
        if (type.ContainingType is { } containing) CollectFromTypeAndContainers(containing, ref ids);

        CollectFrom(type.GetAttributes(), ref ids);
    }

    private static void CollectFrom(ImmutableArray<AttributeData> attributes, ref List<string>? ids)
    {
        foreach (var attribute in attributes)
            if (ReadDeclaredId(attribute) is { } id && IsUsableInPragma(id) && (ids is null || !ids.Contains(id)))
                (ids ??= []).Add(id);
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
