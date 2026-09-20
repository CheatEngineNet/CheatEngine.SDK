namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>One link of a containing-type chain, as the generated partial part re-declares it.</summary>
/// <param name="Keyword">
///     The declaration keyword the part must repeat: <see langword="class" />, <see langword="struct" />,
///     <see langword="record" /> or
///     <c>record struct</c>.
/// </param>
/// <param name="Name">
///     The identifier, keyword-escaped (<c>@class</c>) where needed. Never carries type parameters: generic
///     containing types are rejected.
/// </param>
/// <param name="IsReadOnly">
///     Whether the declaration is a <see langword="readonly" /> struct. Every generated partial part must repeat this
///     modifier or the compiler rejects the split type.
/// </param>
internal sealed record TypeDeclarationModel(string Keyword, string Name, bool IsReadOnly = false);
