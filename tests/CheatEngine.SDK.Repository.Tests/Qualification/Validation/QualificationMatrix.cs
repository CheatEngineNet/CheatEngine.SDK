using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>A typed read of a schema-valid <c>matrix.json</c>, used by the semantic rules and the Markdown renderer.</summary>
internal sealed class QualificationMatrix
{
	private QualificationMatrix(string repository, IReadOnlyList<string> profiles, IReadOnlyList<Row> rows)
	{
		Repository = repository;
		Profiles = profiles;
		Rows = rows;
	}

	internal string Repository
	{
		get;
	}

	internal IReadOnlyList<string> Profiles
	{
		get;
	}

	internal IReadOnlyList<Row> Rows
	{
		get;
	}

	/// <summary>Reads a matrix that already passed schema validation.</summary>
	internal static QualificationMatrix Read(JsonElement matrix)
	{
		List<string> profiles = [];
		foreach (JsonElement profile in matrix.GetProperty("profiles").EnumerateArray())
		{
			profiles.Add(profile.GetString()!);
		}

		List<Row> rows = [];
		foreach (JsonElement row in matrix.GetProperty("rows").EnumerateArray())
		{
			rows.Add(ReadRow(row));
		}

		return new QualificationMatrix(matrix.GetProperty("repository").GetString()!, profiles, rows);
	}

	internal Row? Find(string id)
	{
		foreach (Row row in Rows)
		{
			if (string.Equals(row.Id, id, StringComparison.Ordinal))
			{
				return row;
			}
		}

		return null;
	}

	/// <summary>Every cell with its row, in row order then level order.</summary>
	internal IEnumerable<(Row Row, Cell Cell)> Cells()
	{
		foreach (Row row in Rows)
		{
			foreach (string level in QualificationContract.Levels)
			{
				if (row.Levels.TryGetValue(level, out Cell? cell))
				{
					yield return (row, cell);
				}
			}
		}
	}

	private static Row ReadRow(JsonElement row)
	{
		JsonElement scenario = row.GetProperty("scenario");
		JsonElement audit = row.GetProperty("audit");
		Dictionary<string, Cell> levels = new(StringComparer.Ordinal);
		foreach (JsonProperty level in row.GetProperty("levels").EnumerateObject())
		{
			levels.Add(level.Name, ReadCell(level.Name, level.Value));
		}

		return new Row(
			row.GetProperty("id").GetString()!,
			row.GetProperty("parent").ValueKind == JsonValueKind.Null ? null : row.GetProperty("parent").GetString(),
			Strings(row, "subRows"),
			row.GetProperty("title").GetString()!,
			row.GetProperty("titleFr").GetString()!,
			row.GetProperty("owner").GetString()!,
			Strings(row, "requiredLevels"),
			row.GetProperty("blocks").GetString()!,
			audit.GetProperty("ref").GetString()!,
			Strings(audit, "findings"),
			Strings(scenario, "preconditions"),
			scenario.GetProperty("operation").GetString()!,
			scenario.GetProperty("expected").GetString()!,
			scenario.GetProperty("expectedCategory").GetString()!,
			levels);
	}

	private static Cell ReadCell(string level, JsonElement cell)
	{
		List<Evidence> evidence = [];
		if (cell.TryGetProperty("evidence", out JsonElement items))
		{
			foreach (JsonElement item in items.EnumerateArray())
			{
				evidence.Add(new Evidence(
					item.GetProperty("kind").GetString()!,
					OptionalString(item, "project"),
					OptionalString(item, "file"),
					OptionalString(item, "test"),
					OptionalString(item, "trait"),
					OptionalString(item, "receiptId"),
					OptionalString(item, "path"),
					OptionalString(item, "sha256"),
					OptionalString(item, "url")));
			}
		}

		return new Cell(
			level,
			cell.GetProperty("status").GetString()!,
			OptionalString(cell, "passKind"),
			cell.GetProperty("evidenceKind").GetString()!,
			OptionalString(cell, "profileId"),
			evidence,
			OptionalString(cell, "justification"),
			OptionalString(cell, "expected"),
			OptionalString(cell, "treeHash"),
			OptionalString(cell, "nupkgSha256"),
			OptionalString(cell, "transferJustification"),
			OptionalString(cell, "date"));
	}

	private static List<string> Strings(JsonElement element, string name)
	{
		List<string> values = [];
		if (element.TryGetProperty(name, out JsonElement array))
		{
			foreach (JsonElement value in array.EnumerateArray())
			{
				values.Add(value.GetString()!);
			}
		}

		return values;
	}

	private static string? OptionalString(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	internal sealed record Row(
		string Id,
		string? Parent,
		IReadOnlyList<string> SubRows,
		string Title,
		string TitleFr,
		string Owner,
		IReadOnlyList<string> RequiredLevels,
		string Blocks,
		string AuditRef,
		IReadOnlyList<string> Findings,
		IReadOnlyList<string> Preconditions,
		string Operation,
		string Expected,
		string ExpectedCategory,
		IReadOnlyDictionary<string, Cell> Levels);

	internal sealed record Cell(
		string Level,
		string Status,
		string? PassKind,
		string EvidenceKind,
		string? ProfileId,
		IReadOnlyList<Evidence> Evidence,
		string? Justification,
		string? Expected,
		string? TreeHash,
		string? NupkgSha256,
		string? TransferJustification,
		string? Date)
	{
		internal bool IsHostLevel => Level is "C3" or "C4";

		internal bool IsExecuted => Status is "Passed" or "Failed";
	}

	internal sealed record Evidence(
		string Kind,
		string? Project,
		string? File,
		string? Test,
		string? Trait,
		string? ReceiptId,
		string? Path,
		string? Sha256,
		string? Url);
}
