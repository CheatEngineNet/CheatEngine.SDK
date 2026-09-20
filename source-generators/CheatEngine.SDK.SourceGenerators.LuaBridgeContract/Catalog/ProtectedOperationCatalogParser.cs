using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

/// <summary>Strict, Roslyn-free JSON parser and contract validator for the protected operation catalogue.</summary>
internal static class ProtectedOperationCatalogParser
{
    private const string FileName = "protected-operations.json";
    private const string ExpectedCatalogId = "cheatengine-sdk-lua-protected-operations";

    public static bool IsCatalogFile(string path)
    {
        if (path is null) return false;

        var slash = path.LastIndexOf('/');
        var backslash = path.LastIndexOf('\\');
        var separator = slash > backslash ? slash : backslash;
        return string.Equals(path[(separator + 1)..], FileName, StringComparison.OrdinalIgnoreCase);
    }

    public static CatalogParseResult Parse(CatalogInput input)
    {
        try
        {
            return ParseCore(input);
        }
        catch (Exception exception)
        {
            var reader = new JsonReader(input.Path, input.Text);
            return Failure(input.Path, reader.CreateDiagnostic(new TextSpan(0, 0),
                "The catalogue parser recovered an unexpected " + exception.GetType().Name + "."));
        }
    }

    private static CatalogParseResult ParseCore(CatalogInput input)
    {
        var reader = new JsonReader(input.Path, input.Text);
        if (!reader.TryParse(out var root, out var syntaxDiagnostic))
            return Failure(input.Path, syntaxDiagnostic!);

        if (root is not JsonObject rootObject)
            return Failure(input.Path, reader.CreateDiagnostic(root.Span, "The catalogue root must be a JSON object."));

        var diagnostics = ImmutableArray.CreateBuilder<CatalogDiagnostic>();
        ValidateExactNumber(rootObject, "schemaVersion", 1, reader, diagnostics);
        ValidateExactString(rootObject, "catalogId", ExpectedCatalogId, reader, diagnostics);

        var bridgeContract = RequireObject(rootObject, "bridgeContract", reader, diagnostics);
        ValidateBridgeContract(bridgeContract, reader, diagnostics);

        var operationsValue = Require(rootObject, "operations", reader, diagnostics);
        if (operationsValue is not JsonArray operationsArray)
        {
            if (operationsValue is not null)
                diagnostics.Add(reader.CreateDiagnostic(operationsValue.Span,
                    "Property 'operations' must be a JSON array."));
            return new CatalogParseResult(input.Path, null, diagnostics.ToImmutable());
        }

        return ParseOperations(input.Path, bridgeContract, operationsArray, reader, diagnostics);
    }

    private static void ValidateBridgeContract(
        JsonObject? bridgeContract,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        if (bridgeContract is null) return;
        ValidateExactNumber(bridgeContract, "abiMajor", 1, reader, diagnostics);
        ValidateExactNumber(bridgeContract, "minimumAbiMinor", 1, reader, diagnostics);
        ValidateExactNumber(bridgeContract, "operationBitmapWidth", 64, reader, diagnostics);
    }

    private static CatalogParseResult ParseOperations(
        string sourcePath,
        JsonObject? bridgeContract,
        JsonArray operationsArray,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        if (operationsArray.Items.Count == 0)
            diagnostics.Add(reader.CreateDiagnostic(operationsArray.Span,
                "Property 'operations' must contain at least one protected operation."));

        var operations = new List<CatalogOperation>();
        var ids = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
        var opcodes = new Dictionary<int, JsonValue>();
        ulong bitmap = 0;
        for (var i = 0; i < operationsArray.Items.Count; i++)
            ParseOperation(operationsArray.Items[i], reader, diagnostics, operations, ids, opcodes, ref bitmap);

        if (bridgeContract is not null) ValidateBitmap(bridgeContract, bitmap, reader, diagnostics);
        if (diagnostics.Count > 0) return new CatalogParseResult(sourcePath, null, diagnostics.ToImmutable());

        operations.Sort(static (left, right) =>
        {
            var opcode = left.Opcode.CompareTo(right.Opcode);
            return opcode != 0 ? opcode : string.CompareOrdinal(left.Id, right.Id);
        });
        return new CatalogParseResult(
            sourcePath,
            new CatalogModel(sourcePath, ImmutableArray.CreateRange(operations), bitmap),
            ImmutableArray<CatalogDiagnostic>.Empty);
    }

    private static CatalogParseResult Failure(string sourcePath, CatalogDiagnostic diagnostic)
    {
        return new CatalogParseResult(sourcePath, null, ImmutableArray.Create(diagnostic));
    }

    private static void ParseOperation(
        JsonValue value,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics,
        List<CatalogOperation> operations,
        Dictionary<string, JsonValue> ids,
        Dictionary<int, JsonValue> opcodes,
        ref ulong bitmap)
    {
        if (value is not JsonObject operation)
        {
            diagnostics.Add(reader.CreateDiagnostic(value.Span, "Every item in 'operations' must be a JSON object."));
            return;
        }

        var idValue = RequireString(operation, "id", reader, diagnostics);
        var opcodeValue = RequireNumber(operation, "opcode", reader, diagnostics);
        ValidateExactTrue(operation, "protected", reader, diagnostics);
        ValidateExactTrue(operation, "requiresNativeProtection", reader, diagnostics);
        var managed = RequireObject(operation, "managed", reader, diagnostics);
        var managedConstant = managed is null ? null : RequireString(managed, "constant", reader, diagnostics);
        if (managed is not null) _ = RequireString(managed, "wrapper", reader, diagnostics);

        if (idValue is null || opcodeValue is null) return;

        if (!IsPascalIdentifier(idValue.Text))
        {
            diagnostics.Add(reader.CreateDiagnostic(idValue.Span,
                "Operation 'id' must be a PascalCase ASCII identifier."));
            return;
        }

        if (managedConstant is not null &&
            !string.Equals(managedConstant.Text, idValue.Text + "Operation", StringComparison.Ordinal))
            diagnostics.Add(reader.CreateDiagnostic(managedConstant.Span,
                "Property 'managed.constant' must be '" + idValue.Text + "Operation' for operation '" + idValue.Text +
                "'."));

        if (!int.TryParse(opcodeValue.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var opcode)
            || opcode < 0
            || opcode > 63)
        {
            diagnostics.Add(reader.CreateDiagnostic(opcodeValue.Span,
                "Operation 'opcode' must be an integer between 0 and 63."));
            return;
        }

        if (ids.TryGetValue(idValue.Text, out var firstId))
        {
            diagnostics.Add(reader.CreateDiagnostic(idValue.Span,
                "Operation id '" + idValue.Text + "' duplicates an earlier operation."));
            diagnostics.Add(reader.CreateDiagnostic(firstId.Span,
                "Operation id '" + idValue.Text + "' is duplicated."));
            return;
        }

        if (opcodes.TryGetValue(opcode, out var firstOpcode))
        {
            diagnostics.Add(reader.CreateDiagnostic(opcodeValue.Span,
                "Operation opcode '" + opcode.ToString(CultureInfo.InvariantCulture) +
                "' duplicates an earlier operation."));
            diagnostics.Add(reader.CreateDiagnostic(firstOpcode.Span,
                "Operation opcode '" + opcode.ToString(CultureInfo.InvariantCulture) + "' is duplicated."));
            return;
        }

        ids.Add(idValue.Text, idValue);
        opcodes.Add(opcode, opcodeValue);
        bitmap |= 1UL << opcode;
        operations.Add(new CatalogOperation(idValue.Text, opcode));
    }

    private static void ValidateBitmap(
        JsonObject bridgeContract,
        ulong calculatedBitmap,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var value = RequireString(bridgeContract, "operationBitmap", reader, diagnostics);
        if (value is null) return;

        if (!TryParseBitmap(value.Text, out var expected))
        {
            diagnostics.Add(reader.CreateDiagnostic(value.Span,
                "Property 'bridgeContract.operationBitmap' must have the form 0x followed by exactly 16 uppercase hexadecimal digits."));
            return;
        }

        if (expected != calculatedBitmap)
            diagnostics.Add(reader.CreateDiagnostic(value.Span,
                "Property 'bridgeContract.operationBitmap' is " + value.Text +
                " but the declared operation opcodes require " +
                "0x" + calculatedBitmap.ToString("X16", CultureInfo.InvariantCulture) + "."));
    }

    private static bool TryParseBitmap(string value, out ulong bitmap)
    {
        bitmap = 0;
        if (value.Length != 18 || value[0] != '0' || value[1] != 'x') return false;

        for (var i = 2; i < value.Length; i++)
        {
            var character = value[i];
            int digit;
            if (character is >= '0' and <= '9') digit = character - '0';
            else if (character is >= 'A' and <= 'F') digit = character - 'A' + 10;
            else return false;
            bitmap = (bitmap << 4) | (uint)digit;
        }

        return true;
    }

    private static void ValidateExactNumber(
        JsonObject value,
        string name,
        int expected,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var number = RequireNumber(value, name, reader, diagnostics);
        if (number is null) return;
        if (!string.Equals(number.Text, expected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            diagnostics.Add(reader.CreateDiagnostic(number.Span,
                "Property '" + name + "' must be " + expected.ToString(CultureInfo.InvariantCulture) + "."));
    }

    private static void ValidateExactString(
        JsonObject value,
        string name,
        string expected,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var text = RequireString(value, name, reader, diagnostics);
        if (text is null) return;
        if (!string.Equals(text.Text, expected, StringComparison.Ordinal))
            diagnostics.Add(reader.CreateDiagnostic(text.Span,
                "Property '" + name + "' must be '" + expected + "'."));
    }

    private static void ValidateExactTrue(
        JsonObject value,
        string name,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var item = Require(value, name, reader, diagnostics);
        if (item is null) return;
        if (item is not JsonBoolean { Value: true })
            diagnostics.Add(reader.CreateDiagnostic(item.Span, "Property '" + name + "' must be true."));
    }

    private static JsonObject? RequireObject(
        JsonObject value,
        string name,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var item = Require(value, name, reader, diagnostics);
        if (item is null) return null;
        if (item is JsonObject result) return result;
        diagnostics.Add(reader.CreateDiagnostic(item.Span, "Property '" + name + "' must be a JSON object."));
        return null;
    }

    private static JsonString? RequireString(
        JsonObject value,
        string name,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var item = Require(value, name, reader, diagnostics);
        if (item is null) return null;
        if (item is JsonString result) return result;
        diagnostics.Add(reader.CreateDiagnostic(item.Span, "Property '" + name + "' must be a JSON string."));
        return null;
    }

    private static JsonNumber? RequireNumber(
        JsonObject value,
        string name,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        var item = Require(value, name, reader, diagnostics);
        if (item is null) return null;
        if (item is JsonNumber result) return result;
        diagnostics.Add(reader.CreateDiagnostic(item.Span, "Property '" + name + "' must be a JSON number."));
        return null;
    }

    private static JsonValue? Require(
        JsonObject value,
        string name,
        JsonReader reader,
        ImmutableArray<CatalogDiagnostic>.Builder diagnostics)
    {
        if (value.TryGet(name, out var item)) return item;
        diagnostics.Add(reader.CreateDiagnostic(value.Span, "Property '" + name + "' is required."));
        return null;
    }

    private static bool IsPascalIdentifier(string value)
    {
        if (value.Length == 0 || value[0] is < 'A' or > 'Z') return false;
        for (var i = 1; i < value.Length; i++)
        {
            var character = value[i];
            if (character is < 'A' or > 'Z' && character is < 'a' or > 'z' && character is < '0' or > '9')
                return false;
        }

        return true;
    }

    private abstract class JsonValue(TextSpan span)
    {
        public TextSpan Span { get; } = span;
    }

    private sealed class JsonObject(TextSpan span, List<JsonProperty> properties) : JsonValue(span)
    {
        public bool TryGet(string name, out JsonValue value)
        {
            for (var i = 0; i < properties.Count; i++)
                if (string.Equals(properties[i].Name, name, StringComparison.Ordinal))
                {
                    value = properties[i].Value;
                    return true;
                }

            value = null!;
            return false;
        }
    }

    private sealed class JsonArray(TextSpan span, List<JsonValue> items) : JsonValue(span)
    {
        public List<JsonValue> Items { get; } = items;
    }

    private sealed class JsonString(TextSpan span, string text) : JsonValue(span)
    {
        public string Text { get; } = text;
    }

    private sealed class JsonNumber(TextSpan span, string text) : JsonValue(span)
    {
        public string Text { get; } = text;
    }

    private sealed class JsonBoolean(TextSpan span, bool value) : JsonValue(span)
    {
        public bool Value { get; } = value;
    }

    private sealed class JsonNull(TextSpan span) : JsonValue(span);

    private sealed record JsonProperty(string Name, JsonValue Value);

    private sealed class JsonReader
    {
        private readonly string _path;
        private readonly string _text;
        private int _position;

        public JsonReader(string path, string text)
        {
            _path = path;
            _text = text;
        }

        public CatalogDiagnostic CreateDiagnostic(TextSpan span, string message)
        {
            var start = GetLinePosition(span.Start);
            var end = GetLinePosition(span.End);
            return new CatalogDiagnostic(_path, span, new LinePositionSpan(start, end), message, false);
        }

        public bool TryParse(out JsonValue root, out CatalogDiagnostic? diagnostic)
        {
            root = null!;
            diagnostic = null;
            SkipWhitespace();
            if (!TryParseValue(out root, out diagnostic)) return false;
            SkipWhitespace();
            if (_position == _text.Length) return true;

            diagnostic = Error("Unexpected content after the JSON root value.");
            return false;
        }

        private bool TryParseValue(out JsonValue value, out CatalogDiagnostic? diagnostic)
        {
            value = null!;
            diagnostic = null;
            if (_position >= _text.Length)
            {
                diagnostic = Error("Expected a JSON value.");
                return false;
            }

            return _text[_position] switch
            {
                '{' => TryParseObject(out value, out diagnostic),
                '[' => TryParseArray(out value, out diagnostic),
                '"' => TryParseStringValue(out value, out diagnostic),
                't' => TryParseLiteral("true", true, out value, out diagnostic),
                'f' => TryParseLiteral("false", false, out value, out diagnostic),
                'n' => TryParseNull(out value, out diagnostic),
                '-' => TryParseNumber(out value, out diagnostic),
                >= '0' and <= '9' => TryParseNumber(out value, out diagnostic),
                _ => Fail(out value, out diagnostic, "Expected a JSON value.")
            };
        }

        private bool TryParseObject(out JsonValue value, out CatalogDiagnostic? diagnostic)
        {
            var start = _position++;
            var properties = new List<JsonProperty>();
            var names = new Dictionary<string, TextSpan>(StringComparer.Ordinal);
            diagnostic = null;
            SkipWhitespace();
            if (TryRead('}'))
            {
                value = new JsonObject(new TextSpan(start, _position - start), properties);
                return true;
            }

            while (true)
            {
                if (!TryParseString(out var name, out var nameSpan, out diagnostic))
                    return Fail(out value, out diagnostic, "Expected an object property name.");
                if (names.ContainsKey(name))
                {
                    value = null!;
                    diagnostic = CreateDiagnostic(nameSpan, "JSON object property '" + name + "' is duplicated.");
                    return false;
                }

                names.Add(name, nameSpan);
                SkipWhitespace();
                if (!TryRead(':'))
                    return Fail(out value, out diagnostic, "Expected ':' after an object property name.");
                SkipWhitespace();
                if (!TryParseValue(out var propertyValue, out diagnostic))
                {
                    value = null!;
                    return false;
                }

                properties.Add(new JsonProperty(name, propertyValue));
                SkipWhitespace();
                if (TryRead('}'))
                {
                    value = new JsonObject(new TextSpan(start, _position - start), properties);
                    return true;
                }

                if (!TryRead(','))
                    return Fail(out value, out diagnostic, "Expected ',' or '}' after an object property.");
                SkipWhitespace();
            }
        }

        private bool TryParseArray(out JsonValue value, out CatalogDiagnostic? diagnostic)
        {
            var start = _position++;
            var items = new List<JsonValue>();
            diagnostic = null;
            SkipWhitespace();
            if (TryRead(']'))
            {
                value = new JsonArray(new TextSpan(start, _position - start), items);
                return true;
            }

            while (true)
            {
                if (!TryParseValue(out var item, out diagnostic))
                {
                    value = null!;
                    return false;
                }

                items.Add(item);
                SkipWhitespace();
                if (TryRead(']'))
                {
                    value = new JsonArray(new TextSpan(start, _position - start), items);
                    return true;
                }

                if (!TryRead(',')) return Fail(out value, out diagnostic, "Expected ',' or ']' after an array item.");
                SkipWhitespace();
            }
        }

        private bool TryParseStringValue(out JsonValue value, out CatalogDiagnostic? diagnostic)
        {
            if (!TryParseString(out var text, out var span, out diagnostic))
            {
                value = null!;
                return false;
            }

            value = new JsonString(span, text);
            return true;
        }

        private bool TryParseString(out string value, out TextSpan span, out CatalogDiagnostic? diagnostic)
        {
            var start = _position;
            value = string.Empty;
            span = default;
            diagnostic = null;
            if (!TryRead('"'))
            {
                diagnostic = Error("Expected a JSON string.");
                return false;
            }

            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                var character = _text[_position++];
                if (character == '"')
                {
                    value = builder.ToString();
                    span = new TextSpan(start, _position - start);
                    return true;
                }

                if (character < ' ')
                {
                    diagnostic = Error("A JSON string cannot contain an unescaped control character.");
                    return false;
                }

                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }

                if (!TryAppendEscape(builder, out diagnostic)) return false;
            }

            diagnostic = Error("A JSON string is not terminated.");
            return false;
        }

        private bool TryAppendEscape(StringBuilder builder, out CatalogDiagnostic? diagnostic)
        {
            diagnostic = null;
            if (_position >= _text.Length)
            {
                diagnostic = Error("A JSON string escape is incomplete.");
                return false;
            }

            var escape = _text[_position++];
            switch (escape)
            {
                case '"':
                    builder.Append('"');
                    return true;
                case '\\':
                    builder.Append('\\');
                    return true;
                case '/':
                    builder.Append('/');
                    return true;
                case 'b':
                    builder.Append('\b');
                    return true;
                case 'f':
                    builder.Append('\f');
                    return true;
                case 'n':
                    builder.Append('\n');
                    return true;
                case 'r':
                    builder.Append('\r');
                    return true;
                case 't':
                    builder.Append('\t');
                    return true;
                case 'u':
                    if (TryReadUnicodeEscape(out var unicode))
                    {
                        builder.Append((char)unicode);
                        return true;
                    }

                    diagnostic = Error("A JSON Unicode escape must contain four hexadecimal digits.");
                    return false;
                default:
                    diagnostic = Error("A JSON string contains an invalid escape sequence.");
                    return false;
            }
        }

        private bool TryReadUnicodeEscape(out int value)
        {
            value = 0;
            if (_position > _text.Length - 4) return false;
            for (var i = 0; i < 4; i++)
            {
                var character = _text[_position++];
                if (character is >= '0' and <= '9') value = (value << 4) | (character - '0');
                else if (character is >= 'a' and <= 'f') value = (value << 4) | (character - 'a' + 10);
                else if (character is >= 'A' and <= 'F') value = (value << 4) | (character - 'A' + 10);
                else return false;
            }

            return true;
        }

        private bool TryParseLiteral(string literal, bool boolean, out JsonValue value,
            out CatalogDiagnostic? diagnostic)
        {
            var start = _position;
            if (!TryReadLiteral(literal))
                return Fail(out value, out diagnostic, "Invalid JSON literal.");

            value = new JsonBoolean(new TextSpan(start, literal.Length), boolean);
            diagnostic = null;
            return true;
        }

        private bool TryParseNull(out JsonValue value, out CatalogDiagnostic? diagnostic)
        {
            var start = _position;
            if (!TryReadLiteral("null"))
                return Fail(out value, out diagnostic, "Invalid JSON literal.");

            value = new JsonNull(new TextSpan(start, 4));
            diagnostic = null;
            return true;
        }

        private bool TryParseNumber(out JsonValue value, out CatalogDiagnostic? diagnostic)
        {
            var start = _position;
            if (TryRead('-') && _position == _text.Length)
                return Fail(out value, out diagnostic, "A JSON number cannot end after '-'.");

            if (TryRead('0'))
            {
                if (_position < _text.Length && IsDigit(_text[_position]))
                    return Fail(out value, out diagnostic, "A JSON number cannot have a leading zero.");
            }
            else if (!TryReadDigits())
            {
                return Fail(out value, out diagnostic, "A JSON number must contain digits.");
            }

            if (TryRead('.'))
                if (!TryReadDigits())
                    return Fail(out value, out diagnostic, "A JSON fractional part requires digits.");

            if (_position < _text.Length && (_text[_position] == 'e' || _text[_position] == 'E'))
            {
                _position++;
                if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-')) _position++;
                if (!TryReadDigits()) return Fail(out value, out diagnostic, "A JSON exponent requires digits.");
            }

            value = new JsonNumber(new TextSpan(start, _position - start), _text.Substring(start, _position - start));
            diagnostic = null;
            return true;
        }

        private bool TryReadDigits()
        {
            var start = _position;
            while (_position < _text.Length && IsDigit(_text[_position])) _position++;
            return _position > start;
        }

        private bool TryReadLiteral(string literal)
        {
            if (_position > _text.Length - literal.Length) return false;
            for (var i = 0; i < literal.Length; i++)
                if (_text[_position + i] != literal[i])
                    return false;
            _position += literal.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length)
            {
                var character = _text[_position];
                if (character != ' ' && character != '\t' && character != '\r' && character != '\n') return;
                _position++;
            }
        }

        private bool TryRead(char expected)
        {
            if (_position >= _text.Length || _text[_position] != expected) return false;
            _position++;
            return true;
        }

        private static bool IsDigit(char value)
        {
            return value is >= '0' and <= '9';
        }

        private bool Fail(out JsonValue value, out CatalogDiagnostic? diagnostic, string message)
        {
            value = null!;
            diagnostic = Error(message);
            return false;
        }

        private CatalogDiagnostic Error(string message)
        {
            return CreateDiagnostic(new TextSpan(_position, 0), message);
        }

        private LinePosition GetLinePosition(int position)
        {
            var line = 0;
            var character = 0;
            for (var i = 0; i < position; i++)
                if (_text[i] == '\n')
                {
                    line++;
                    character = 0;
                }
                else if (_text[i] != '\r')
                {
                    character++;
                }

            return new LinePosition(line, character);
        }
    }
}
