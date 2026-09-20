using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>Prepares parsed spec files as one deterministic generation pass.</summary>
/// <remarks>
///     A spec file exclusively owns its generated type. That gives a malformed or duplicate <c>AdditionalFile</c> an
///     explicit, actionable failure instead of allowing two independent emitters to rely on partial-type ordering.
/// </remarks>
internal static class SpecFiles
{
    /// <summary>
    ///     Sorts specs by source path, gives every one a unique deterministic hint name, then suppresses every file in
    ///     a cross-file generated-identity conflict while retaining a located issue on each participant.
    /// </summary>
    public static EquatableArray<SpecFileModel> AssignHintNames(ImmutableArray<SpecFileModel> specs)
    {
        if (specs.IsDefaultOrEmpty) return EquatableArray<SpecFileModel>.Empty;

        List<SpecFileModel> sorted = [.. specs];
        sorted.Sort(static (left, right) => string.CompareOrdinal(left.SourcePath, right.SourcePath));

        AssignUniqueHintNames(sorted);
        SuppressCrossFileConflicts(sorted);

        return new EquatableArray<SpecFileModel>([.. sorted]);
    }

    private static void AssignUniqueHintNames(List<SpecFileModel> specs)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < specs.Count; i++)
        {
            var baseName = FileName(specs[i].SourcePath);
            var hintName = HintNames.ForType(baseName, SpecFileModel.HintSuffix);
            if (!used.Add(hintName))
            {
                var sourceIdentity = baseName + "." + SourcePathHash(specs[i].SourcePath);
                hintName = HintNames.ForType(sourceIdentity, SpecFileModel.HintSuffix);
                var disambiguator = 1;
                while (!used.Add(hintName))
                {
                    hintName = HintNames.ForType(
                        sourceIdentity + "." + disambiguator.ToString(CultureInfo.InvariantCulture),
                        SpecFileModel.HintSuffix);
                    disambiguator++;
                }
            }

            specs[i] = specs[i] with { HintName = hintName };
        }
    }

    private static void SuppressCrossFileConflicts(List<SpecFileModel> specs)
    {
        Dictionary<string, List<int>> filesByType = new(StringComparer.Ordinal);
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            if (spec.TypeName.Length == 0) continue;

            var typeIdentity = spec.Namespace + "\u001f" + spec.TypeName;
            if (!filesByType.TryGetValue(typeIdentity, out var indices))
            {
                indices = [];
                filesByType.Add(typeIdentity, indices);
            }

            indices.Add(i);
        }

        foreach (var group in filesByType)
        {
            var indices = group.Value;
            if (indices.Count < 2) continue;

            AppendTypeConflictIssues(specs, indices);
            AppendMemberConflictIssues(specs, indices);
            AppendCacheConflictIssues(specs, indices);

            foreach (var index in indices) specs[index] = specs[index] with { IsSuppressed = true };
        }
    }

    private static void AppendTypeConflictIssues(List<SpecFileModel> specs, List<int> indices)
    {
        foreach (var index in indices)
        {
            var spec = specs[index];
            AddConflict(ref spec, spec.TypeLine, spec.TypeColumn,
                "Generated type '" + QualifiedTypeName(spec) +
                "' is declared by multiple Engine API spec files; one spec file must own a generated type.");
            specs[index] = spec;
        }
    }

    private static void AppendMemberConflictIssues(List<SpecFileModel> specs, List<int> indices)
    {
        Dictionary<string, List<(int FileIndex, int Line, int Column)>> owners = new(StringComparer.Ordinal);
        foreach (var index in indices)
        foreach (var call in specs[index].Calls)
        {
            AddOwner(owners, call.Call.MethodName, (index, call.MethodLine, call.MethodColumn));
            if (UsesAddressFacade(call.Call))
                AddOwner(owners, CoreMethodName(call.Call.MethodName), (index, call.MethodLine, call.MethodColumn));
        }

        foreach (var entry in owners)
        {
            if (entry.Value.Count < 2) continue;

            foreach (var (fileIndex, line, column) in entry.Value)
            {
                var spec = specs[fileIndex];
                AddConflict(ref spec, line, column,
                    "Generated member '" + entry.Key + "' is declared by multiple Engine API spec files for type '" +
                    QualifiedTypeName(spec) + "'.");
                specs[fileIndex] = spec;
            }
        }
    }

    private static void AppendCacheConflictIssues(List<SpecFileModel> specs, List<int> indices)
    {
        Dictionary<string, List<(int FileIndex, int Line, int Column)>> owners = new(StringComparer.Ordinal);
        foreach (var index in indices)
        {
            HashSet<string> seenInFile = new(StringComparer.Ordinal);
            foreach (var call in specs[index].Calls)
            {
                var cacheField = LuaGlobalCallModel.CacheFieldFor(call.Call.GlobalName);
                if (seenInFile.Add(cacheField))
                    AddOwner(owners, cacheField, (index, call.GlobalLine, call.GlobalColumn));
            }
        }

        foreach (var entry in owners)
        {
            if (entry.Value.Count < 2) continue;

            foreach (var (fileIndex, line, column) in entry.Value)
            {
                var spec = specs[fileIndex];
                AddConflict(ref spec, line, column,
                    "Generated cache field '" + entry.Key +
                    "' is declared by multiple Engine API spec files for type '" + QualifiedTypeName(spec) + "'.");
                specs[fileIndex] = spec;
            }
        }
    }

    private static void AddOwner(Dictionary<string, List<(int FileIndex, int Line, int Column)>> owners,
        string identity,
        (int FileIndex, int Line, int Column) owner)
    {
        if (!owners.TryGetValue(identity, out var values))
        {
            values = [];
            owners.Add(identity, values);
        }

        values.Add(owner);
    }

    private static void AddConflict(ref SpecFileModel spec, int line, int column, string message)
    {
        List<SpecIssue> issues = [.. spec.Issues];
        issues.Add(new SpecIssue(line, message, column, SpecIssueKind.Conflict));
        spec = spec with { Issues = new EquatableArray<SpecIssue>([.. issues]) };
    }

    private static string QualifiedTypeName(SpecFileModel spec)
    {
        return spec.Namespace.Length == 0 ? spec.TypeName : spec.Namespace + "." + spec.TypeName;
    }

    private static bool UsesAddressFacade(LuaGlobalCallModel call)
    {
        foreach (var argument in call.Arguments)
            if (argument.Kind == LuaValueKind.Address)
                return true;

        foreach (var result in call.Results)
            if (result.Kind == LuaValueKind.Address)
                return true;

        return call.ReturnKind == LuaValueKind.Address;
    }

    private static string CoreMethodName(string methodName)
    {
        return "__" + (methodName[0] == '@' ? methodName[1..] : methodName) + "Raw";
    }

    // FNV-1a over the source path's UTF-16 code units. This keeps a collision resolution stable when unrelated
    // additional files are added before it in ordinal order, without using a process-randomized string hash or putting
    // the complete directory path into a generated file name.
    private static string SourcePathHash(string sourcePath)
    {
        var hash = 2166136261u;
        foreach (var c in sourcePath) hash = unchecked((hash ^ c) * 16777619u);

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    // The file name only (no directory), without touching the file system: AdditionalText.Path is a string the
    // build system hands the compiler, never read from disk here.
    private static string FileName(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
