using System.Text.Json;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.Qualification;

/// <summary>
///     The four v0 schemas under <c>docs/qualification/schemas</c> are draft 2020-12, closed, identified by their
///     repository URL, use only the keywords the C# validator implements, and list exactly the validator's vocabulary.
/// </summary>
public sealed class QualificationSchemaTests
{
	private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

	public static TheoryData<string> SchemaFiles()
	{
		return [.. QualificationContract.SchemaFiles];
	}

	[Fact]
	public void Every_schema_is_draft_2020_12_with_a_repository_id_and_closed_objects()
	{
		List<string> problems = [];
		foreach (string file in QualificationContract.SchemaFiles)
		{
			JsonSchemaSubset schema = QualificationDocuments.Schema(file);
			JsonElement root = schema.Root;
			if (!string.Equals(root.GetProperty("$schema").GetString(), Draft202012, StringComparison.Ordinal))
			{
				problems.Add($"{file}: $schema must be {Draft202012}.");
			}

			if (!string.Equals(root.GetProperty("$id").GetString(), QualificationContract.SchemaIdPrefix + file,
					StringComparison.Ordinal))
			{
				problems.Add($"{file}: $id must be {QualificationContract.SchemaIdPrefix + file}.");
			}

			string description = root.GetProperty("description").GetString() ?? string.Empty;
			if (!description.Contains("CRLF is normalized to LF", StringComparison.Ordinal))
			{
				problems.Add($"{file}: the description must state the LF-normalized hash rule (A-SQUAL-4).");
			}

			foreach ((string pointer, JsonElement node) in schema.EnumerateSchemaObjects())
			{
				bool isObjectType = node.TryGetProperty("type", out JsonElement type) &&
									type.GetRawText().Contains("\"object\"", StringComparison.Ordinal);
				if (isObjectType && (!node.TryGetProperty("additionalProperties", out JsonElement additional) ||
									 additional.ValueKind != JsonValueKind.False))
				{
					problems.Add($"{file}{pointer}: an object schema must set \"additionalProperties\": false.");
				}
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Schemas_use_only_the_keywords_the_validator_implements()
	{
		List<string> problems = [];
		foreach (string file in QualificationContract.SchemaFiles)
		{
			foreach ((string pointer, JsonElement node) in QualificationDocuments.Schema(file).EnumerateSchemaObjects())
			{
				foreach (JsonProperty keyword in node.EnumerateObject())
				{
					if (!JsonSchemaSubset.SupportedKeywords.Contains(keyword.Name))
					{
						problems.Add($"{file}{pointer}: '{keyword.Name}' is not implemented by JsonSchemaSubset.");
					}
				}
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Theory]
	[MemberData(nameof(SchemaFiles))]
	public void Schema_required_and_enum_lists_equal_the_validator_constants(string file)
	{
		List<string> problems = [];
		HashSet<string> found = new(StringComparer.Ordinal);
		foreach ((string pointer, JsonElement node) in QualificationDocuments.Schema(file).EnumerateSchemaObjects())
		{
			// Lists inside an if condition select a branch; they are not requirements of the document.
			if (pointer.Contains("/if", StringComparison.Ordinal))
			{
				continue;
			}

			foreach (string keyword in (string[]) ["required", "enum"])
			{
				if (!node.TryGetProperty(keyword, out JsonElement list))
				{
					continue;
				}

				string key = QualificationContract.Key(file, pointer.Length == 0 ? "/" : pointer, keyword);
				found.Add(key);
				string[] actual = [.. list.EnumerateArray().Select(static value => value.GetString() ?? string.Empty)];
				if (!QualificationContract.SchemaLists.TryGetValue(key, out string[]? expected))
				{
					problems.Add($"{key}: [{string.Join(", ", actual)}] has no validator constant.");
				}
				else if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
				{
					problems.Add($"{key}: schema [{string.Join(", ", actual)}] differs from the validator [{string.Join(", ", expected)}].");
				}
			}
		}

		foreach (string key in QualificationContract.SchemaLists.Keys)
		{
			if (key.StartsWith(file + "|", StringComparison.Ordinal) && !found.Contains(key))
			{
				problems.Add($"{key}: the validator constant names a list the schema no longer has.");
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Each_schema_pins_its_document_kind_identifier()
	{
		Assert.Equal(QualificationContract.SupportProfileSchema, SchemaConst(QualificationContract.SupportProfileSchemaFile));
		Assert.Equal(QualificationContract.MatrixSchema, SchemaConst(QualificationContract.MatrixSchemaFile));
		Assert.Equal(QualificationContract.ReceiptSchema, SchemaConst(QualificationContract.ReceiptSchemaFile));
		Assert.Equal(QualificationContract.EventsSchema, SchemaConst(QualificationContract.EventsSchemaFile));
	}

	[Fact]
	public void The_validator_reports_unknown_properties_missing_fields_wrong_types_and_failed_conditions()
	{
		JsonSchemaSubset schema = JsonSchemaSubset.Parse(
			"""
			{
			  "type": "object",
			  "additionalProperties": false,
			  "required": ["status", "count"],
			  "properties": {
			    "status": { "enum": ["Passed", "NotExecuted"] },
			    "count": { "type": "integer", "minimum": 1 },
			    "hash": { "anyOf": [ { "type": "string", "pattern": "^[0-9a-f]{4}$" }, { "type": "null" } ] },
			    "evidence": { "type": "array", "minItems": 1, "items": { "type": "string" } }
			  },
			  "if": { "required": ["status"], "properties": { "status": { "const": "Passed" } } },
			  "then": { "required": ["evidence"] },
			  "else": { "properties": { "evidence": false } }
			}
			""", "inline");

		Assert.Empty(schema.Validate(Parse("""{ "status": "Passed", "count": 2, "hash": "00ff", "evidence": ["x"] }""")));
		Assert.Empty(schema.Validate(Parse("""{ "status": "NotExecuted", "count": 1, "hash": null }""")));
		Assert.Contains(schema.Validate(Parse("""{ "status": "Passed", "count": 1, "extra": 1, "evidence": ["x"] }""")),
			static error => error.Contains("unexpected property 'extra'", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "Passed", "evidence": ["x"] }""")),
			static error => error.Contains("required property 'count'", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "Passed", "count": 1.5, "evidence": ["x"] }""")),
			static error => error.Contains("must be of type", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "Passed", "count": 0, "evidence": ["x"] }""")),
			static error => error.Contains("at least 1", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "Passed", "count": 1 }""")),
			static error => error.Contains("required property 'evidence'", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "NotExecuted", "count": 1, "evidence": ["x"] }""")),
			static error => error.Contains("not allowed", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "Failed", "count": 1 }""")),
			static error => error.Contains("must be one of", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Parse("""{ "status": "NotExecuted", "count": 1, "hash": "00FF" }""")),
			static error => error.Contains("anyOf", StringComparison.Ordinal));
	}

	private static string? SchemaConst(string file)
	{
		return QualificationDocuments.Schema(file).Root.GetProperty("properties").GetProperty("schema")
			.GetProperty("const").GetString();
	}

	private static JsonElement Parse(string json)
	{
		return QualificationDocuments.ParseJson(json);
	}
}
