using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.Shared.LuaBindings.Parsing;

/// <summary>Spells a symbol name as an identifier token of generated code.</summary>
internal static class Identifiers
{
    /// <summary>
    ///     Prefixes <paramref name="name" /> with <c>@</c> when it is a C# keyword or contextual keyword (<c>@class</c>,
    ///     <c>@var</c>); a symbol's <c>Name</c> never carries the prefix, a declaration sometimes must.
    /// </summary>
    public static string Escape(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
               SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;
    }
}
