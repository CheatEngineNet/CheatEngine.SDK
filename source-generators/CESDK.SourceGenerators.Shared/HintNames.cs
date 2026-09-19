using System;
using System.Text;

namespace CESDK.SourceGenerators.Shared;

/// <summary>
///     Builds the hint name of a generated file from the name of the type it belongs to, for generators that emit one
///     file per type.
/// </summary>
/// <remarks>
///     A hint name must be stable across runs (never a counter), unique within the generator, and usable as a file
///     name on every host, since <c>EmitCompilerGeneratedFiles</c> writes it to disk. Letters, digits, <c>.</c> and
///     <c>_</c> pass through; everything else (path separators, <c>&lt;&gt;</c> of generic arity, keyword escapes,
///     non-ASCII letters) becomes <c>_</c>, and when anything was replaced an 8-digit hash of the original name is
///     appended so that two types differing only in replaced characters cannot share a file. Deterministic and
///     culture-independent. <see cref="ForType" /> alone is not enough when two types differ only in ASCII case (no
///     character is replaced on either side, so both produce the same result, and Roslyn treats hint names as
///     case-insensitive): a caller that emits one file per type must track the names it has already handed out
///     (case-insensitively) and fall back to <see cref="Disambiguated" /> on a collision.
/// </remarks>
internal static class HintNames
{
    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    ///     The hint name for <paramref name="typeName" /> (a dotted, namespace-qualified name as the emitter spells it,
    ///     without <c>global::</c>) with <paramref name="suffix" /> (for example <c>.LuaFunctions.g.cs</c>).
    /// </summary>
    public static string ForType(string typeName, string suffix)
    {
        return Build(typeName, suffix, false);
    }

    /// <summary>
    ///     Same as <see cref="ForType" />, but always appends the hash, even when every character of
    ///     <paramref name="typeName" /> passed through unchanged.
    /// </summary>
    /// <remarks>
    ///     A caller uses this to disambiguate a type whose plain hint name would collide with another type's in the
    ///     same generation pass: Roslyn's <c>AdditionalSourcesCollection</c> compares hint names case-insensitively
    ///     (an <see cref="System.ArgumentException" /> from <c>AddSource</c> otherwise), so two types whose dotted names
    ///     differ only in ASCII case produce the identical <see cref="ForType" /> result (neither one has a replaced
    ///     character), which <see cref="ForType" /> alone cannot tell apart. The hash is computed over the exact,
    ///     case-sensitive <paramref name="typeName" />, so it differs between the two.
    /// </remarks>
    public static string Disambiguated(string typeName, string suffix)
    {
        return Build(typeName, suffix, true);
    }

    private static string Build(string typeName, string suffix, bool forceHash)
    {
        if (typeName is null) throw new ArgumentNullException(nameof(typeName));

        if (suffix is null) throw new ArgumentNullException(nameof(suffix));

        StringBuilder builder = new(typeName.Length + suffix.Length + 9);
        var replaced = false;
        foreach (var c in typeName)
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
                replaced = true;
            }

        if (replaced || forceHash)
        {
            builder.Append('_');
            AppendHash(builder, typeName);
        }

        return builder.Append(suffix).ToString();
    }

    // FNV-1a over the UTF-16 code units: stable across runtimes (string.GetHashCode is randomised per process).
    private static void AppendHash(StringBuilder builder, string value)
    {
        var hash = 2166136261;
        foreach (var c in value) hash = unchecked((hash ^ c) * 16777619);

        for (var shift = 28; shift >= 0; shift -= 4) builder.Append(HexDigits[(int)((hash >> shift) & 0xF)]);
    }
}
