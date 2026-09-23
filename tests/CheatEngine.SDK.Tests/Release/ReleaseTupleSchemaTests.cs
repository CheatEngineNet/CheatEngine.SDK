using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>eng/release/release-tuple.v0.schema.json</c> and <see cref="ReleaseTupleValidator" /> describe the same document:
///     every required list, enum and const of the schema equals the validator's constant, in both directions, and every
///     object of the schema rejects unknown properties. No trait: both CI legs run it.
/// </summary>
public sealed class ReleaseTupleSchemaTests
{
	private const string SchemaPath = "eng/release/release-tuple.v0.schema.json";

	[Fact]
	public void Schema_required_and_enum_lists_equal_the_validator_constants()
	{
		using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.PathOf(SchemaPath)));
		JsonElement root = schema.RootElement;
		Dictionary<string, string[]> required = new(StringComparer.Ordinal);
		Dictionary<string, string[]> enums = new(StringComparer.Ordinal);
		Dictionary<string, string> consts = new(StringComparer.Ordinal);
		Collect(root, root, "", required, enums, consts);

		Assert.Equal(Sorted(ReleaseTupleValidator.RequiredProperties.Keys), Sorted(required.Keys));
		foreach ((string path, string[] expected) in ReleaseTupleValidator.RequiredProperties)
		{
			Assert.True(expected.SequenceEqual(required[path]),
				$"required of '{path}': schema [{string.Join(", ", required[path])}], validator [{string.Join(", ", expected)}].");
		}

		Assert.Equal(Sorted(ReleaseTupleValidator.EnumValues.Keys), Sorted(enums.Keys));
		foreach ((string path, string[] expected) in ReleaseTupleValidator.EnumValues)
		{
			Assert.Equal(expected, enums[path]);
		}

		Assert.Equal(Sorted(ReleaseTupleValidator.ConstValues.Keys), Sorted(consts.Keys));
		foreach ((string path, string expected) in ReleaseTupleValidator.ConstValues)
		{
			Assert.Equal(expected, consts[path]);
		}
	}

	[Fact]
	public void Every_object_in_the_schema_rejects_additional_properties()
	{
		using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.PathOf(SchemaPath)));
		List<string> offenders = [];
		int objects = 0;
		Walk(schema.RootElement, "#", node =>
		{
			if (node.Value.TryGetProperty("type", out JsonElement type)
				&& type.ValueKind == JsonValueKind.String
				&& string.Equals(type.GetString(), "object", StringComparison.Ordinal))
			{
				objects++;
				if (!node.Value.TryGetProperty("additionalProperties", out JsonElement additional)
					|| additional.ValueKind != JsonValueKind.False)
				{
					offenders.Add(node.Pointer);
				}
			}
		});

		Assert.Equal(RequiredObjectCount(), objects);
		Assert.True(offenders.Count == 0, $"Objects accepting unknown properties: {string.Join(", ", offenders)}");
	}

	[Fact]
	public void Schema_id_names_its_path_in_this_repository()
	{
		using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.PathOf(SchemaPath)));

		Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
		Assert.Equal($"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/{SchemaPath}",
			schema.RootElement.GetProperty("$id").GetString());
	}

	/// <summary>
	///     Walks the main property tree (not <c>allOf</c> conditions, which only narrow values) and records, per validator
	///     path, the <c>required</c> list of each object and the <c>enum</c>/<c>const</c> of each value.
	/// </summary>
	private static void Collect(JsonElement root, JsonElement node, string path, Dictionary<string, string[]> required,
		Dictionary<string, string[]> enums, Dictionary<string, string> consts)
	{
		node = Resolve(root, node);
		if (node.TryGetProperty("required", out JsonElement requiredList))
		{
			required[path] = [.. requiredList.EnumerateArray().Select(static e => e.GetString()!)];
		}

		if (node.TryGetProperty("enum", out JsonElement enumList))
		{
			enums[path] = [.. enumList.EnumerateArray().Select(static e => e.GetString()!)];
		}

		if (node.TryGetProperty("const", out JsonElement constant) && constant.ValueKind == JsonValueKind.String)
		{
			consts[path] = constant.GetString()!;
		}

		if (node.TryGetProperty("properties", out JsonElement properties))
		{
			foreach (JsonProperty property in properties.EnumerateObject())
			{
				Collect(root, property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", required, enums,
					consts);
			}
		}

		if (node.TryGetProperty("items", out JsonElement items))
		{
			Collect(root, items, path + "[]", required, enums, consts);
		}
	}

	/// <summary>Follows <c>$ref</c> and picks the non-null branch of an <c>anyOf</c> (the schema's nullable form).</summary>
	private static JsonElement Resolve(JsonElement root, JsonElement node)
	{
		while (true)
		{
			if (node.TryGetProperty("$ref", out JsonElement reference))
			{
				string pointer = reference.GetString()!;
				Assert.StartsWith("#/$defs/", pointer, StringComparison.Ordinal);
				node = root.GetProperty("$defs").GetProperty(pointer["#/$defs/".Length..]);
				continue;
			}

			if (node.TryGetProperty("anyOf", out JsonElement anyOf))
			{
				JsonElement[] branches = [.. anyOf.EnumerateArray().Where(static b => !IsNullType(b))];
				Assert.Single(branches);
				node = branches[0];
				continue;
			}

			return node;
		}
	}

	private static bool IsNullType(JsonElement schema)
	{
		return schema.TryGetProperty("type", out JsonElement type)
			   && type.ValueKind == JsonValueKind.String
			   && string.Equals(type.GetString(), "null", StringComparison.Ordinal);
	}

	private static void Walk(JsonElement node, string pointer, Action<(string Pointer, JsonElement Value)> visit)
	{
		if (node.ValueKind == JsonValueKind.Object)
		{
			visit((pointer, node));
			foreach (JsonProperty property in node.EnumerateObject())
			{
				Walk(property.Value, $"{pointer}/{property.Name}", visit);
			}
		}
		else if (node.ValueKind == JsonValueKind.Array)
		{
			int index = 0;
			foreach (JsonElement item in node.EnumerateArray())
			{
				Walk(item, $"{pointer}/{index++}", visit);
			}
		}
	}

	/// <summary>Every object the validator knows is one schema object; <c>pullRequest</c> and the item types sit in <c>$defs</c>.</summary>
	private static int RequiredObjectCount()
	{
		return ReleaseTupleValidator.RequiredProperties.Count;
	}

	private static List<string> Sorted(IEnumerable<string> values)
	{
		List<string> sorted = [.. values];
		sorted.Sort(StringComparer.Ordinal);
		return sorted;
	}
}
