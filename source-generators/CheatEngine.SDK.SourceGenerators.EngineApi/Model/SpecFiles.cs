using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>Resolves the hint name of every parsed spec file, across the whole pass: the grouping step of the pipeline.</summary>
/// <remarks>
///     One spec file always produces at most one generated file (unlike <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>,
///     which groups many attributed members by containing type), so an <c>AdditionalText</c>'s own path already makes
///     two entries distinct; the collision this step guards against is two paths whose file names differ only in ASCII
///     case, which <see cref="HintNames.ForType" /> alone cannot always tell apart (Roslyn compares hint names
///     case-insensitively).
/// </remarks>
internal static class SpecFiles
{
    /// <summary>Sorts by source path (ordinal, deterministic) and assigns each file's <see cref="SpecFileModel.HintName" />.</summary>
    public static EquatableArray<SpecFileModel> AssignHintNames(ImmutableArray<SpecFileModel> specs)
    {
        if (specs.IsDefaultOrEmpty) return EquatableArray<SpecFileModel>.Empty;

        List<SpecFileModel> sorted = [.. specs];
        sorted.Sort(static (left, right) => string.CompareOrdinal(left.SourcePath, right.SourcePath));

        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sorted.Count; i++)
        {
            var baseName = FileName(sorted[i].SourcePath);
            var hintName = HintNames.ForType(baseName, SpecFileModel.HintSuffix);
            if (!used.Add(hintName))
            {
                hintName = HintNames.Disambiguated(baseName, SpecFileModel.HintSuffix);
                used.Add(hintName);
            }

            sorted[i] = sorted[i] with { HintName = hintName };
        }

        return new EquatableArray<SpecFileModel>([.. sorted]);
    }

    // The file name only (no directory), without touching the file system: AdditionalText.Path is a string the
    // build system hands the compiler, never read from disk here.
    private static string FileName(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
