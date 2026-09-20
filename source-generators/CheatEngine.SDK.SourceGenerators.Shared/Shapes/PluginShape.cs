using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.Shapes;

/// <summary>
///     Decides whether the generated entry point can construct a <c>[CheatEnginePlugin]</c> class, that is whether
///     <c>new global::&lt;type&gt;()</c> compiles inside a top-level type of the same assembly, in another file, and
///     yields the supplied <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c> symbol. "Compiles" includes the two
///     errors that no accessibility
///     check
///     finds: CS9035 (required members without an object initializer) and CS0619 (<c>[Obsolete]</c> as an error).
/// </summary>
/// <remarks>
///     <para>
///         The single place the rule lives: both <c>CheatEngine.SDK.SourceGenerators.EntryPoint</c>
///         (which only tests <see cref="PluginShapeIssues.None" /> to decide whether to emit) and analyzer rule CESDK0001
///         in
///         <c>CheatEngine.SDK.Analyzers</c> (which explains every flag that is set) call this type instead of keeping
///         their own
///         copy.
///     </para>
///     <para>
///         Pure function of the symbols it is given: no compilation, no syntax, no state, so a generator's incremental
///         pipeline can call it from inside a per-node transform without combining with anything wider. Never throws on a
///         malformed (error) symbol. The preferred overload receives the resolved SDK plugin-base symbol and compares
///         symbols, rather than accepting a same-named type from another assembly. The compatibility overload keeps the
///         old structural fallback until all consumers pass that symbol. The BCL attribute symbols are also resolved by
///         the caller: pass <see langword="null" /> when a compilation has none, which reads as "no constructor sets
///         required members" / "nothing is obsolete-as-error".
///     </para>
/// </remarks>
[SuppressMessage(
    "Meziantou.Analyzer",
    "MA0182",
    Justification =
        "This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class PluginShape
{
    // Namespace and type name of the entry point that Cheat Engine looks up: 'CESDK.CESDK'.
    private const string ReservedName = "CESDK";

    // The fallback spelling of the plugin base type, retained only for consumers not yet upgraded to the symbol-aware
    // overload. New consumers must pass the actual SDK assembly symbol.
    private const string PluginBaseName = "CheatEnginePlugin";
    private const string HostingNamespaceName = "Hosting";
    private const string PluginNamespaceName = "Plugin";
    private const string SdkNamespaceName = "SDK";
    private const string SdkRootNamespaceName = "CheatEngine";

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
        return Inspect(
            type,
            attribute,
            pluginBase: null,
            setsRequiredMembersAttribute,
            obsoleteAttribute,
            out displayName,
            out _);
    }

    /// <summary>
    ///     Inspects <paramref name="type" /> against the actual SDK plugin-base symbol and returns the exact
    ///     zero-parameter constructor the generated <c>new T()</c> expression names.
    /// </summary>
    /// <param name="type">The class carrying the SDK plugin marker.</param>
    /// <param name="attribute">The marker application on <paramref name="type" />, or <see langword="null" />.</param>
    /// <param name="pluginBase">
    ///     The resolved <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c> symbol. When <see langword="null" />,
    ///     the legacy structural fallback is used only for compatibility with consumers not yet upgraded to this
    ///     overload.
    /// </param>
    /// <param name="setsRequiredMembersAttribute">
    ///     The resolved BCL <c>SetsRequiredMembersAttribute</c>, or <see langword="null" /> when unavailable.
    /// </param>
    /// <param name="obsoleteAttribute">
    ///     The resolved BCL <c>ObsoleteAttribute</c>, or <see langword="null" /> when unavailable.
    /// </param>
    /// <param name="displayName">The marker's display name; empty when its argument is unusable.</param>
    /// <param name="parameterlessConstructor">
    ///     The actual zero-parameter instance constructor selected by the generated expression, including an implicit
    ///     constructor; otherwise <see langword="null" />. Constructors whose parameters are optional or
    ///     <see langword="params" /> are deliberately not selected.
    /// </param>
    public static PluginShapeIssues Inspect(
        INamedTypeSymbol type,
        AttributeData? attribute,
        INamedTypeSymbol? pluginBase,
        INamedTypeSymbol? setsRequiredMembersAttribute,
        INamedTypeSymbol? obsoleteAttribute,
        out string displayName,
        out IMethodSymbol? parameterlessConstructor)
    {
        displayName = ReadDisplayName(attribute);
        var issues = string.IsNullOrWhiteSpace(displayName) ? PluginShapeIssues.InvalidName : PluginShapeIssues.None;
        parameterlessConstructor = null;

        if (type.IsStatic)
            // A static class is also abstract and sealed in metadata, has no base class and no instance
            // constructor: one message instead of four.
            return issues | PluginShapeIssues.Static;

        if (type.IsAbstract) issues |= PluginShapeIssues.Abstract;

        if (type.Arity > 0) issues |= PluginShapeIssues.Generic;

        if (type.ContainingType is { IsGenericType: true }) issues |= PluginShapeIssues.NestedInGeneric;

        if (!DerivesFromPluginBase(type, pluginBase)) issues |= PluginShapeIssues.NotDerivedFromPluginBase;

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAssemblyWide(current.DeclaredAccessibility)) issues |= PluginShapeIssues.Inaccessible;

            if (current.IsFileLocal) issues |= PluginShapeIssues.FileLocal;

            // 'new global::Outer.Plugin()' names every type of the chain.
            if (IsObsoleteError(current, obsoleteAttribute)) issues |= PluginShapeIssues.ObsoleteError;
        }

        if (IsOrIsNestedInEntryPointType(type)) issues |= PluginShapeIssues.ReservedEntryPointName;

        return issues | InspectConstructors(
            type,
            setsRequiredMembersAttribute,
            obsoleteAttribute,
            out parameterlessConstructor);
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

    private static bool DerivesFromPluginBase(INamedTypeSymbol type, INamedTypeSymbol? pluginBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (pluginBase is null
                    ? IsPluginBaseFallback(current)
                    : SymbolEqualityComparer.Default.Equals(current, pluginBase))
                return true;

        return false;
    }

    private static bool IsPluginBaseFallback(INamedTypeSymbol type)
    {
        return type is
        {
            Name: PluginBaseName, Arity: 0, ContainingType: null, ContainingNamespace:
            {
                Name: PluginNamespaceName,
                ContainingNamespace:
                {
                    Name: HostingNamespaceName,
                    ContainingNamespace:
                    {
                        Name: SdkNamespaceName,
                        ContainingNamespace:
                        {
                            Name: SdkRootNamespaceName,
                            ContainingNamespace.IsGlobalNamespace: true,
                        },
                    }
                }
            }
        };
    }

    private static PluginShapeIssues InspectConstructors(
        INamedTypeSymbol type,
        INamedTypeSymbol? setsRequiredMembersAttribute,
        INamedTypeSymbol? obsoleteAttribute,
        out IMethodSymbol? parameterlessConstructor)
    {
        // The generated factory has an explicit contract: it invokes a real parameterless constructor. C# permits an
        // empty argument list to bind to optional or params parameters, but accepting that broadens a construction
        // contract that cannot be represented in the generated factory's documentation or lifecycle model. An
        // implicitly declared zero-parameter constructor is a real constructor and is accepted.
        IMethodSymbol? accessible = null;
        IMethodSymbol? inaccessible = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!constructor.Parameters.IsEmpty) continue;

            // The implicit constructor is public, except on an abstract class (protected), which is reported as
            // Abstract: once 'abstract' is gone the implicit constructor is public again.
            if (constructor.IsImplicitlyDeclared || IsAssemblyWide(constructor.DeclaredAccessibility))
                accessible ??= constructor;
            else
                inaccessible ??= constructor;
        }

        parameterlessConstructor = accessible ?? inaccessible;
        var issues = parameterlessConstructor switch
        {
            null => PluginShapeIssues.MissingParameterlessConstructor,
            _ when accessible is null => PluginShapeIssues.InaccessibleParameterlessConstructor,
            _ => PluginShapeIssues.None
        };

        var setsRequiredMembers = false;
        if (parameterlessConstructor is not null)
        {
            if (IsObsoleteError(parameterlessConstructor, obsoleteAttribute)) issues |= PluginShapeIssues.ObsoleteError;

            setsRequiredMembers = HasAttribute(parameterlessConstructor, setsRequiredMembersAttribute);
        }

        // 'new T()' has no object initializer: required members make it CS9035 unless the constructor it binds to
        // promises to set them. Reported next to a missing constructor too: adding a plain one would not be enough.
        if (!setsRequiredMembers && HasRequiredMembers(type)) issues |= PluginShapeIssues.RequiredMembers;

        return issues;
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
