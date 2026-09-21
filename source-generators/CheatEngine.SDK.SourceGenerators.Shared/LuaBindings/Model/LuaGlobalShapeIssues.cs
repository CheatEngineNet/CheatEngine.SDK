using System;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

/// <summary>
///     Why a <c>[LuaGlobal]</c> method cannot receive a generated body. The generator only uses the flags to stay
///     silent; the CESDK2xxx analyzer rules link the same shape file and explain each of them.
/// </summary>
/// <remarks>
///     Containing-type problems are a separate enum, <see cref="ContainingTypeIssues" />, shared with
///     <c>[LuaFunction]</c>.
/// </remarks>
[Flags]
internal enum LuaGlobalShapeIssues
{
    /// <summary>The method can be implemented.</summary>
    None = 0,

    /// <summary>
    ///     The method is not an ordinary method (an accessor, an operator, a local function, an explicit interface
    ///     implementation).
    /// </summary>
    NotOrdinaryMethod = 1 << 0,

    /// <summary>The method is an instance method: the generator binds static members only.</summary>
    NotStatic = 1 << 1,

    /// <summary>
    ///     The attributed declaration is not the defining declaration of a <see langword="partial" /> method (it is not
    ///     partial, or it is the implementing part).
    /// </summary>
    NotPartialDefinition = 1 << 2,

    /// <summary>The partial method already has an implementing declaration: a generated one would be a second body.</summary>
    AlreadyImplemented = 1 << 3,

    /// <summary>The method has type parameters.</summary>
    Generic = 1 << 4,

    /// <summary>The method is <see langword="async" />.</summary>
    Async = 1 << 5,

    /// <summary>The attribute's name argument is missing, not a string, or not a Lua identifier (see <c>LuaNames</c>).</summary>
    InvalidName = 1 << 6,

    /// <summary>
    ///     An argument has a type no marshaller pushes: only <see langword="int" />, <see langword="long" />,
    ///     <see langword="float" />, <see langword="double" />,
    ///     <see langword="bool" />, <see langword="nuint" />, <c>ReadOnlySpan&lt;byte&gt;</c> and <see langword="string" />
    ///     are accepted unless an explicit annotation names a type that implements the matching
    ///     <c>ILuaMarshaller&lt;T&gt;</c> contract.
    /// </summary>
    UnsupportedParameterType = 1 << 7,

    /// <summary>
    ///     A parameter is <see langword="ref" />, <see langword="in" /> or <see langword="ref" />
    ///     <see langword="readonly" />: arguments are values, results are <see langword="out" />.
    /// </summary>
    ByRefParameter = 1 << 8,

    /// <summary>A parameter is <see langword="params" />.</summary>
    ParamsParameter = 1 << 9,

    /// <summary>A parameter has a default value: the body pushes every argument, so a default would be meaningless.</summary>
    OptionalParameter = 1 << 10,

    /// <summary>A <c>LuaState</c> parameter is not the first parameter.</summary>
    StateParameterNotFirst = 1 << 11,

    /// <summary>
    ///     An <see langword="out" /> parameter has a type no marshaller reads (including a named type without a valid
    ///     <c>ILuaMarshaller&lt;T&gt;</c> annotation), or a <c>Span&lt;byte&gt;</c> destination
    ///     is not followed by <see langword="out" /> <see langword="int" />.
    /// </summary>
    UnsupportedResultType = 1 << 12,

    /// <summary>
    ///     A result is a <c>ReadOnlySpan&lt;byte&gt;</c> (an <see langword="out" /> parameter or the return type): it
    ///     would point into a Lua string the body pops before returning. Use
    ///     <c>Span&lt;byte&gt; destination</c>, <see langword="out" /> <see langword="int" /> written, or
    ///     <see langword="string" />.
    /// </summary>
    SpanResult = 1 << 13,

    /// <summary>An argument follows a result: results (<see langword="out" /> parameters and copy-out pairs) must come last.</summary>
    ResultBeforeArgument = 1 << 14,

    /// <summary>
    ///     The return type is neither <see langword="void" />, <see langword="bool" />, a built-in marshalled type nor
    ///     a type with a valid explicit <c>ILuaMarshaller&lt;T&gt;</c> annotation.
    /// </summary>
    UnsupportedReturnType = 1 << 15,

    /// <summary>
    ///     The method has <see langword="out" /> results but does not return <see langword="bool" />: the Try form is
    ///     <see langword="bool" /> + <see langword="out" /> results, the throwing form has no <see langword="out" />
    ///     parameter.
    /// </summary>
    TryFormReturnNotBool = 1 << 16
}
