using System.Text;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     Renders the Markdown blocks generated from <c>matrix.json</c>: the matrix summary of <c>docs/qualification/README.md</c>
///     and the "Not executed" list of <c>docs/qualification/support-profile.md</c>. The tests compare the committed blocks
///     with these renderings and print the expected block on failure, so an author pastes it instead of editing by hand.
/// </summary>
internal static class QualificationMarkdown
{
	internal const string MatrixSummaryMarker = "matrix-summary";
	internal const string NotExecutedMarker = "not-executed";

	/// <summary>One line per row: owner, required levels and the status of every level that has a cell.</summary>
	internal static string MatrixSummary(QualificationMatrix matrix)
	{
		StringBuilder builder = new();
		builder.Append("| Row | Scenario | Owner | Required | C0 | C1 | C2 | C3 | C4 |\n");
		builder.Append("|-----|----------|-------|----------|----|----|----|----|----|\n");
		foreach (QualificationMatrix.Row row in matrix.Rows)
		{
			builder.Append("| ").Append(row.Id)
				.Append(" | ").Append(row.Title)
				.Append(" | ").Append(row.Owner)
				.Append(" | ").Append(string.Join(", ", row.RequiredLevels));
			foreach (string level in QualificationContract.Levels)
			{
				builder.Append(" | ").Append(row.Levels.TryGetValue(level, out QualificationMatrix.Cell? cell)
					? Describe(cell)
					: "–");
			}

			builder.Append(" |\n");
		}

		return builder.ToString();
	}

	/// <summary>Every row with at least one NotExecuted level, with those levels.</summary>
	internal static string NotExecuted(QualificationMatrix matrix)
	{
		StringBuilder builder = new();
		builder.Append("| Row | Scenario | Owner | Not executed at |\n");
		builder.Append("|-----|----------|-------|-----------------|\n");
		foreach (QualificationMatrix.Row row in matrix.Rows)
		{
			List<string> levels = [];
			foreach (string level in QualificationContract.Levels)
			{
				if (row.Levels.TryGetValue(level, out QualificationMatrix.Cell? cell) &&
					string.Equals(cell.Status, "NotExecuted", StringComparison.Ordinal))
				{
					levels.Add(level);
				}
			}

			if (levels.Count == 0)
			{
				continue;
			}

			builder.Append("| ").Append(row.Id)
				.Append(" | ").Append(row.Title)
				.Append(" | ").Append(row.Owner)
				.Append(" | ").Append(string.Join(", ", levels))
				.Append(" |\n");
		}

		return builder.ToString();
	}

	/// <summary>
	///     The text between <c>&lt;!-- BEGIN GENERATED: marker --&gt;</c> and <c>&lt;!-- END GENERATED: marker --&gt;</c>, LF
	///     newlines, without the marker lines; <see langword="null" /> when a marker is missing.
	/// </summary>
	internal static string? GeneratedBlock(string markdown, string marker)
	{
		string begin = "<!-- BEGIN GENERATED: " + marker + " -->\n";
		string end = "<!-- END GENERATED: " + marker + " -->";
		int start = markdown.IndexOf(begin, StringComparison.Ordinal);
		int stop = markdown.IndexOf(end, StringComparison.Ordinal);
		if (start < 0 || stop < start)
		{
			return null;
		}

		return markdown[(start + begin.Length)..stop];
	}

	private static string Describe(QualificationMatrix.Cell cell)
	{
		return cell.Status switch
		{
			"Passed" when string.Equals(cell.PassKind, "RefusalVerified", StringComparison.Ordinal) =>
				"Passed (refusal verified)",
			"Passed" => "Passed",
			"Failed" => "Failed",
			"NotApplicable" => "Not applicable",
			_ => "Not executed"
		};
	}
}
