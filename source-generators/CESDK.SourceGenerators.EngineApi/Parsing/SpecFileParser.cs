using System;
using System.Collections.Generic;
using CESDK.SourceGenerators.EngineApi.Model;
using CESDK.SourceGenerators.Shared;
using CESDK.SourceGenerators.Shared.LuaEmit;

namespace CESDK.SourceGenerators.EngineApi.Parsing;

/// <summary>
///     Parses the curated spec-file format into a <see cref="SpecFileModel" />. The only code of this generator that
///     reads spec-file text; every other type here and in <c>Model/</c> works with the already-typed result.
/// </summary>
/// <remarks>
///     <para>
///         Dependency-free by construction: the whole type touches nothing but <see cref="string" /> and the BCL
///         collections,
///         so it parses identically whether the caller is the generator's <c>AdditionalTextsProvider</c> pipeline (which
///         hands it <c>AdditionalText.Path</c> and <c>AdditionalText.GetText(...).ToString()</c>) or a plain unit test
///         that
///         never touches Roslyn.
///     </para>
///     <para>
///         A malformed line, an unknown or duplicated key, an invalid name, an unsupported kind, or a duplicated method
///         name
///         never throws and never stops the file: the offending <em>entry</em> (or, for a broken header, the whole file)
///         is
///         left out of <see cref="SpecFileModel.Calls" /> and recorded in <see cref="SpecFileModel.Issues" />
///         (generators never report diagnostics). <see cref="Parse" /> itself never throws for any input, including
///         <see langword="null" />-like empty text.
///     </para>
/// </remarks>
internal static class SpecFileParser
{
    /// <summary>The recognised spec-file extension (case-insensitive), the <c>AdditionalTextsProvider</c> filter.</summary>
    public const string FileNameSuffix = ".cesdk-api.txt";

    /// <summary>Whether <paramref name="path" /> ends with <see cref="FileNameSuffix" />.</summary>
    public static bool IsSpecFile(string? path)
    {
        return path is not null && path.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Parses the text of one spec file. Never throws; <see cref="SpecFileModel.HintName" /> is empty (assigned later
    ///     by <see cref="SpecFiles" />).
    /// </summary>
    public static SpecFileModel Parse(string filePath, string? text)
    {
        List<SpecIssue> issues = [];
        var blocks = SplitBlocks(text ?? string.Empty, issues);

        var headerOk = ParseHeader(blocks, issues, out var ns, out var typeName);

        List<(SpecCallModel Call, int Line)> parsed = [];
        if (headerOk)
            for (var i = 1; i < blocks.Count; i++)
            {
                var call = ParseEntry(blocks[i], issues);
                if (call is not null) parsed.Add((call, blocks[i].StartLine));
            }

        var calls = DropDuplicateMethodNames(parsed, issues);
        calls.Sort(static (left, right) => string.CompareOrdinal(left.Call.MethodName, right.Call.MethodName));

        var cachedGlobals = CollectCachedGlobals(calls);

        return new SpecFileModel(
            filePath,
            headerOk ? ns : string.Empty,
            headerOk ? typeName : string.Empty,
            string.Empty,
            new EquatableArray<string>([.. cachedGlobals]),
            new EquatableArray<SpecCallModel>([.. calls]),
            new EquatableArray<SpecIssue>([.. issues]));
    }

    private static List<string> CollectCachedGlobals(List<SpecCallModel> calls)
    {
        List<string> cachedGlobals = [];
        HashSet<string> seenGlobals = new(StringComparer.Ordinal);
        foreach (var call in calls)
            if (seenGlobals.Add(call.Call.GlobalName))
                cachedGlobals.Add(call.Call.GlobalName);

        cachedGlobals.Sort(StringComparer.Ordinal);
        return cachedGlobals;
    }

    // Blank lines (whitespace-only, after comment lines are dropped) separate blocks; '#' lines are comments and
    // never affect block boundaries, wherever they appear. The first block is the header, every later one an entry.
    private static List<Block> SplitBlocks(string text, List<SpecIssue> issues)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        List<Block> blocks = [];
        Block current = new();
        for (var i = 0; i < lines.Length; i++) AddLine(lines[i], i + 1, blocks, ref current, issues);

        if (!current.IsEmpty) blocks.Add(current);

        return blocks;
    }

    private static void AddLine(string rawLine, int lineNumber, List<Block> blocks, ref Block current,
        List<SpecIssue> issues)
    {
        var trimmed = rawLine.Trim();
        if (trimmed.Length == 0)
        {
            if (!current.IsEmpty)
            {
                blocks.Add(current);
                current = new Block();
            }

            return;
        }

        if (trimmed[0] == '#') return;

        if (current.StartLine == 0) current.StartLine = lineNumber;

        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            issues.Add(new SpecIssue(lineNumber, "Malformed line: expected 'key: value'."));
            current.Malformed = true;
            return;
        }

        var key = trimmed[..colon].TrimEnd();
        var value = trimmed[(colon + 1)..].Trim();
        current.Fields.Add((lineNumber, key, value));
    }

    private static bool ParseHeader(List<Block> blocks, List<SpecIssue> issues, out string ns, out string typeName)
    {
        ns = string.Empty;
        typeName = string.Empty;

        if (blocks.Count == 0)
        {
            issues.Add(new SpecIssue(1,
                "The spec file is empty: expected a header block with 'namespace' and 'type'."));
            return false;
        }

        var header = blocks[0];
        if (header.Malformed)
        {
            issues.Add(new SpecIssue(header.StartLine, "The header block contains a malformed line."));
            return false;
        }

        if (!ReadHeaderFields(header, issues, out var namespaceValue, out var typeValue)) return false;

        if (namespaceValue is null)
        {
            issues.Add(new SpecIssue(header.StartLine,
                "The header is missing required key 'namespace' (use an empty value for the global namespace)."));
            return false;
        }

        if (typeValue is null || typeValue.Length == 0)
        {
            issues.Add(new SpecIssue(header.StartLine,
                "The header is missing required key 'type', or its value is empty."));
            return false;
        }

        if (!SpecIdentifiers.IsValidNamespace(namespaceValue))
        {
            issues.Add(new SpecIssue(header.StartLine, "'" + namespaceValue + "' is not a valid namespace."));
            return false;
        }

        if (!SpecIdentifiers.IsValidIdentifier(typeValue))
        {
            issues.Add(new SpecIssue(header.StartLine, "'" + typeValue + "' is not a valid type name."));
            return false;
        }

        ns = namespaceValue;
        typeName = typeValue;
        return true;
    }

    private static bool ReadHeaderFields(Block header, List<SpecIssue> issues, out string? namespaceValue,
        out string? typeValue)
    {
        namespaceValue = null;
        typeValue = null;
        HashSet<string> seen = new(StringComparer.Ordinal);
        var ok = true;
        foreach (var (line, key, value) in header.Fields)
        {
            if (!seen.Add(key))
            {
                issues.Add(new SpecIssue(line, "Duplicate header key '" + key + "'."));
                ok = false;
                continue;
            }

            switch (key)
            {
                case "namespace":
                    namespaceValue = value;
                    break;
                case "type":
                    typeValue = value;
                    break;
                default:
                    issues.Add(new SpecIssue(line, "Unknown header key '" + key + "'."));
                    ok = false;
                    break;
            }
        }

        return ok;
    }

    private static SpecCallModel? ParseEntry(Block block, List<SpecIssue> issues)
    {
        if (block.Malformed)
        {
            issues.Add(new SpecIssue(block.StartLine,
                "The entry contains a malformed line; the whole entry was skipped."));
            return null;
        }

        if (!ReadEntryFields(block, issues, out var fields)) return null;

        if (!ValidateRequiredText(fields, block.StartLine, issues, out var isTry, out var isThrowing)) return null;

        if (!ValidateResultShape(fields, isTry, isThrowing, block.StartLine, issues)) return null;

        var arguments = ParseArguments(fields.ArgTokens, issues);
        if (arguments is null) return null;

        var fixedArguments = ParseFixedArguments(fields.FixedTokens, issues);
        if (fixedArguments is null) return null;
        arguments.AddRange(fixedArguments);

        var results = ParseResults(fields.ResultTokens, issues);
        if (results is null) return null;

        if (!TryParseReturnKind(fields, isThrowing, block.StartLine, issues, out var returnKind,
                out var returnIsNullable)) return null;

        var escapedMethod = SpecIdentifiers.Escape(fields.Method!);
        LuaGlobalCallModel call = new(
            fields.Global!,
            LuaGlobalCallModel.CacheFieldFor(fields.Global!),
            "public static",
            escapedMethod,
            string.Empty,
            new EquatableArray<LuaArgumentModel>([.. arguments]),
            isTry ? LuaCallForm.Try : LuaCallForm.Throwing,
            new EquatableArray<LuaResultModel>([.. results]),
            returnKind,
            returnIsNullable);

        return new SpecCallModel(fields.Doc!, call);
    }

    private static bool ReadEntryFields(Block block, List<SpecIssue> issues, out EntryFields fields)
    {
        fields = new EntryFields();
        HashSet<string> singular = new(StringComparer.Ordinal);
        var ok = true;

        foreach (var (line, key, value) in block.Fields)
            switch (key)
            {
                case "global":
                    ok &= RequireOnce(singular, "global", line, issues);
                    fields.Global = value;
                    break;
                case "method":
                    ok &= RequireOnce(singular, "method", line, issues);
                    fields.Method = value;
                    break;
                case "form":
                    ok &= RequireOnce(singular, "form", line, issues);
                    fields.Form = value;
                    break;
                case "doc":
                    ok &= RequireOnce(singular, "doc", line, issues);
                    fields.Doc = value;
                    break;
                case "return":
                    ok &= RequireOnce(singular, "return", line, issues);
                    fields.ReturnToken = value;
                    fields.SawReturn = true;
                    break;
                case "arg":
                    fields.ArgTokens.Add((line, value));
                    break;
                case "fixed":
                    fields.FixedTokens.Add((line, value));
                    break;
                case "result":
                    fields.ResultTokens.Add((line, value));
                    break;
                default:
                    issues.Add(new SpecIssue(line, "Unknown entry key '" + key + "'."));
                    ok = false;
                    break;
            }

        return ok;
    }

    private static bool ValidateRequiredText(EntryFields fields, int startLine, List<SpecIssue> issues, out bool isTry,
        out bool isThrowing)
    {
        isTry = false;
        isThrowing = false;

        if (string.IsNullOrEmpty(fields.Global))
        {
            issues.Add(new SpecIssue(startLine, "The entry is missing required key 'global'."));
            return false;
        }

        if (string.IsNullOrEmpty(fields.Method))
        {
            issues.Add(new SpecIssue(startLine, "The entry is missing required key 'method'."));
            return false;
        }

        if (string.IsNullOrEmpty(fields.Form))
        {
            issues.Add(new SpecIssue(startLine, "The entry is missing required key 'form'."));
            return false;
        }

        if (string.IsNullOrEmpty(fields.Doc))
        {
            issues.Add(new SpecIssue(startLine, "The entry is missing required key 'doc'."));
            return false;
        }

        if (!LuaNames.IsValidName(fields.Global))
        {
            issues.Add(new SpecIssue(startLine, "'" + fields.Global + "' is not a valid Lua global name."));
            return false;
        }

        if (!SpecIdentifiers.IsValidIdentifier(fields.Method))
        {
            issues.Add(new SpecIssue(startLine, "'" + fields.Method + "' is not a valid C# method name."));
            return false;
        }

        isTry = string.Equals(fields.Form, "try", StringComparison.Ordinal);
        isThrowing = string.Equals(fields.Form, "throwing", StringComparison.Ordinal);
        if (!isTry && !isThrowing)
        {
            issues.Add(new SpecIssue(startLine,
                "'" + fields.Form + "' is not a valid form: expected 'try' or 'throwing'."));
            return false;
        }

        return true;
    }

    private static bool ValidateResultShape(EntryFields fields, bool isTry, bool isThrowing, int startLine,
        List<SpecIssue> issues)
    {
        if (isThrowing && fields.ResultTokens.Count > 0)
        {
            issues.Add(new SpecIssue(startLine,
                "A 'throwing' entry must not declare 'result' (its value, if any, is 'return')."));
            return false;
        }

        if (isTry && fields.SawReturn)
        {
            issues.Add(new SpecIssue(startLine, "A 'try' entry must not declare 'return' (its values are 'result')."));
            return false;
        }

        if (isTry && fields.ResultTokens.Count == 0)
        {
            issues.Add(new SpecIssue(startLine, "A 'try' entry needs at least one 'result'."));
            return false;
        }

        return true;
    }

    private static bool TryParseReturnKind(
        EntryFields fields,
        bool isThrowing,
        int startLine,
        List<SpecIssue> issues,
        out LuaValueKind? returnKind,
        out bool returnIsNullable)
    {
        returnKind = null;
        returnIsNullable = false;
        if (!isThrowing || string.IsNullOrEmpty(fields.ReturnToken)) return true;

        if (!SpecValueKinds.TryParse(fields.ReturnToken!, out var kind, out var nullable))
        {
            issues.Add(new SpecIssue(startLine, "'" + fields.ReturnToken + "' is not a valid return kind."));
            return false;
        }

        if (!LuaValueKinds.CanBeResult(kind))
        {
            issues.Add(new SpecIssue(startLine,
                "'" + fields.ReturnToken +
                "' cannot be a return type: the span would dangle once the stack is restored."));
            return false;
        }

        returnKind = kind;
        returnIsNullable = nullable;
        return true;
    }

    private static List<LuaArgumentModel>? ParseArguments(List<(int Line, string Value)> tokens, List<SpecIssue> issues)
    {
        List<LuaArgumentModel> arguments = new(tokens.Count);
        foreach (var (line, value) in tokens)
        {
            if (!TryParseNamedValue(value, out var name, out var kindToken)
                || !SpecIdentifiers.IsValidIdentifier(name)
                || !SpecValueKinds.TryParse(kindToken, out var kind, out var nullable))
            {
                issues.Add(new SpecIssue(line, "'" + value + "' is not a valid 'name:kind' argument."));
                return null;
            }

            arguments.Add(new LuaArgumentModel(SpecIdentifiers.Escape(name), kind, nullable));
        }

        return arguments;
    }

    // A fixed argument has the narrow, host-facing grammar 'kind:value'. It is pushed in call order but deliberately
    // omitted from the generated C# signature. Only boolean literals are needed by the curated CE surface today; keep
    // that vocabulary explicit rather than accepting arbitrary C# expressions in a repository text file.
    private static List<LuaArgumentModel>? ParseFixedArguments(List<(int Line, string Value)> tokens,
        List<SpecIssue> issues)
    {
        List<LuaArgumentModel> arguments = new(tokens.Count);
        foreach (var (line, value) in tokens)
        {
            if (!TryParseNamedValue(value, out var kindToken, out var literal)
                || !string.Equals(kindToken, "boolean", StringComparison.Ordinal)
                || !(string.Equals(literal, "true", StringComparison.Ordinal)
                     || string.Equals(literal, "false", StringComparison.Ordinal)))
            {
                issues.Add(new SpecIssue(line,
                    "'" + value + "' is not a valid fixed argument: expected 'boolean:true' or 'boolean:false'."));
                return null;
            }

            arguments.Add(new LuaArgumentModel(literal, LuaValueKind.Boolean, false, FixedValue: literal));
        }

        return arguments;
    }

    private static List<LuaResultModel>? ParseResults(List<(int Line, string Value)> tokens, List<SpecIssue> issues)
    {
        List<LuaResultModel> results = new(tokens.Count);
        foreach (var (line, value) in tokens)
        {
            if (!TryParseNamedValue(value, out var name, out var kindToken)
                || !SpecIdentifiers.IsValidIdentifier(name)
                || !SpecValueKinds.TryParse(kindToken, out var kind, out var nullable))
            {
                issues.Add(new SpecIssue(line, "'" + value + "' is not a valid 'name:kind' result."));
                return null;
            }

            if (!LuaValueKinds.CanBeResult(kind))
            {
                issues.Add(new SpecIssue(line,
                    "'" + kindToken + "' cannot be a result: the span would dangle once the stack is restored."));
                return null;
            }

            results.Add(LuaResultModel.Value(kind, SpecIdentifiers.Escape(name), nullable));
        }

        return results;
    }

    // "name:kind" (or "name:string?"): split on the FIRST colon, so the '?' of a nullable string kind is part of
    // the kind token, not mistaken for another separator.
    private static bool TryParseNamedValue(string raw, out string name, out string kind)
    {
        var colon = raw.IndexOf(':');
        if (colon <= 0 || colon == raw.Length - 1)
        {
            name = string.Empty;
            kind = string.Empty;
            return false;
        }

        name = raw[..colon].Trim();
        kind = raw[(colon + 1)..].Trim();
        return name.Length > 0 && kind.Length > 0;
    }

    private static bool RequireOnce(HashSet<string> seen, string key, int line, List<SpecIssue> issues)
    {
        if (seen.Add(key)) return true;

        issues.Add(new SpecIssue(line, "Duplicate entry key '" + key + "'."));
        return false;
    }

    // A method name reused by more than one entry cannot be emitted (CS0111): every entry using it is dropped, one
    // issue per line, mirroring CESDK.SourceGenerators.LuaBindings' duplicate-Lua-name rule (both members dropped).
    private static List<SpecCallModel> DropDuplicateMethodNames(List<(SpecCallModel Call, int Line)> entries,
        List<SpecIssue> issues)
    {
        Dictionary<string, List<int>> linesByMethod = new(StringComparer.Ordinal);
        foreach (var (call, line) in entries)
        {
            var name = call.Call.MethodName;
            if (!linesByMethod.TryGetValue(name, out var lines))
            {
                lines = [];
                linesByMethod.Add(name, lines);
            }

            lines.Add(line);
        }

        List<SpecCallModel> result = new(entries.Count);
        foreach (var (call, _) in entries)
            if (linesByMethod[call.Call.MethodName].Count == 1)
                result.Add(call);

        foreach (var group in linesByMethod)
        {
            if (group.Value.Count <= 1) continue;

            foreach (var line in group.Value)
                issues.Add(new SpecIssue(line,
                    "Duplicate method name '" + group.Key + "': every entry using it was dropped."));
        }

        return result;
    }

    // One "key: value" line. A line without a colon (or an empty key) marks the whole block Malformed: the block is
    // still collected (so the caller can report one issue at its start line) but ParseEntry/ParseHeader never look
    // at a malformed block's fields.
    private sealed class Block
    {
        public readonly List<(int Line, string Key, string Value)> Fields = [];
        public bool Malformed;
        public int StartLine;

        public bool IsEmpty => Fields.Count == 0 && !Malformed;
    }

    // The raw fields of one entry block, read once by ReadEntryFields and consumed by the validators below.
    private sealed class EntryFields
    {
        public readonly List<(int Line, string Value)> ArgTokens = [];
        public readonly List<(int Line, string Value)> FixedTokens = [];
        public readonly List<(int Line, string Value)> ResultTokens = [];
        public string? Doc;
        public string? Form;
        public string? Global;
        public string? Method;
        public string? ReturnToken;
        public bool SawReturn;
    }
}
