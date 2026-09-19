using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

namespace CheatEngine.SDK.Analyzers.Generation;

/// <summary>
///     The sentence fragment that completes "Lua global binding 'X' ..." in the CESDK2004 message, one per
///     <see cref="LuaGlobalShapeIssues" /> flag (from <c>CheatEngine.SDK.SourceGenerators.Shared</c>).
/// </summary>
internal static class LuaGlobalProblemText
{
    /// <summary>The flags in the order they are reported for one method.</summary>
    public static readonly ImmutableArray<LuaGlobalShapeIssues> ReportOrder =
    [
        LuaGlobalShapeIssues.NotOrdinaryMethod,
        LuaGlobalShapeIssues.NotStatic,
        LuaGlobalShapeIssues.NotPartialDefinition,
        LuaGlobalShapeIssues.AlreadyImplemented,
        LuaGlobalShapeIssues.Generic,
        LuaGlobalShapeIssues.Async,
        LuaGlobalShapeIssues.InvalidName,
        LuaGlobalShapeIssues.ByRefParameter,
        LuaGlobalShapeIssues.ParamsParameter,
        LuaGlobalShapeIssues.OptionalParameter,
        LuaGlobalShapeIssues.StateParameterNotFirst,
        LuaGlobalShapeIssues.UnsupportedParameterType,
        LuaGlobalShapeIssues.ResultBeforeArgument,
        LuaGlobalShapeIssues.UnsupportedResultType,
        LuaGlobalShapeIssues.SpanResult,
        LuaGlobalShapeIssues.UnsupportedReturnType,
        LuaGlobalShapeIssues.TryFormReturnNotBool
    ];

    /// <summary>Returns the message fragment of a single flag.</summary>
    public static string Describe(LuaGlobalShapeIssues problem)
    {
        return problem switch
        {
            LuaGlobalShapeIssues.NotOrdinaryMethod =>
                "must be an ordinary method: not an accessor, operator, local function or explicit interface implementation",
            LuaGlobalShapeIssues.NotStatic => "must be static: this pass binds static members only",
            LuaGlobalShapeIssues.NotPartialDefinition =>
                "must be the defining declaration of a partial method: the generator adds the implementing part",
            LuaGlobalShapeIssues.AlreadyImplemented =>
                "must not already have an implementing declaration: a generated one would be a second body",
            LuaGlobalShapeIssues.Generic => "must not be generic",
            LuaGlobalShapeIssues.Async => "must not be async",
            LuaGlobalShapeIssues.InvalidName =>
                "must be given a Lua identifier in [LuaGlobal] that is not a reserved word (Lua 5.3 manual, section 3.1)",
            LuaGlobalShapeIssues.ByRefParameter =>
                "must take its arguments by value and its results as 'out' parameters: 'ref', 'in' and 'ref readonly' are not supported",
            LuaGlobalShapeIssues.ParamsParameter => "must not have a 'params' parameter",
            LuaGlobalShapeIssues.OptionalParameter =>
                "must not have a parameter with a default value: the body pushes every argument",
            LuaGlobalShapeIssues.StateParameterNotFirst =>
                "must take its 'CheatEngine.SDK.Lua.State.LuaState' parameter, if any, first",
            LuaGlobalShapeIssues.UnsupportedParameterType =>
                "must use only argument types a marshaller pushes: int, long, float, double, bool, nuint, ReadOnlySpan<byte> or string",
            LuaGlobalShapeIssues.ResultBeforeArgument =>
                "must declare its results ('out' parameters and copy-out pairs) after every argument",
            LuaGlobalShapeIssues.UnsupportedResultType =>
                "must use only result types a marshaller reads (int, long, float, double, bool, nuint, string), or a 'Span<byte> destination, out int written' copy-out pair",
            LuaGlobalShapeIssues.SpanResult =>
                "must not return a ReadOnlySpan<byte> result: it would point into a Lua string popped before the wrapper returns; use 'Span<byte> destination, out int written' or 'string' instead",
            LuaGlobalShapeIssues.UnsupportedReturnType => "must return void, bool or a type a marshaller reads",
            LuaGlobalShapeIssues.TryFormReturnNotBool =>
                "must return bool when it declares 'out' results: that is the Try form's shape, the throwing form has no 'out' parameter",
            _ => "cannot receive a generated body"
        };
    }
}
