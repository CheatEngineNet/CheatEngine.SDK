using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CheatEngine.SDK.SourceGenerators.Shared;

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
///     culture-independent. <see cref="AllocateUnique" /> resolves the remaining, case-insensitive collisions that
///     Roslyn rejects when sources are added to a generation pass.
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
        return Build(typeName, suffix);
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
        var readable = ForType(typeName, suffix);
        StringBuilder builder = new(readable.Length + 9);
        builder.Append(readable, 0, readable.Length - suffix.Length);
        builder.Append('_');
        AppendHash(builder, typeName);
        return builder.Append(suffix).ToString();
    }

    /// <summary>Creates the case-insensitive set used to reserve generated-source hint names.</summary>
    /// <remarks>
    ///     Roslyn compares source hint names with ordinal case insensitivity. Callers must use this set for an entire
    ///     generation pass and pass it to <see cref="AllocateUnique" /> for every source they emit.
    /// </remarks>
    public static HashSet<string> CreateUsedNames()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reserves and returns a unique, deterministic hint name for one generated source.</summary>
    /// <remarks>
    ///     The readable name is preferred. On a collision, the exact type name's deterministic hash is appended. If
    ///     that name is already reserved too, ordinal suffixes beginning at two are tried until
    ///     <see cref="HashSet{T}.Add(T)" /> succeeds. The supplied set must use
    ///     <see cref="StringComparer.OrdinalIgnoreCase" /> so the reservation matches Roslyn's rule.
    ///     The Engine API generator currently needs a source-path identity in addition to a type name. Its allocator
    ///     remains intentionally separate until that identity rule is reconciled with this shared helper.
    /// </remarks>
    public static string AllocateUnique(string typeName, string suffix, HashSet<string> used)
    {
        if (used is null) throw new ArgumentNullException(nameof(used));

        if (!StringComparer.OrdinalIgnoreCase.Equals(used.Comparer))
            throw new ArgumentException("Hint names must be reserved with StringComparer.OrdinalIgnoreCase.",
                nameof(used));

        var readable = ForType(typeName, suffix);
        if (used.Add(readable)) return readable;

        var hashed = Disambiguated(typeName, suffix);
        if (used.Add(hashed)) return hashed;

        for (var ordinal = 2;; ordinal++)
        {
            var suffixed = AppendOrdinal(hashed, suffix, ordinal);
            if (used.Add(suffixed)) return suffixed;
        }
    }

    private static string Build(string typeName, string suffix)
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

        if (replaced)
        {
            builder.Append('_');
            AppendHash(builder, typeName);
        }

        return builder.Append(suffix).ToString();
    }

    private static string AppendOrdinal(string hintName, string suffix, int ordinal)
    {
        StringBuilder builder = new(hintName.Length + 12);
        builder.Append(hintName, 0, hintName.Length - suffix.Length);
        builder.Append('_');
        builder.Append(ordinal.ToString(CultureInfo.InvariantCulture));
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
