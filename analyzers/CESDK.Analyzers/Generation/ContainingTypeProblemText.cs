using System.Collections.Immutable;
using CESDK.SourceGenerators.Shared.LuaBindings.Model;

namespace CESDK.Analyzers.Generation;

/// <summary>
///     The sentence fragment that completes "Lua binding 'X' ... its containing type ..." in the CESDK2002 message, one
///     per <see cref="ContainingTypeIssues" /> flag (from <c>CESDK.SourceGenerators.Shared</c>).
///     <c>DiagnosticCatalogTests</c> fails when a flag is missing from <c>ReportOrder</c> or two flags share a message.
/// </summary>
internal static class ContainingTypeProblemText
{
    /// <summary>The flags in the order they are reported for one member.</summary>
    public static readonly ImmutableArray<ContainingTypeIssues> ReportOrder =
    [
        ContainingTypeIssues.NotClassOrStruct,
        ContainingTypeIssues.Generic,
        ContainingTypeIssues.FileLocal,
        ContainingTypeIssues.NotPartial
    ];

    /// <summary>Returns the message fragment of a single flag.</summary>
    public static string Describe(ContainingTypeIssues problem)
    {
        return problem switch
        {
            ContainingTypeIssues.NotClassOrStruct =>
                "must be a class or a struct: interfaces, enums and delegates take no generated members",
            ContainingTypeIssues.Generic =>
                "must not be generic, and must not be nested in a generic type: a generated part cannot be named without type arguments",
            ContainingTypeIssues.FileLocal =>
                "must not be a file-local type, and must not be nested in one: a generated part in another file cannot reach it",
            ContainingTypeIssues.NotPartial =>
                "must be declared partial, like every type it is nested in: a generated part needs a second declaration to add itself to",
            _ => "cannot receive a generated Lua binding part"
        };
    }
}
