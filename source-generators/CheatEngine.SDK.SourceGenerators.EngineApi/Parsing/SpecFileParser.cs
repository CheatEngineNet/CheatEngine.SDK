using System;
using System.Collections.Generic;
using System.Globalization;
using CheatEngine.SDK.SourceGenerators.EngineApi.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;

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
///         left out of <see cref="SpecFileModel.Calls" /> and recorded in <see cref="SpecFileModel.Issues" />. The
///         generator reports each issue against the additional file after parsing. <see cref="Parse" /> itself never
///         throws for any input, including <see langword="null" />-like empty text.
///     </para>
/// </remarks>
internal static class SpecFileParser
{
    /// <summary>The recognised spec-file extension (case-insensitive), the <c>AdditionalTextsProvider</c> filter.</summary>
    public const string FileNameSuffix = ".cheatengine-sdk-api.txt";

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

        var headerOk = ParseHeader(
            blocks,
            issues,
            out var ns,
            out var typeName,
            out var typeLine,
            out var typeColumn,
            out var fileContract);

        List<SpecCallModel> parsed = [];
        if (headerOk)
            for (var i = 1; i < blocks.Count; i++)
            {
                var call = ParseEntry(blocks[i], issues, fileContract);
                if (call is not null) parsed.Add(call);
            }

        var calls = DropDuplicateMethodNames(parsed, issues);
        calls = DropGeneratedMemberCollisions(calls, issues);
        calls = DropCacheMemberCollisions(calls, issues);
        calls = DropTypeMemberCollisions(calls, headerOk ? typeName : string.Empty, issues);
        calls.Sort(static (left, right) => string.CompareOrdinal(left.Call.MethodName, right.Call.MethodName));

        var cachedGlobals = CollectCachedGlobals(calls);

        return new SpecFileModel(
            filePath,
            headerOk ? ns : string.Empty,
            headerOk ? typeName : string.Empty,
            typeLine,
            typeColumn,
            headerOk ? fileContract : null,
            string.Empty,
            new EquatableArray<string>([.. cachedGlobals]),
            new EquatableArray<SpecCallModel>([.. calls]),
            new EquatableArray<SpecIssue>([.. issues]),
            false);
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

        var firstNonWhitespace = rawLine.Length - rawLine.TrimStart().Length;
        if (current.StartLine == 0)
        {
            current.StartLine = lineNumber;
            current.StartColumn = firstNonWhitespace + 1;
        }

        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            issues.Add(new SpecIssue(lineNumber, "Malformed line: expected 'key: value'.", firstNonWhitespace + 1));
            current.Malformed = true;
            return;
        }

        var key = trimmed[..colon].TrimEnd();
        var value = trimmed[(colon + 1)..].Trim();
        current.Fields.Add(
            new SpecField(lineNumber, firstNonWhitespace + 1, firstNonWhitespace + colon + 3, key, value));
    }

    private static bool ParseHeader(List<Block> blocks, List<SpecIssue> issues, out string ns, out string typeName,
        out int typeLine, out int typeColumn, out SpecFileContract? contract)
    {
        ns = string.Empty;
        typeName = string.Empty;
        typeLine = 1;
        typeColumn = 1;
        contract = null;

        if (blocks.Count == 0)
        {
            issues.Add(new SpecIssue(1,
                "The spec file is empty: expected a header block with 'namespace' and 'type'."));
            return false;
        }

        var header = blocks[0];
        if (header.Malformed)
        {
            issues.Add(new SpecIssue(header.StartLine, "The header block contains a malformed line.",
                header.StartColumn));
            return false;
        }

        if (!ReadHeaderFields(
                header,
                issues,
                out var namespaceValue,
                out var typeValue,
                out typeLine,
                out typeColumn,
                out contract))
            return false;

        if (!ValidateHeaderIdentity(header, namespaceValue, typeValue, typeLine, typeColumn, issues)) return false;

        ns = namespaceValue!;
        typeName = typeValue!;
        return true;
    }

    private static bool ValidateHeaderIdentity(Block header, string? namespaceValue, string? typeValue, int typeLine,
        int typeColumn, List<SpecIssue> issues)
    {
        if (namespaceValue is null)
        {
            issues.Add(new SpecIssue(header.StartLine,
                "The header is missing required key 'namespace' (use an empty value for the global namespace).",
                header.StartColumn));
            return false;
        }

        if (typeValue is null || typeValue.Length == 0)
        {
            issues.Add(new SpecIssue(header.StartLine,
                "The header is missing required key 'type', or its value is empty.", header.StartColumn));
            return false;
        }

        if (!SpecIdentifiers.IsValidNamespace(namespaceValue))
        {
            issues.Add(new SpecIssue(header.StartLine, "'" + namespaceValue + "' is not a valid namespace.",
                header.StartColumn));
            return false;
        }

        if (SpecIdentifiers.IsValidTypeIdentifier(typeValue)) return true;

        issues.Add(new SpecIssue(typeLine, "'" + typeValue + "' is not a valid type name.", typeColumn));
        return false;
    }

    private static bool ReadHeaderFields(Block header, List<SpecIssue> issues, out string? namespaceValue,
        out string? typeValue, out int typeLine, out int typeColumn, out SpecFileContract? contract)
    {
        HeaderFields fields = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        var ok = true;
        foreach (var field in header.Fields)
        {
            if (!seen.Add(field.Key))
            {
                issues.Add(new SpecIssue(field.Line, "Duplicate header key '" + field.Key + "'.", field.KeyColumn));
                ok = false;
                continue;
            }

            if (!TrySetHeaderField(fields, field, issues)) ok = false;
        }

        namespaceValue = fields.Namespace?.Value;
        typeValue = fields.Type?.Value;
        typeLine = fields.Type?.Line ?? header.StartLine;
        typeColumn = fields.Type?.ValueColumn ?? header.StartColumn;
        contract = null;
        return ok && TryCreateContract(fields, header, issues, out contract);
    }

    private static bool TrySetHeaderField(HeaderFields fields, SpecField field, List<SpecIssue> issues)
    {
        switch (field.Key)
        {
            case "namespace":
                fields.Namespace = field;
                return true;
            case "type":
                fields.Type = field;
                return true;
            case "contract":
                fields.ContractSchema = field;
                return true;
            case "provenance":
                fields.Provenance = field;
                return true;
            case "minimum-ce":
                fields.MinimumCe = field;
                return true;
            case "architecture":
                fields.Architecture = field;
                return true;
            case "thread":
                fields.Thread = field;
                return true;
            case "ownership":
                fields.Ownership = field;
                return true;
            default:
                issues.Add(new SpecIssue(field.Line, "Unknown header key '" + field.Key + "'.", field.KeyColumn));
                return false;
        }
    }

    private static bool TryCreateContract(HeaderFields fields, Block header, List<SpecIssue> issues,
        out SpecFileContract? contract)
    {
        contract = null;
        if (fields.ContractSchema is null) return ValidateLegacyContractFields(fields, issues);

        var schema = fields.ContractSchema.Value;
        if (!string.Equals(schema.Value, "ce77", StringComparison.Ordinal))
        {
            issues.Add(new SpecIssue(schema.Line,
                "'" + schema.Value + "' is not a valid Engine API contract: expected 'ce77'.", schema.ValueColumn));
            return false;
        }

        return TryCreateCe77Contract(fields, header, issues, out contract);
    }

    private static bool ValidateLegacyContractFields(HeaderFields fields, List<SpecIssue> issues)
    {
        var field = fields.FirstContractField;
        if (field is null) return true;

        var value = field.Value;
        issues.Add(new SpecIssue(value.Line, "Engine API contract fields require header 'contract: ce77'.",
            value.KeyColumn));
        return false;
    }

    private static bool TryCreateCe77Contract(HeaderFields fields, Block header, List<SpecIssue> issues,
        out SpecFileContract? contract)
    {
        contract = null;
        if (!TryRequireContractField(fields.Provenance, "provenance", header, issues, out var provenance)
            || !TryRequireContractField(fields.MinimumCe, "minimum-ce", header, issues, out var minimumCe)
            || !TryRequireContractField(fields.Architecture, "architecture", header, issues, out var architecture)
            || !TryRequireContractField(fields.Thread, "thread", header, issues, out var thread)
            || !TryRequireContractField(fields.Ownership, "ownership", header, issues, out var ownership))
            return false;

        if (!ValidateContractValues(provenance, minimumCe, architecture, thread, ownership, issues)) return false;

        contract = new SpecFileContract(provenance.Value, minimumCe.Value, architecture.Value, thread.Value,
            ownership.Value);
        return true;
    }

    private static bool ValidateContractValues(SpecField provenance, SpecField minimumCe, SpecField architecture,
        SpecField thread, SpecField ownership, List<SpecIssue> issues)
    {
        // Do not short-circuit: one malformed evidence header must report every independently actionable value on the
        // AdditionalText. Otherwise fixing the first field would merely reveal the next one on a subsequent build.
        var isValid = TryValidateProvenance(provenance, issues);
        if (!TryValidateVersion(minimumCe, issues)) isValid = false;
        if (!TryValidateArchitecture(architecture, issues)) isValid = false;
        if (!TryValidateThread(thread, issues)) isValid = false;
        if (!TryValidateOwnership(ownership, issues)) isValid = false;
        return isValid;
    }

    private static bool TryValidateProvenance(SpecField field, List<SpecIssue> issues)
    {
        if (IsValidProvenance(field.Value)) return true;

        issues.Add(new SpecIssue(field.Line,
            "'" + field.Value + "' is not a valid provenance: use a proof status followed by ': '.",
            field.ValueColumn));
        return false;
    }

    private static bool TryValidateVersion(SpecField field, List<SpecIssue> issues)
    {
        if (IsFourPartVersion(field.Value)) return true;

        issues.Add(new SpecIssue(field.Line,
            "'" + field.Value + "' is not a valid minimum CE version: expected four decimal parts.",
            field.ValueColumn));
        return false;
    }

    private static bool TryValidateArchitecture(SpecField field, List<SpecIssue> issues)
    {
        if (string.Equals(field.Value, "x64", StringComparison.Ordinal)) return true;

        issues.Add(new SpecIssue(field.Line,
            "'" + field.Value + "' is not a supported Engine API architecture: expected 'x64'.", field.ValueColumn));
        return false;
    }

    private static bool TryValidateThread(SpecField field, List<SpecIssue> issues)
    {
        if (IsThreadAffinity(field.Value)) return true;

        issues.Add(new SpecIssue(field.Line,
            "'" + field.Value + "' is not a valid thread contract: expected 'any', 'main' or 'unknown'.",
            field.ValueColumn));
        return false;
    }

    private static bool TryValidateOwnership(SpecField field, List<SpecIssue> issues)
    {
        if (IsOwnership(field.Value)) return true;

        issues.Add(new SpecIssue(field.Line,
            "'" + field.Value + "' is not a valid ownership contract: expected 'none', 'borrowed' or 'owned'.",
            field.ValueColumn));
        return false;
    }

    private static bool TryRequireContractField(SpecField? field, string key, Block header, List<SpecIssue> issues,
        out SpecField value)
    {
        value = field.GetValueOrDefault();
        if (field is not null && value.Value.Length > 0) return true;

        var line = field?.Line ?? header.StartLine;
        var column = field?.ValueColumn ?? header.StartColumn;
        issues.Add(new SpecIssue(line,
            "A 'contract: ce77' header is missing required key '" + key + "', or its value is empty.", column));
        return false;
    }

    private static bool IsValidProvenance(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0 || colon == value.Length - 1 || value[(colon + 1)..].Trim().Length == 0) return false;

        var status = value[..colon];
        return string.Equals(status, "ExactBinary", StringComparison.Ordinal)
               || string.Equals(status, "ExactInstalledFile", StringComparison.Ordinal)
               || string.Equals(status, "PinnedUpstream", StringComparison.Ordinal)
               || string.Equals(status, "ObservedLive", StringComparison.Ordinal)
               || string.Equals(status, "Inferred", StringComparison.Ordinal)
               || string.Equals(status, "Unknown", StringComparison.Ordinal);
    }

    private static bool IsFourPartVersion(string value)
    {
        var parts = 1;
        var digitsInPart = 0;
        foreach (var character in value)
        {
            if (character == '.')
            {
                if (digitsInPart == 0 || parts == 4) return false;

                parts++;
                digitsInPart = 0;
                continue;
            }

            if (character < '0' || character > '9') return false;

            digitsInPart++;
        }

        return parts == 4 && digitsInPart > 0;
    }

    private static bool IsThreadAffinity(string value)
    {
        return string.Equals(value, "any", StringComparison.Ordinal)
               || string.Equals(value, "main", StringComparison.Ordinal)
               || string.Equals(value, "unknown", StringComparison.Ordinal);
    }

    private static bool IsOwnership(string value)
    {
        return string.Equals(value, "none", StringComparison.Ordinal)
               || string.Equals(value, "borrowed", StringComparison.Ordinal)
               || string.Equals(value, "owned", StringComparison.Ordinal);
    }

    private static bool IsNilSemantics(string value)
    {
        return string.Equals(value, "none", StringComparison.Ordinal)
               || string.Equals(value, "absence", StringComparison.Ordinal)
               || string.Equals(value, "expected-failure", StringComparison.Ordinal)
               || string.Equals(value, "lua-error", StringComparison.Ordinal);
    }

    private static SpecCallModel? ParseEntry(Block block, List<SpecIssue> issues, SpecFileContract? fileContract)
    {
        if (block.Malformed)
        {
            issues.Add(new SpecIssue(block.StartLine,
                "The entry contains a malformed line; the whole entry was skipped.", block.StartColumn));
            return null;
        }

        if (!ReadEntryFields(block, issues, out var fields)) return null;

        if (!ValidateRequiredText(fields, block.StartLine, fileContract is not null, issues, out var isTry,
                out var isThrowing))
            return null;

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

        if (!ValidateParameterAndLocalIdentities(arguments, results, call, block.StartLine, issues)) return null;

        return CreateSpecCallModel(block, fields, call, fileContract);
    }

    private static SpecCallModel CreateSpecCallModel(Block block, EntryFields fields, LuaGlobalCallModel call,
        SpecFileContract? fileContract)
    {
        var contract = fileContract is null
            ? null
            : new SpecContract(
                fileContract.Provenance,
                fileContract.MinimumCheatEngineVersion,
                fileContract.Architecture,
                fileContract.ThreadAffinity,
                fileContract.Ownership,
                fields.NilSemantics!);

        return new SpecCallModel(block.StartLine, fields.MethodLine, fields.MethodColumn, fields.GlobalLine,
            fields.GlobalColumn, fields.Doc!, call, contract);
    }

    private static bool ReadEntryFields(Block block, List<SpecIssue> issues, out EntryFields fields)
    {
        fields = new EntryFields();
        HashSet<string> singular = new(StringComparer.Ordinal);
        var ok = true;

        foreach (var field in block.Fields)
            if (!TrySetEntryField(fields, singular, field, issues))
                ok = false;

        return ok;
    }

    private static bool TrySetEntryField(EntryFields fields, HashSet<string> singular, SpecField field,
        List<SpecIssue> issues)
    {
        switch (field.Key)
        {
            case "global": return TrySetGlobal(fields, singular, field, issues);
            case "method": return TrySetMethod(fields, singular, field, issues);
            case "form": return TrySetForm(fields, singular, field, issues);
            case "doc": return TrySetDoc(fields, singular, field, issues);
            case "nil": return TrySetNil(fields, singular, field, issues);
            case "return": return TrySetReturn(fields, singular, field, issues);
            case "arg":
                fields.ArgTokens.Add((field.Line, field.ValueColumn, field.Value));
                return true;
            case "fixed":
                fields.FixedTokens.Add((field.Line, field.ValueColumn, field.Value));
                return true;
            case "result":
                fields.ResultTokens.Add((field.Line, field.ValueColumn, field.Value));
                return true;
            default:
                issues.Add(new SpecIssue(field.Line, "Unknown entry key '" + field.Key + "'.", field.KeyColumn));
                return false;
        }
    }

    private static bool TrySetGlobal(EntryFields fields, HashSet<string> singular, SpecField field,
        List<SpecIssue> issues)
    {
        fields.Global = field.Value;
        fields.GlobalLine = field.Line;
        fields.GlobalColumn = field.ValueColumn;
        return RequireOnce(singular, "global", field.Line, field.KeyColumn, issues);
    }

    private static bool TrySetMethod(EntryFields fields, HashSet<string> singular, SpecField field,
        List<SpecIssue> issues)
    {
        fields.Method = field.Value;
        fields.MethodLine = field.Line;
        fields.MethodColumn = field.ValueColumn;
        return RequireOnce(singular, "method", field.Line, field.KeyColumn, issues);
    }

    private static bool TrySetForm(EntryFields fields, HashSet<string> singular, SpecField field,
        List<SpecIssue> issues)
    {
        fields.Form = field.Value;
        fields.FormLine = field.Line;
        fields.FormColumn = field.ValueColumn;
        return RequireOnce(singular, "form", field.Line, field.KeyColumn, issues);
    }

    private static bool TrySetDoc(EntryFields fields, HashSet<string> singular, SpecField field, List<SpecIssue> issues)
    {
        fields.Doc = field.Value;
        return RequireOnce(singular, "doc", field.Line, field.KeyColumn, issues);
    }

    private static bool TrySetNil(EntryFields fields, HashSet<string> singular, SpecField field, List<SpecIssue> issues)
    {
        fields.NilSemantics = field.Value;
        fields.NilLine = field.Line;
        fields.NilColumn = field.ValueColumn;
        return RequireOnce(singular, "nil", field.Line, field.KeyColumn, issues);
    }

    private static bool TrySetReturn(EntryFields fields, HashSet<string> singular, SpecField field,
        List<SpecIssue> issues)
    {
        fields.ReturnToken = field.Value;
        fields.ReturnLine = field.Line;
        fields.ReturnColumn = field.ValueColumn;
        fields.SawReturn = true;
        return RequireOnce(singular, "return", field.Line, field.KeyColumn, issues);
    }

    private static bool ValidateRequiredText(EntryFields fields, int startLine, bool requiresCe77Contract,
        List<SpecIssue> issues, out bool isTry, out bool isThrowing)
    {
        isTry = false;
        isThrowing = false;

        if (!ValidateRequiredPresence(fields, startLine, issues)
            || !ValidateNilContract(fields, startLine, requiresCe77Contract, issues))
            return false;

        if (!LuaNames.IsValidName(fields.Global))
        {
            issues.Add(new SpecIssue(fields.GlobalLine, "'" + fields.Global + "' is not a valid Lua global name.",
                fields.GlobalColumn));
            return false;
        }

        if (!SpecIdentifiers.IsValidIdentifier(fields.Method))
        {
            issues.Add(new SpecIssue(fields.MethodLine, "'" + fields.Method + "' is not a valid C# method name.",
                fields.MethodColumn));
            return false;
        }

        isTry = string.Equals(fields.Form, "try", StringComparison.Ordinal);
        isThrowing = string.Equals(fields.Form, "throwing", StringComparison.Ordinal);
        if (!isTry && !isThrowing)
        {
            issues.Add(new SpecIssue(fields.FormLine,
                "'" + fields.Form + "' is not a valid form: expected 'try' or 'throwing'.", fields.FormColumn));
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
            issues.Add(new SpecIssue(fields.ReturnLine, "'" + fields.ReturnToken + "' is not a valid return kind.",
                fields.ReturnColumn));
            return false;
        }

        if (!LuaValueKinds.CanBeResult(kind))
        {
            issues.Add(new SpecIssue(fields.ReturnLine,
                "'" + fields.ReturnToken +
                "' cannot be a return type: the span would dangle once the stack is restored.", fields.ReturnColumn));
            return false;
        }

        returnKind = kind;
        returnIsNullable = nullable;
        return true;
    }

    private static List<LuaArgumentModel>? ParseArguments(List<(int Line, int Column, string Value)> tokens,
        List<SpecIssue> issues)
    {
        List<LuaArgumentModel> arguments = new(tokens.Count);
        foreach (var (line, column, value) in tokens)
        {
            if (!TryParseNamedValue(value, out var name, out var kindToken)
                || !SpecIdentifiers.IsValidIdentifier(name)
                || !SpecValueKinds.TryParse(kindToken, out var kind, out var nullable))
            {
                issues.Add(new SpecIssue(line, "'" + value + "' is not a valid 'name:kind' argument.", column));
                return null;
            }

            arguments.Add(new LuaArgumentModel(SpecIdentifiers.Escape(name), kind, nullable));
        }

        return arguments;
    }

    // A fixed argument has the narrow, host-facing grammar 'kind:value'. It is pushed in call order but deliberately
    // omitted from the generated C# signature. Only boolean literals are needed by the curated CE surface today; keep
    // that vocabulary explicit rather than accepting arbitrary C# expressions in a repository text file.
    private static List<LuaArgumentModel>? ParseFixedArguments(List<(int Line, int Column, string Value)> tokens,
        List<SpecIssue> issues)
    {
        List<LuaArgumentModel> arguments = new(tokens.Count);
        foreach (var (line, column, value) in tokens)
        {
            if (!TryParseNamedValue(value, out var kindToken, out var literal)
                || !string.Equals(kindToken, "boolean", StringComparison.Ordinal)
                || !(string.Equals(literal, "true", StringComparison.Ordinal)
                     || string.Equals(literal, "false", StringComparison.Ordinal)))
            {
                issues.Add(new SpecIssue(line,
                    "'" + value + "' is not a valid fixed argument: expected 'boolean:true' or 'boolean:false'.",
                    column));
                return null;
            }

            arguments.Add(new LuaArgumentModel(literal, LuaValueKind.Boolean, false, FixedValue: literal));
        }

        return arguments;
    }

    private static List<LuaResultModel>? ParseResults(List<(int Line, int Column, string Value)> tokens,
        List<SpecIssue> issues)
    {
        List<LuaResultModel> results = new(tokens.Count);
        foreach (var (line, column, value) in tokens)
        {
            if (!TryParseNamedValue(value, out var name, out var kindToken)
                || !SpecIdentifiers.IsValidIdentifier(name)
                || !SpecValueKinds.TryParse(kindToken, out var kind, out var nullable))
            {
                issues.Add(new SpecIssue(line, "'" + value + "' is not a valid 'name:kind' result.", column));
                return null;
            }

            if (!LuaValueKinds.CanBeResult(kind))
            {
                issues.Add(new SpecIssue(line,
                    "'" + kindToken + "' cannot be a result: the span would dangle once the stack is restored.",
                    column));
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

    private static bool RequireOnce(HashSet<string> seen, string key, int line, int column, List<SpecIssue> issues)
    {
        if (seen.Add(key)) return true;

        issues.Add(new SpecIssue(line, "Duplicate entry key '" + key + "'.", column));
        return false;
    }

    // A method name reused by more than one entry cannot be emitted (CS0111): every entry using it is dropped, one
    // issue per line, mirroring CheatEngine.SDK.SourceGenerators.LuaBindings' duplicate-Lua-name rule (both members dropped).
    private static List<SpecCallModel> DropDuplicateMethodNames(List<SpecCallModel> entries,
        List<SpecIssue> issues)
    {
        Dictionary<string, List<int>> linesByMethod = new(StringComparer.Ordinal);
        foreach (var call in entries)
        {
            var name = call.Call.MethodName;
            if (!linesByMethod.TryGetValue(name, out var lines))
            {
                lines = [];
                linesByMethod.Add(name, lines);
            }

            lines.Add(call.Line);
        }

        List<SpecCallModel> result = new(entries.Count);
        foreach (var call in entries)
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

    private static bool ValidateParameterAndLocalIdentities(
        List<LuaArgumentModel> arguments,
        List<LuaResultModel> results,
        LuaGlobalCallModel call,
        int line,
        List<SpecIssue> issues)
    {
        Dictionary<string, byte> parameters = new(StringComparer.Ordinal);
        foreach (var argument in arguments)
            if (!argument.IsFixed && parameters.ContainsKey(argument.Name))
            {
                issues.Add(new SpecIssue(line,
                    "Generated parameter '" + argument.Name + "' is declared more than once in this entry."));
                return false;
            }
            else if (!argument.IsFixed)
            {
                parameters.Add(argument.Name, 0);
            }

        foreach (var result in results)
            if (parameters.ContainsKey(result.Name))
            {
                issues.Add(new SpecIssue(line,
                    "Generated parameter '" + result.Name + "' is declared more than once in this entry."));
                return false;
            }
            else
            {
                parameters.Add(result.Name, 0);
            }

        foreach (var name in parameters.Keys)
            if (IsReservedBodyLocal(name, call))
            {
                issues.Add(new SpecIssue(line,
                    "Generated parameter '" + name + "' conflicts with a reserved local in the emitted wrapper."));
                return false;
            }

        return true;
    }

    private static bool ValidateRequiredPresence(EntryFields fields, int startLine, List<SpecIssue> issues)
    {
        if (string.IsNullOrEmpty(fields.Global)) return ReportMissingEntryKey("global", startLine, issues);
        if (string.IsNullOrEmpty(fields.Method)) return ReportMissingEntryKey("method", startLine, issues);
        if (string.IsNullOrEmpty(fields.Form)) return ReportMissingEntryKey("form", startLine, issues);
        if (string.IsNullOrEmpty(fields.Doc)) return ReportMissingEntryKey("doc", startLine, issues);

        return true;
    }

    private static bool ReportMissingEntryKey(string key, int startLine, List<SpecIssue> issues)
    {
        issues.Add(new SpecIssue(startLine, "The entry is missing required key '" + key + "'."));
        return false;
    }

    private static bool ValidateNilContract(EntryFields fields, int startLine, bool requiresCe77Contract,
        List<SpecIssue> issues)
    {
        if (requiresCe77Contract && string.IsNullOrEmpty(fields.NilSemantics))
        {
            issues.Add(new SpecIssue(startLine, "A 'contract: ce77' entry is missing required key 'nil'."));
            return false;
        }

        if (!requiresCe77Contract && fields.NilSemantics is not null)
        {
            issues.Add(new SpecIssue(fields.NilLine,
                "Entry key 'nil' requires header 'contract: ce77'.", fields.NilColumn));
            return false;
        }

        if (!requiresCe77Contract || IsNilSemantics(fields.NilSemantics!)) return true;

        issues.Add(new SpecIssue(fields.NilLine,
            "'" + fields.NilSemantics +
            "' is not a valid nil contract: expected 'none', 'absence', 'expected-failure' or 'lua-error'.",
            fields.NilColumn));
        return false;
    }

    private static bool IsReservedBodyLocal(string name, LuaGlobalCallModel call)
    {
        if (string.Equals(name, "__L", StringComparison.Ordinal)
            || string.Equals(name, "__operation", StringComparison.Ordinal)
            || string.Equals(name, "__top", StringComparison.Ordinal)
            || string.Equals(name, "__ok", StringComparison.Ordinal)
            || string.Equals(name, "__status", StringComparison.Ordinal)
            || string.Equals(name, "__result", StringComparison.Ordinal))
            return true;

        if (!UsesAddressFacade(call)) return false;

        if (string.Equals(name, "__engineApiSucceeded", StringComparison.Ordinal)
            || string.Equals(name, "__engineApiRawResult", StringComparison.Ordinal))
            return true;

        if (call.Form != LuaCallForm.Try) return false;

        for (var i = 0; i < call.Results.Length; i++)
            if (call.Results[i].Kind == LuaValueKind.Address
                && string.Equals(name, RawResultName(i), StringComparison.Ordinal))
                return true;

        return false;
    }

    private static List<SpecCallModel> DropGeneratedMemberCollisions(List<SpecCallModel> entries,
        List<SpecIssue> issues)
    {
        Dictionary<string, List<SpecCallModel>> owners = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            AddGeneratedMemberOwner(owners, entry.Call.MethodName, entry);
            if (UsesAddressFacade(entry.Call))
                AddGeneratedMemberOwner(owners, CoreMethodName(entry.Call.MethodName), entry);
        }

        HashSet<SpecCallModel> invalid = [];
        foreach (var pair in owners)
        {
            if (pair.Value.Count < 2) continue;

            foreach (var entry in pair.Value)
            {
                invalid.Add(entry);
                issues.Add(new SpecIssue(entry.Line,
                    "Generated member '" + pair.Key + "' conflicts with another member emitted from this spec file."));
            }
        }

        if (invalid.Count == 0) return entries;

        List<SpecCallModel> valid = new(entries.Count - invalid.Count);
        foreach (var entry in entries)
            if (!invalid.Contains(entry))
                valid.Add(entry);

        return valid;
    }

    private static List<SpecCallModel> DropCacheMemberCollisions(List<SpecCallModel> entries,
        List<SpecIssue> issues)
    {
        HashSet<string> cacheFields = new(StringComparer.Ordinal);
        foreach (var entry in entries) cacheFields.Add(LuaGlobalCallModel.CacheFieldFor(entry.Call.GlobalName));

        List<SpecCallModel> valid = new(entries.Count);
        foreach (var entry in entries)
        {
            var conflicts = cacheFields.Contains(entry.Call.MethodName)
                            || (UsesAddressFacade(entry.Call) &&
                                cacheFields.Contains(CoreMethodName(entry.Call.MethodName)));
            if (!conflicts)
            {
                valid.Add(entry);
                continue;
            }

            issues.Add(new SpecIssue(entry.Line,
                "Generated method '" + entry.Call.MethodName + "' conflicts with a generated Lua-global cache field."));
        }

        return valid;
    }

    private static List<SpecCallModel> DropTypeMemberCollisions(List<SpecCallModel> entries, string typeName,
        List<SpecIssue> issues)
    {
        if (typeName.Length == 0) return entries;

        List<SpecCallModel> valid = new(entries.Count);
        foreach (var entry in entries)
        {
            var conflicts = string.Equals(entry.Call.MethodName, typeName, StringComparison.Ordinal)
                            || (UsesAddressFacade(entry.Call)
                                && string.Equals(CoreMethodName(entry.Call.MethodName), typeName,
                                    StringComparison.Ordinal));
            if (!conflicts)
            {
                valid.Add(entry);
                continue;
            }

            issues.Add(new SpecIssue(entry.Line,
                "Generated method '" + entry.Call.MethodName + "' conflicts with its containing type '" + typeName +
                "'."));
        }

        return valid;
    }

    private static void AddGeneratedMemberOwner(Dictionary<string, List<SpecCallModel>> owners, string name,
        SpecCallModel entry)
    {
        if (!owners.TryGetValue(name, out var entries))
        {
            entries = [];
            owners.Add(name, entries);
        }

        entries.Add(entry);
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

    private static string RawResultName(int index)
    {
        return "__engineApiRawResult" + index.ToString(CultureInfo.InvariantCulture);
    }

    // One "key: value" line. A line without a colon (or an empty key) marks the whole block Malformed: the block is
    // still collected (so the caller can report one issue at its start line) but ParseEntry/ParseHeader never look
    // at a malformed block's fields.
    private sealed class Block
    {
        public readonly List<SpecField> Fields = [];
        public bool Malformed;
        public int StartColumn;
        public int StartLine;

        public bool IsEmpty => Fields.Count == 0 && !Malformed;
    }

    // Header fields stay as source-positioned values until the ce77 contract has been validated, so every grammar
    // diagnostic points at the additional-file key or value that needs correction.
    private sealed class HeaderFields
    {
        public SpecField? Architecture;
        public SpecField? ContractSchema;
        public SpecField? MinimumCe;
        public SpecField? Namespace;
        public SpecField? Ownership;
        public SpecField? Provenance;
        public SpecField? Thread;
        public SpecField? Type;

        public SpecField? FirstContractField => Provenance ?? MinimumCe ?? Architecture ?? Thread ?? Ownership;
    }

    // The raw fields of one entry block, read once by ReadEntryFields and consumed by the validators below.
    private sealed class EntryFields
    {
        public readonly List<(int Line, int Column, string Value)> ArgTokens = [];
        public readonly List<(int Line, int Column, string Value)> FixedTokens = [];
        public readonly List<(int Line, int Column, string Value)> ResultTokens = [];
        public string? Doc;
        public string? Form;
        public int FormColumn;
        public int FormLine;
        public string? Global;
        public int GlobalColumn;
        public int GlobalLine;
        public string? Method;
        public int MethodColumn;
        public int MethodLine;
        public int NilColumn;
        public int NilLine;
        public string? NilSemantics;
        public int ReturnColumn;
        public int ReturnLine;
        public string? ReturnToken;
        public bool SawReturn;
    }

    private readonly record struct SpecField(int Line, int KeyColumn, int ValueColumn, string Key, string Value);
}
