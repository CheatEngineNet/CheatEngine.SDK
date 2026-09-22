using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     A validator for the subset of JSON Schema draft 2020-12 the qualification schemas use. It deliberately implements
///     only <see cref="SupportedKeywords" />: <c>QualificationSchemaTests</c> fails when a schema uses anything else, so a
///     schema can never rely on a keyword that this validator would silently ignore.
/// </summary>
/// <remarks>
///     Semantics follow https://json-schema.org/draft/2020-12/json-schema-validation: <c>pattern</c> is an ECMA-262
///     regular expression (evaluated with <see cref="RegexOptions.ECMAScript" />, unanchored), <c>properties</c>,
///     <c>required</c> and <c>additionalProperties</c> apply to objects only, <c>items</c> and <c>minItems</c> to arrays only,
///     <c>minimum</c> to numbers only, and <c>$ref</c> resolves local <c>#/$defs/…</c> pointers only.
/// </remarks>
internal sealed class JsonSchemaSubset
{
	/// <summary>Keywords with validation semantics implemented below, plus pure annotations.</summary>
	internal static readonly IReadOnlySet<string> SupportedKeywords = new HashSet<string>(StringComparer.Ordinal)
	{
		"$schema", "$id", "$comment", "$defs", "$ref", "title", "description",
		"type", "const", "enum", "pattern", "required", "properties", "additionalProperties", "items", "minItems",
		"minimum", "allOf", "anyOf", "if", "then", "else"
	};

	private const string DefinitionsPrefix = "#/$defs/";
	private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

	private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

	private JsonSchemaSubset(JsonElement root, string name)
	{
		Root = root;
		Name = name;
	}

	/// <summary>The root schema object (a standalone clone, independent of any document).</summary>
	internal JsonElement Root
	{
		get;
	}

	/// <summary>The schema file name, for messages.</summary>
	internal string Name
	{
		get;
	}

	/// <summary>Loads a schema from its UTF-8 text.</summary>
	internal static JsonSchemaSubset Parse(string json, string name)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return new JsonSchemaSubset(document.RootElement.Clone(), name);
	}

	/// <summary>Validates <paramref name="instance" />; returns one message per violation, empty when valid.</summary>
	internal IReadOnlyList<string> Validate(JsonElement instance)
	{
		List<string> errors = [];
		Validate(Root, instance, string.Empty, errors);
		return errors;
	}

	/// <summary>Returns whether <paramref name="instance" /> is valid.</summary>
	internal bool IsValid(JsonElement instance)
	{
		return Validate(instance).Count == 0;
	}

	/// <summary>Enumerates every schema object of the document with its JSON pointer, depth first.</summary>
	internal IEnumerable<(string Pointer, JsonElement Schema)> EnumerateSchemaObjects()
	{
		return EnumerateSchemaObjects(Root, string.Empty);
	}

	private static IEnumerable<(string Pointer, JsonElement Schema)> EnumerateSchemaObjects(JsonElement schema,
		string pointer)
	{
		if (schema.ValueKind != JsonValueKind.Object)
		{
			yield break;
		}

		yield return (pointer, schema);
		foreach (JsonProperty keyword in schema.EnumerateObject())
		{
			string childPointer = pointer + "/" + keyword.Name;
			switch (keyword.Name)
			{
				case "properties":
				case "$defs":
					foreach (JsonProperty entry in keyword.Value.EnumerateObject())
					{
						foreach ((string Pointer, JsonElement Schema) nested in EnumerateSchemaObjects(entry.Value,
									 childPointer + "/" + entry.Name))
						{
							yield return nested;
						}
					}

					break;
				case "allOf":
				case "anyOf":
					int index = 0;
					foreach (JsonElement branch in keyword.Value.EnumerateArray())
					{
						foreach ((string Pointer, JsonElement Schema) nested in EnumerateSchemaObjects(branch,
									 childPointer + "/" + index.ToString(CultureInfo.InvariantCulture)))
						{
							yield return nested;
						}

						index++;
					}

					break;
				case "items":
				case "additionalProperties":
				case "if":
				case "then":
				case "else":
					foreach ((string Pointer, JsonElement Schema) nested in EnumerateSchemaObjects(keyword.Value,
								 childPointer))
					{
						yield return nested;
					}

					break;
			}
		}
	}

	private void Validate(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		switch (schema.ValueKind)
		{
			case JsonValueKind.True:
				return;
			case JsonValueKind.False:
				errors.Add(At(pointer, "is not allowed here"));
				return;
			case JsonValueKind.Object:
				break;
			default:
				throw new InvalidOperationException($"{Name}: a schema must be an object or a boolean at '{pointer}'.");
		}

		ValidateReference(schema, instance, pointer, errors);
		ValidateTypeConstAndEnum(schema, instance, pointer, errors);
		ValidateString(schema, instance, pointer, errors);
		ValidateNumber(schema, instance, pointer, errors);
		ValidateObject(schema, instance, pointer, errors);
		ValidateArray(schema, instance, pointer, errors);
		ValidateCombinators(schema, instance, pointer, errors);
	}

	private void ValidateReference(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		if (schema.TryGetProperty("$ref", out JsonElement reference))
		{
			Validate(Resolve(reference.GetString() ?? string.Empty), instance, pointer, errors);
		}
	}

	private static void ValidateTypeConstAndEnum(JsonElement schema, JsonElement instance, string pointer,
		List<string> errors)
	{
		if (schema.TryGetProperty("type", out JsonElement type) && !MatchesType(type, instance))
		{
			errors.Add(At(pointer, $"must be of type {type.GetRawText()}, found {instance.ValueKind}"));
		}

		if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(constant, instance))
		{
			errors.Add(At(pointer, $"must equal {constant.GetRawText()}, found {Describe(instance)}"));
		}

		if (schema.TryGetProperty("enum", out JsonElement values))
		{
			foreach (JsonElement value in values.EnumerateArray())
			{
				if (JsonElement.DeepEquals(value, instance))
				{
					return;
				}
			}

			errors.Add(At(pointer, $"must be one of {values.GetRawText()}, found {Describe(instance)}"));
		}
	}

	private void ValidateString(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		if (instance.ValueKind != JsonValueKind.String || !schema.TryGetProperty("pattern", out JsonElement pattern))
		{
			return;
		}

		string expression = pattern.GetString() ?? string.Empty;
		if (!_patterns.TryGetValue(expression, out Regex? regex))
		{
			regex = new Regex(expression, RegexOptions.ECMAScript, PatternTimeout);
			_patterns.Add(expression, regex);
		}

		if (!regex.IsMatch(instance.GetString() ?? string.Empty))
		{
			errors.Add(At(pointer, $"does not match the pattern {expression}: {Describe(instance)}"));
		}
	}

	private static void ValidateNumber(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		if (instance.ValueKind == JsonValueKind.Number && schema.TryGetProperty("minimum", out JsonElement minimum) &&
			instance.GetDecimal() < minimum.GetDecimal())
		{
			errors.Add(At(pointer, $"must be at least {minimum.GetRawText()}, found {instance.GetRawText()}"));
		}
	}

	private void ValidateObject(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		if (instance.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		if (schema.TryGetProperty("required", out JsonElement required))
		{
			foreach (JsonElement name in required.EnumerateArray())
			{
				if (!instance.TryGetProperty(name.GetString() ?? string.Empty, out _))
				{
					errors.Add(At(pointer, $"misses the required property '{name.GetString()}'"));
				}
			}
		}

		bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties);
		bool hasAdditional = schema.TryGetProperty("additionalProperties", out JsonElement additional);
		foreach (JsonProperty property in instance.EnumerateObject())
		{
			string childPointer = pointer + "/" + EscapePointer(property.Name);
			if (hasProperties && properties.TryGetProperty(property.Name, out JsonElement propertySchema))
			{
				Validate(propertySchema, property.Value, childPointer, errors);
			}
			else if (hasAdditional)
			{
				if (additional.ValueKind == JsonValueKind.False)
				{
					errors.Add(At(pointer, $"has the unexpected property '{property.Name}'"));
				}
				else
				{
					Validate(additional, property.Value, childPointer, errors);
				}
			}
		}
	}

	private void ValidateArray(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		if (instance.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		if (schema.TryGetProperty("minItems", out JsonElement minItems) &&
			instance.GetArrayLength() < minItems.GetInt32())
		{
			errors.Add(At(pointer, $"must have at least {minItems.GetInt32()} item(s), found {instance.GetArrayLength()}"));
		}

		if (schema.TryGetProperty("items", out JsonElement items))
		{
			int index = 0;
			foreach (JsonElement item in instance.EnumerateArray())
			{
				Validate(items, item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture), errors);
				index++;
			}
		}
	}

	private void ValidateCombinators(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
	{
		if (schema.TryGetProperty("allOf", out JsonElement allOf))
		{
			foreach (JsonElement branch in allOf.EnumerateArray())
			{
				Validate(branch, instance, pointer, errors);
			}
		}

		if (schema.TryGetProperty("anyOf", out JsonElement anyOf))
		{
			List<string> firstBranchErrors = [];
			bool matched = false;
			foreach (JsonElement branch in anyOf.EnumerateArray())
			{
				List<string> branchErrors = [];
				Validate(branch, instance, pointer, branchErrors);
				if (branchErrors.Count == 0)
				{
					matched = true;
					break;
				}

				if (firstBranchErrors.Count == 0)
				{
					firstBranchErrors = branchErrors;
				}
			}

			if (!matched)
			{
				errors.Add(At(pointer, "matches no anyOf branch; first branch: " + string.Join("; ", firstBranchErrors)));
			}
		}

		if (schema.TryGetProperty("if", out JsonElement condition))
		{
			List<string> conditionErrors = [];
			Validate(condition, instance, pointer, conditionErrors);
			string branchName = conditionErrors.Count == 0 ? "then" : "else";
			if (schema.TryGetProperty(branchName, out JsonElement branch))
			{
				Validate(branch, instance, pointer, errors);
			}
		}
	}

	private JsonElement Resolve(string reference)
	{
		if (!reference.StartsWith(DefinitionsPrefix, StringComparison.Ordinal) ||
			!Root.TryGetProperty("$defs", out JsonElement definitions) ||
			!definitions.TryGetProperty(reference[DefinitionsPrefix.Length..], out JsonElement target))
		{
			throw new InvalidOperationException(
				$"{Name}: only local '{DefinitionsPrefix}name' references are supported; '{reference}' does not resolve.");
		}

		return target;
	}

	private static bool MatchesType(JsonElement type, JsonElement instance)
	{
		if (type.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement candidate in type.EnumerateArray())
			{
				if (MatchesType(candidate.GetString() ?? string.Empty, instance))
				{
					return true;
				}
			}

			return false;
		}

		return MatchesType(type.GetString() ?? string.Empty, instance);
	}

	private static bool MatchesType(string type, JsonElement instance)
	{
		return type switch
		{
			"object" => instance.ValueKind == JsonValueKind.Object,
			"array" => instance.ValueKind == JsonValueKind.Array,
			"string" => instance.ValueKind == JsonValueKind.String,
			"boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
			"null" => instance.ValueKind == JsonValueKind.Null,
			"number" => instance.ValueKind == JsonValueKind.Number,
			"integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetDecimal(out decimal value) &&
						 decimal.Truncate(value) == value,
			_ => throw new InvalidOperationException($"Unknown JSON Schema type '{type}'.")
		};
	}

	private static string Describe(JsonElement instance)
	{
		string raw = instance.GetRawText();
		return raw.Length <= 80 ? raw : raw[..77] + "...";
	}

	private static string EscapePointer(string name)
	{
		return name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
	}

	private static string At(string pointer, string message)
	{
		return (pointer.Length == 0 ? "/" : pointer) + " " + message;
	}
}
