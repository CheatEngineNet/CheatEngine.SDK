using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;

namespace CheatEngine.SDK.Analyzers.Plugin;

/// <summary>
///     The sentence fragment that completes "Plugin class 'X' ..." in the CESDK0001 message, one per problem. Kept
///     next to the flags so a new flag cannot be added without its text. Analyzer-only: the shared
///     <see cref="PluginShape" /> predicate (from <c>CheatEngine.SDK.SourceGenerators.Shared</c>) only
///     tests <see cref="PluginShapeIssues.None" />, so this text has no counterpart on the generator side.
/// </summary>
internal static class PluginClassProblemText
{
    /// <summary>The flags in the order they are reported for one class.</summary>
    public static readonly ImmutableArray<PluginShapeIssues> ReportOrder =
    [
        PluginShapeIssues.Static,
        PluginShapeIssues.Abstract,
        PluginShapeIssues.Generic,
        PluginShapeIssues.NestedInGeneric,
        PluginShapeIssues.NotDerivedFromPluginBase,
        PluginShapeIssues.Inaccessible,
        PluginShapeIssues.FileLocal,
        PluginShapeIssues.ReservedEntryPointName,
        PluginShapeIssues.MissingParameterlessConstructor,
        PluginShapeIssues.InaccessibleParameterlessConstructor,
        PluginShapeIssues.RequiredMembers,
        PluginShapeIssues.ObsoleteError,
        PluginShapeIssues.InvalidName
    ];

    /// <summary>Returns the message fragment of a single flag.</summary>
    public static string Describe(PluginShapeIssues problem)
    {
        return problem switch
        {
            PluginShapeIssues.Static => "must not be static: the generated entry point creates an instance of it",
            PluginShapeIssues.Abstract => "must not be abstract: the generated entry point creates it with 'new'",
            PluginShapeIssues.Generic =>
                "must not be generic: the generated entry point has no type arguments to give it",
            PluginShapeIssues.NestedInGeneric =>
                "must not be nested in a generic type: the generated entry point has no type arguments to give it",
            PluginShapeIssues.NotDerivedFromPluginBase => "must derive from 'CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin'",
            PluginShapeIssues.Inaccessible =>
                "must be reachable from generated code in the same assembly: it and every type it is nested in must be public or internal",
            PluginShapeIssues.FileLocal =>
                "must not be a file-local type: the generated entry point lives in another file",
            PluginShapeIssues.ReservedEntryPointName =>
                "must not be, or be nested in, a type named 'CESDK.CESDK': Cheat Engine dictates that name for the generated entry point type",
            PluginShapeIssues.MissingParameterlessConstructor =>
                "must declare a public or internal constructor callable with no arguments (parameterless, or with only optional/'params' parameters): the generated entry point calls it as 'new T()'",
            PluginShapeIssues.InaccessibleParameterlessConstructor =>
                "must make a constructor callable with no arguments public or internal: the generated entry point calls it as 'new T()'",
            PluginShapeIssues.RequiredMembers =>
                "must not have required members, unless its parameterless constructor is marked [SetsRequiredMembers]: the generated entry point calls 'new' without an object initializer",
            PluginShapeIssues.ObsoleteError =>
                "must be usable without an [Obsolete] error: neither the class, a type it is nested in, nor its parameterless constructor may be marked [Obsolete] with 'error: true', because the generated entry point names them",
            PluginShapeIssues.InvalidName =>
                "must be given a non-empty display name in [CheatEnginePlugin]: it is the name Cheat Engine shows in its plugin list",
            _ => "cannot be constructed by the generated entry point"
        };
    }
}
