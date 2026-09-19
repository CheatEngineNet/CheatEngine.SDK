using System;

namespace CheatEngine.SDK.SourceGenerators.Shared.Shapes;

/// <summary>
///     Every independent reason a class marked <c>[CheatEnginePlugin]</c> cannot be constructed by the generated entry
///     point: <c>new global::&lt;type&gt;()</c>, evaluated from a top-level type of the same assembly, in another file,
///     must compile without an object initializer and must yield a <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c>.
/// </summary>
/// <remarks>
///     The single source of truth for both consumers of <see cref="PluginShape" />: the
///     <c>CheatEngine.SDK.SourceGenerators.EntryPoint</c> generator only tests <see cref="None" /> to decide whether to stay
///     silent, and analyzer rule CESDK0001 (<c>CheatEngine.SDK.Analyzers</c>) reports one diagnostic per flag that is set, in a
///     fixed order, with its own message text per flag. A flags enum (not a list of diagnostics) keeps the shared
///     predicate free of anything analyzer- or generator-specific.
/// </remarks>
[Flags]
internal enum PluginShapeIssues
{
    /// <summary>The class can be constructed by the generated factory.</summary>
    None = 0,

    /// <summary>The class is <see langword="static" />. Its own bit: every other problem follows from it.</summary>
    Static = 1 << 0,

    /// <summary>The class is <see langword="abstract" />.</summary>
    Abstract = 1 << 1,

    /// <summary>The class itself has type parameters.</summary>
    Generic = 1 << 2,

    /// <summary>A type the class is nested in has type parameters.</summary>
    NestedInGeneric = 1 << 3,

    /// <summary>
    ///     No base class is named <c>CheatEnginePlugin</c> in namespace <c>CheatEngine.SDK.Hosting.Plugin</c>. Checked
    ///     structurally (name and namespace of each symbol in the base-type chain), not by resolving
    ///     <c>CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin</c> and comparing symbols: the decision then depends on nothing but
    ///     the symbol it is given, which also lets the entry-point generator use it from inside a per-node transform
    ///     without combining with the compilation.
    /// </summary>
    NotDerivedFromPluginBase = 1 << 4,

    /// <summary>
    ///     The class, or a type it is nested in, is <see langword="private" />, <see langword="protected" /> or
    ///     <see langword="private protected" />: not reachable from a generated top-level type in another file.
    /// </summary>
    Inaccessible = 1 << 5,

    /// <summary>The class, or a type it is nested in, is a <see langword="file" /> type: out of reach from another file.</summary>
    FileLocal = 1 << 6,

    /// <summary>
    ///     Constructors are declared, and none of them is callable with an empty argument list: neither a literally
    ///     parameterless constructor, nor one whose every parameter is optional or ends in <see langword="params" />.
    /// </summary>
    MissingParameterlessConstructor = 1 << 7,

    /// <summary>
    ///     A constructor callable with an empty argument list exists, but every such constructor is
    ///     <see langword="private" />, <see langword="protected" /> or <see langword="private protected" />.
    /// </summary>
    InaccessibleParameterlessConstructor = 1 << 8,

    /// <summary>The display name given to the attribute is missing, <see langword="null" />, empty or white space.</summary>
    InvalidName = 1 << 9,

    /// <summary>
    ///     The class or a base class declares <see langword="required" /> members and the parameterless constructor is
    ///     not marked <c>[SetsRequiredMembers]</c>: <c>new T()</c> without an object initializer is CS9035.
    /// </summary>
    RequiredMembers = 1 << 10,

    /// <summary>
    ///     The class, a type it is nested in, or its parameterless constructor is marked <c>[Obsolete]</c> with
    ///     <c>error: true</c>: naming it is CS0619, which no <c>#pragma</c> can silence.
    /// </summary>
    ObsoleteError = 1 << 11,

    /// <summary>
    ///     The class is, or is nested in, the top-level type <c>CESDK.CESDK</c>: the name Cheat Engine dictates for the
    ///     generated entry point type, of which an assembly holds only one.
    /// </summary>
    ReservedEntryPointName = 1 << 12
}
