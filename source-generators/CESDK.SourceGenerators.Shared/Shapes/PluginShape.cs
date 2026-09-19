using Microsoft.CodeAnalysis;

namespace CESDK.SourceGenerators.Shared.Shapes;

/// <summary>
///     Decides whether the generated entry point can construct a <c>[CheatEnginePlugin]</c> class, that is whether
///     <c>new global::&lt;type&gt;()</c> compiles inside a top-level type of the same assembly, in another file, and
///     yields a <c>CESDK.Hosting.Plugin.CheatEnginePlugin</c>. "Compiles" includes the two errors that no accessibility
///     check
///     finds: CS9035 (required members without an object initializer) and CS0619 (<c>[Obsolete]</c> as an error).
/// </summary>
/// <remarks>
///     <para>
///         The single place the rule lives: both <c>CESDK.SourceGenerators.EntryPoint</c>
///         (which only tests <see cref="PluginShapeIssues.None" /> to decide whether to emit) and analyzer rule CESDK0001
///         in
///         <c>CESDK.Analyzers</c> (which explains every flag that is set) call this type instead of keeping their own
///         copy.
///     </para>
///     <para>
///         Pure function of the symbols it is given: no compilation, no syntax, no state, so a generator's incremental
///         pipeline can call it from inside a per-node transform without combining with anything wider. Never throws on a
///         malformed (error) symbol. <see cref="PluginShapeIssues.NotDerivedFromPluginBase" /> is decided structurally
///         (name
///         and namespace of the base-type chain), not by resolving <c>CESDK.Hosting.Plugin.CheatEnginePlugin</c> once and
///         comparing
///         symbols: <c>Compilation.GetTypeByMetadataName</c> returns <see langword="null" /> when two references define
///         the
///         type, and a decision that depends on nothing but the given symbol is robust to that. The two attribute symbols
///         are still resolved by the caller (there is no name/namespace-only way to recognise a BCL attribute safely):
///         pass
///         <see langword="null" /> when a compilation has none, which reads as "no constructor sets required members" /
///         "nothing is obsolete-as-error".
///     </para>
/// </remarks>
internal static class PluginShape
{
    // Namespace and type name of the entry point that Cheat Engine looks up: 'CESDK.CESDK'.
    private const string ReservedName = "CESDK";

    // The plugin base class is CESDK.Hosting.Plugin.CheatEnginePlugin: type name, then the namespace segments, innermost first.
    private const string PluginBaseName = "CheatEnginePlugin";
    private const string HostingNamespaceName = "Hosting";
    private const string PluginNamespaceName = "Plugin";

    /// <summary>Inspects <paramref name="type" />; never throws on malformed (error) symbols.</summary>
    /// <param name="type">A class that carries the plugin attribute.</param>
    /// <param name="attribute">
    ///     The application of the plugin attribute on <paramref name="type" />, or <see langword="null" /> when the
    ///     caller could not find one (read as "no usable name").
    /// </param>
    /// <param name="setsRequiredMembersAttribute">
    ///     The resolved <c>System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute</c>, or <see langword="null" />
    ///     when the compilation has none, in which case no constructor counts as setting the required members.
    /// </param>
    /// <param name="obsoleteAttribute">
    ///     The resolved <c>System.ObsoleteAttribute</c>, or <see langword="null" /> when the compilation has none, in
    ///     which case obsolete errors are not looked for.
    /// </param>
    /// <param name="displayName">
    ///     The attribute's name argument, verbatim; empty when it is missing, not a constant string, or the argument
    ///     list does not have exactly one argument.
    /// </param>
    public static PluginShapeIssues Inspect(
        INamedTypeSymbol type,
        AttributeData? attribute,
        INamedTypeSymbol? setsRequiredMembersAttribute,
        INamedTypeSymbol? obsoleteAttribute,
        out string displayName)
    {
        displayName = ReadDisplayName(attribute);
        var issues = string.IsNullOrWhiteSpace(displayName) ? PluginShapeIssues.InvalidName : PluginShapeIssues.None;

        if (type.IsStatic)
            // A static class is also abstract and sealed in metadata, has no base class and no instance
            // constructor: one message instead of four.
            return issues | PluginShapeIssues.Static;

        if (type.IsAbstract) issues |= PluginShapeIssues.Abstract;

        if (type.Arity > 0) issues |= PluginShapeIssues.Generic;

        if (type.ContainingType is { IsGenericType: true }) issues |= PluginShapeIssues.NestedInGeneric;

        if (!DerivesFromPluginBase(type)) issues |= PluginShapeIssues.NotDerivedFromPluginBase;

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAssemblyWide(current.DeclaredAccessibility)) issues |= PluginShapeIssues.Inaccessible;

            if (current.IsFileLocal) issues |= PluginShapeIssues.FileLocal;

            // 'new global::Outer.Plugin()' names every type of the chain.
            if (IsObsoleteError(current, obsoleteAttribute)) issues |= PluginShapeIssues.ObsoleteError;
        }

        if (IsOrIsNestedInEntryPointType(type)) issues |= PluginShapeIssues.ReservedEntryPointName;

        return issues | InspectConstructors(type, setsRequiredMembersAttribute, obsoleteAttribute);
    }

    // A missing or mistyped argument is a compiler error already (CS7036, CS1503); a well-formed but unusable name
    // (null, empty, white space) is what the shape check reports.
    private static string ReadDisplayName(AttributeData? attribute)
    {
        if (attribute is null || attribute.ConstructorArguments.Length != 1) return string.Empty;

        var name = attribute.ConstructorArguments[0];
        return name is { Kind: TypedConstantKind.Primitive, Value: string text } ? text : string.Empty;
    }

    // The generated file declares the top-level type 'CESDK.CESDK' (the host looks it up by that name). An outermost
    // type of the author with that very name would make it a second declaration (CS0101), so the class cannot be
    // constructed from a generated file. Nothing wider is reserved: 'Demo.CESDK' and 'CESDK.Samples.CESDK' are other types.
    private static bool IsOrIsNestedInEntryPointType(INamedTypeSymbol type)
    {
        var outermost = type;
        while (outermost.ContainingType is { } containing) outermost = containing;

        return outermost is
        {
            Name: ReservedName, Arity: 0,
            ContainingNamespace: { Name: ReservedName, ContainingNamespace.IsGlobalNamespace: true }
        };
    }

    private static bool DerivesFromPluginBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (IsPluginBase(current))
                return true;

        return false;
    }

    private static bool IsPluginBase(INamedTypeSymbol type)
    {
        return type is
        {
            Name: PluginBaseName, Arity: 0, ContainingType: null, ContainingNamespace:
            {
                Name: PluginNamespaceName,
                ContainingNamespace:
                {
                    Name: HostingNamespaceName,
                    ContainingNamespace: { Name: ReservedName, ContainingNamespace.IsGlobalNamespace: true }
                }
            }
        };
    }

    private static PluginShapeIssues InspectConstructors(
        INamedTypeSymbol type,
        INamedTypeSymbol? setsRequiredMembersAttribute,
        INamedTypeSymbol? obsoleteAttribute)
    {
        // 'new T()' binds to any constructor callable with an empty argument list, not only a literally
        // parameterless one (every parameter optional, or a trailing 'params'); more than one such constructor can
        // legally coexist (e.g. 'P()' and 'P(int x = 0)'). An inaccessible one is not even a candidate for a caller
        // outside the class, so an accessible candidate (if any exists) is always what the compiler binds to,
        // regardless of what else is declared; only when every candidate is inaccessible does that reason surface.
        IMethodSymbol? accessible = null;
        IMethodSymbol? inaccessible = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!IsCallableWithNoArguments(constructor)) continue;

            // The implicit constructor is public, except on an abstract class (protected), which is reported as
            // Abstract: once 'abstract' is gone the implicit constructor is public again.
            if (constructor.IsImplicitlyDeclared || IsAssemblyWide(constructor.DeclaredAccessibility))
                accessible ??= constructor;
            else
                inaccessible ??= constructor;
        }

        var chosen = accessible ?? inaccessible;
        var issues = chosen switch
        {
            null => PluginShapeIssues.MissingParameterlessConstructor,
            _ when accessible is null => PluginShapeIssues.InaccessibleParameterlessConstructor,
            _ => PluginShapeIssues.None
        };

        var setsRequiredMembers = false;
        if (chosen is not null)
        {
            if (IsObsoleteError(chosen, obsoleteAttribute)) issues |= PluginShapeIssues.ObsoleteError;

            setsRequiredMembers = HasAttribute(chosen, setsRequiredMembersAttribute);
        }

        // 'new T()' has no object initializer: required members make it CS9035 unless the constructor it binds to
        // promises to set them. Reported next to a missing constructor too: adding a plain one would not be enough.
        if (!setsRequiredMembers && HasRequiredMembers(type)) issues |= PluginShapeIssues.RequiredMembers;

        return issues;
    }

    // A parameter binds without a supplied argument when it has a default value ('optional') or, being the
    // trailing parameter, is 'params' (an empty argument list binds it to an empty array). C# requires every
    // optional parameter to follow all required ones and 'params' to be the very last parameter, so this needs no
    // look-ahead.
    private static bool IsCallableWithNoArguments(IMethodSymbol constructor)
    {
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            if (parameter.IsOptional || (parameter.IsParams && i == constructor.Parameters.Length - 1)) continue;

            return false;
        }

        return true;
    }

    // Required members are inherited: the whole base-class chain counts.
    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                    return true;

        return false;
    }

    // [Obsolete(message, error: true)]: the only constructor of the attribute with a second argument.
    private static bool IsObsoleteError(ISymbol symbol, INamedTypeSymbol? obsoleteAttribute)
    {
        if (obsoleteAttribute is null) return false;

        foreach (var attribute in symbol.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute)
                && attribute.ConstructorArguments is [_, { Value: true } _])
                return true;

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null) return false;

        foreach (var attribute in symbol.GetAttributes())
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeClass))
                return true;

        return false;
    }

    // The generated factory is a top-level type of the same assembly with no inheritance relation to the plugin:
    // 'internal' is the minimum at every nesting level, and 'protected internal' grants it.
    private static bool IsAssemblyWide(Accessibility accessibility)
    {
        return accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }
}
