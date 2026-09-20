using System;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;

/// <summary>
///     C# identifier and namespace validity for spec-file <c>method</c>/<c>type</c>/<c>namespace</c> values, and the
///     keyword-escape rule for the ones that become generated identifiers.
/// </summary>
/// <remarks>
///     Deliberately not the linked <c>Identifiers.Escape</c> of <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>: that
///     file
///     lives in the LuaBindings project, which EngineApi does not reference, and it is not part of <c>Shared/</c>.
///     This is an independent, equally small implementation of the same one-line rule.
/// </remarks>
internal static class SpecIdentifiers
{
    // The reserved words of the C# language (ECMA-334, section 6.4.4), which need an '@' prefix to name a declaration.
    // Contextual keywords ('partial', 'var', 'async', ...) are not reserved and need no escape.
    private static readonly string[] Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];

    /// <summary>
    ///     Whether <paramref name="value" /> is a plain (unescaped) C# identifier: ASCII letter or <c>_</c>, then
    ///     letters, digits, <c>_</c>.
    /// </summary>
    public static bool IsValidIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsStart(value![0])) return false;

        for (var i = 1; i < value.Length; i++)
            if (!IsPart(value[i]))
                return false;

        return true;
    }

    /// <summary>Whether <paramref name="value" /> is valid where the generator cannot emit an <c>@</c> escape.</summary>
    public static bool IsValidTypeIdentifier(string? value)
    {
        return IsValidIdentifier(value) && Array.IndexOf(Keywords, value) < 0;
    }

    /// <summary>
    ///     Whether <paramref name="value" /> is a dotted sequence of valid identifiers, or empty for the global
    ///     namespace.
    /// </summary>
    public static bool IsValidNamespace(string value)
    {
        if (value.Length == 0) return true;

        foreach (var part in value.Split('.'))
            if (!IsValidTypeIdentifier(part))
                return false;

        return true;
    }

    /// <summary><paramref name="identifier" />, <c>@</c>-prefixed when it is a reserved word.</summary>
    public static string Escape(string identifier)
    {
        return Array.IndexOf(Keywords, identifier) >= 0 ? "@" + identifier : identifier;
    }

    private static bool IsStart(char c)
    {
        return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
    }

    private static bool IsPart(char c)
    {
        return IsStart(c) || c is >= '0' and <= '9';
    }
}
