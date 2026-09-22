using System.Globalization;
using System.Text.Json;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     The semantic rules of the qualification matrix that a JSON schema cannot express: structure, profiles, evidence
///     kinds, sub-row aggregation, receipt resolution and freshness, and the link between Automated evidence and the
///     traited test methods. Every method returns one message per violation, so a test can assert an empty list and
///     print every offender at once.
/// </summary>
internal static class QualificationRules
{
	/// <summary>Row ids are unique and sorted, sub-rows and parents agree, and every required level has a cell.</summary>
	internal static IReadOnlyList<string> Structure(QualificationMatrix matrix)
	{
		List<string> errors = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		string? previous = null;
		foreach (QualificationMatrix.Row row in matrix.Rows)
		{
			if (!seen.Add(row.Id))
			{
				errors.Add($"{row.Id}: the id appears more than once.");
			}

			if (previous is not null && string.CompareOrdinal(previous, row.Id) >= 0)
			{
				errors.Add($"{row.Id}: rows must be sorted by id (ordinal); it follows {previous}.");
			}

			previous = row.Id;
			CheckParent(matrix, row, errors);
			CheckRequiredLevels(row, errors);
		}

		return errors;
	}

	/// <summary>
	///     Cells cite profiles of the support profile; a C3/C4 cell cites a qualifiable profile, and no executed cell
	///     cites the documentary public-source profile.
	/// </summary>
	internal static IReadOnlyList<string> Profiles(QualificationMatrix matrix, JsonElement supportProfile)
	{
		Dictionary<string, bool> qualifiable = SupportProfileRules.QualifiableByProfileId(supportProfile);
		List<string> errors = [];
		foreach (string profile in matrix.Profiles)
		{
			if (!qualifiable.ContainsKey(profile))
			{
				errors.Add($"matrix.profiles lists '{profile}', which the support profile does not define.");
			}
		}

		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in matrix.Cells())
		{
			if (cell.ProfileId is null)
			{
				continue;
			}

			string at = At(row, cell);
			if (!qualifiable.TryGetValue(cell.ProfileId, out bool isQualifiable) ||
				!Contains(matrix.Profiles, cell.ProfileId))
			{
				errors.Add($"{at}: cites the unknown profile '{cell.ProfileId}'.");
				continue;
			}

			if (cell.IsExecuted && !isQualifiable)
			{
				errors.Add($"{at}: a {cell.Status} cell cites the documentary profile '{cell.ProfileId}', which is never qualifiable.");
			}
			else if (cell.IsHostLevel && !isQualifiable)
			{
				errors.Add($"{at}: a host-level cell must name a qualifiable profile, not '{cell.ProfileId}'.");
			}
		}

		return errors;
	}

	/// <summary>
	///     NotExecuted is ToQualify; an executed C0-C2 cell is ObservedSource; an executed C3/C4 cell is ObservedHost; a
	///     NotApplicable cell is a ProposedDecision, or ObservedHost when a receipt records it.
	/// </summary>
	internal static IReadOnlyList<string> EvidenceKinds(QualificationMatrix matrix)
	{
		List<string> errors = [];
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in matrix.Cells())
		{
			string expected = cell.Status switch
			{
				"NotExecuted" => "ToQualify",
				"Passed" or "Failed" => cell.IsHostLevel ? "ObservedHost" : "ObservedSource",
				_ => cell.Evidence.Count == 0 ? "ProposedDecision" : "ObservedHost"
			};
			if (!string.Equals(cell.EvidenceKind, expected, StringComparison.Ordinal))
			{
				errors.Add($"{At(row, cell)}: a {cell.Status} cell has evidenceKind {cell.EvidenceKind}, expected {expected}.");
			}
		}

		return errors;
	}

	/// <summary>
	///     A parent row's cell equals the aggregate of its sub-rows' cells at that level: Failed if any is Failed,
	///     NotApplicable if all are, Passed if all are Passed or NotApplicable, otherwise NotExecuted.
	/// </summary>
	internal static IReadOnlyList<string> Aggregation(QualificationMatrix matrix)
	{
		List<string> errors = [];
		foreach (QualificationMatrix.Row parent in matrix.Rows)
		{
			if (parent.SubRows.Count == 0)
			{
				continue;
			}

			foreach ((string level, QualificationMatrix.Cell parentCell) in parent.Levels)
			{
				List<string> statuses = [];
				foreach (string subRowId in parent.SubRows)
				{
					if (matrix.Find(subRowId) is { } subRow &&
						subRow.Levels.TryGetValue(level, out QualificationMatrix.Cell? subCell))
					{
						statuses.Add(subCell.Status);
					}
				}

				if (statuses.Count == 0)
				{
					continue;
				}

				string aggregate = Aggregate(statuses);
				if (!string.Equals(parentCell.Status, aggregate, StringComparison.Ordinal))
				{
					errors.Add($"{parent.Id} {level}: is {parentCell.Status}, but its sub-rows ({string.Join(", ", statuses)}) aggregate to {aggregate}.");
				}
			}
		}

		return errors;
	}

	/// <summary>The contract's aggregation of sub-row statuses (shared-contracts section 2.2).</summary>
	internal static string Aggregate(IReadOnlyCollection<string> statuses)
	{
		bool allNotApplicable = true;
		bool allPassedOrNotApplicable = true;
		foreach (string status in statuses)
		{
			if (string.Equals(status, "Failed", StringComparison.Ordinal))
			{
				return "Failed";
			}

			allNotApplicable &= string.Equals(status, "NotApplicable", StringComparison.Ordinal);
			allPassedOrNotApplicable &= status is "Passed" or "NotApplicable";
		}

		if (allNotApplicable)
		{
			return "NotApplicable";
		}

		return allPassedOrNotApplicable ? "Passed" : "NotExecuted";
	}

	/// <summary>
	///     Every receipt a cell cites is committed at its path with the cited LF-normalized SHA-256, qualifies that row,
	///     level, status and profile, and was produced from the cell's tree and package unless the cell carries a
	///     transferJustification.
	/// </summary>
	internal static IReadOnlyList<string> Receipts(QualificationMatrix matrix, Func<string, JsonElement?> loadReceipt)
	{
		List<string> errors = [];
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in matrix.Cells())
		{
			foreach (QualificationMatrix.Evidence evidence in cell.Evidence)
			{
				if (string.Equals(evidence.Kind, "Receipt", StringComparison.Ordinal))
				{
					CheckReceiptEvidence(row, cell, evidence, loadReceipt, errors);
				}
			}
		}

		return errors;
	}

	/// <summary>
	///     Automated evidence names a *.Tests project of the solution, a file of that project, a method declared in it,
	///     and the trait that method carries for this row.
	/// </summary>
	internal static IReadOnlyList<string> AutomatedEvidence(QualificationMatrix matrix,
		IReadOnlySet<string> solutionProjects)
	{
		List<string> errors = [];
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in matrix.Cells())
		{
			foreach (QualificationMatrix.Evidence evidence in cell.Evidence)
			{
				if (string.Equals(evidence.Kind, "Automated", StringComparison.Ordinal))
				{
					CheckAutomatedEvidence(row, cell, evidence, solutionProjects, errors);
				}
			}
		}

		return errors;
	}

	/// <summary>Every method-level Qualification trait is cited by the matrix, and every citation names a row.</summary>
	internal static IReadOnlyList<string> TraitParity(QualificationMatrix matrix, TestSourceIndex index)
	{
		List<string> errors = [.. index.Violations];
		HashSet<string> cited = new(StringComparer.Ordinal);
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in matrix.Cells())
		{
			foreach (QualificationMatrix.Evidence evidence in cell.Evidence)
			{
				if (string.Equals(evidence.Kind, "Automated", StringComparison.Ordinal))
				{
					cited.Add(evidence.File + "|" + evidence.Test + "|" + evidence.Trait);
				}
			}
		}

		foreach (TestSourceIndex.TraitUse trait in index.Traits)
		{
			if (matrix.Find(trait.Value) is null)
			{
				errors.Add($"{trait.File}:{trait.Line}: {trait.Test} carries Qualification={trait.Value}, which is not a matrix row.");
			}
			else if (!cited.Contains(trait.File + "|" + trait.Test + "|Qualification=" + trait.Value))
			{
				errors.Add($"{trait.File}:{trait.Line}: {trait.Test} carries Qualification={trait.Value}, but no matrix cell of {trait.Value} cites it as Automated evidence.");
			}
		}

		return errors;
	}

	private static void CheckParent(QualificationMatrix matrix, QualificationMatrix.Row row, List<string> errors)
	{
		int dot = row.Id.IndexOf('.', StringComparison.Ordinal);
		if (row.Parent is null)
		{
			if (dot >= 0)
			{
				errors.Add($"{row.Id}: a sub-row must name its parent.");
			}
		}
		else if (dot < 0 || !string.Equals(row.Id[..dot], row.Parent, StringComparison.Ordinal))
		{
			errors.Add($"{row.Id}: parent '{row.Parent}' is not the id prefix.");
		}
		else if (matrix.Find(row.Parent) is not { } parent || !Contains(parent.SubRows, row.Id) || parent.Parent is not null)
		{
			errors.Add($"{row.Id}: parent '{row.Parent}' must be a top row that lists it in subRows.");
		}

		foreach (string subRowId in row.SubRows)
		{
			if (matrix.Find(subRowId) is not { } subRow || !string.Equals(subRow.Parent, row.Id, StringComparison.Ordinal))
			{
				errors.Add($"{row.Id}: subRows lists '{subRowId}', which is not a sub-row of it.");
			}
		}
	}

	private static void CheckRequiredLevels(QualificationMatrix.Row row, List<string> errors)
	{
		HashSet<string> required = new(StringComparer.Ordinal);
		foreach (string level in row.RequiredLevels)
		{
			if (!required.Add(level))
			{
				errors.Add($"{row.Id}: requiredLevels repeats {level}.");
			}

			if (!row.Levels.ContainsKey(level))
			{
				errors.Add($"{row.Id}: required level {level} has no cell.");
			}
		}
	}

	private static void CheckReceiptEvidence(QualificationMatrix.Row row, QualificationMatrix.Cell cell,
		QualificationMatrix.Evidence evidence, Func<string, JsonElement?> loadReceipt, List<string> errors)
	{
		string at = At(row, cell) + " receipt " + evidence.ReceiptId;
		string expectedPath = QualificationDocuments.ReceiptDirectory + "/" + row.Id + "/" + evidence.ReceiptId + ".json";
		if (!string.Equals(evidence.Path, expectedPath, StringComparison.Ordinal))
		{
			errors.Add($"{at}: path must be {expectedPath}.");
			return;
		}

		JsonElement? loaded = loadReceipt(expectedPath);
		if (loaded is not { } receipt)
		{
			errors.Add($"{at}: {expectedPath} is not committed.");
			return;
		}

		if (!string.Equals(QualificationDocuments.CommittedJsonSha256(expectedPath), evidence.Sha256,
				StringComparison.Ordinal))
		{
			errors.Add($"{at}: the cited sha256 is not the LF-normalized SHA-256 of {expectedPath}.");
		}

		ExpectEqual(errors, at, "receiptId", receipt.GetProperty("receiptId").GetString(), evidence.ReceiptId);
		ExpectEqual(errors, at, "qualificationId", receipt.GetProperty("qualificationId").GetString(), row.Id);
		ExpectEqual(errors, at, "level", receipt.GetProperty("level").GetString(), cell.Level);
		ExpectEqual(errors, at, "status", receipt.GetProperty("status").GetString(), cell.Status);
		ExpectEqual(errors, at, "profileId", receipt.GetProperty("profileId").GetString(), cell.ProfileId);
		CheckFreshness(cell, receipt, at, errors);
	}

	private static void CheckFreshness(QualificationMatrix.Cell cell, JsonElement receipt, string at, List<string> errors)
	{
		string? tree = receipt.GetProperty("repository").GetProperty("treeHash").GetString();
		string? package = receipt.GetProperty("package").GetProperty("nupkgSha256").GetString();
		bool sameTree = string.Equals(tree, cell.TreeHash, StringComparison.Ordinal);
		bool samePackage = string.Equals(package, cell.NupkgSha256, StringComparison.Ordinal);
		if (cell.IsExecuted && (!sameTree || !samePackage) && cell.TransferJustification is null)
		{
			errors.Add($"{at}: produced from tree {tree} and package {package}, not the cell's {cell.TreeHash} / {cell.NupkgSha256}; add a transferJustification or a fresh receipt.");
		}
	}

	private static void CheckAutomatedEvidence(QualificationMatrix.Row row, QualificationMatrix.Cell cell,
		QualificationMatrix.Evidence evidence, IReadOnlySet<string> solutionProjects, List<string> errors)
	{
		string at = At(row, cell) + " " + evidence.Test;
		string project = evidence.Project ?? string.Empty;
		if (!solutionProjects.Contains(project) || !project.EndsWith(".Tests.csproj", StringComparison.Ordinal))
		{
			errors.Add($"{at}: '{project}' is not a *.Tests module of CheatEngine.SDK.slnx, so CI does not run it.");
		}

		string projectDirectory = project[..(project.LastIndexOf('/') + 1)];
		string file = evidence.File ?? string.Empty;
		if (!file.StartsWith(projectDirectory, StringComparison.Ordinal) || !QualificationDocuments.Exists(file))
		{
			errors.Add($"{at}: '{file}' is not a file of {project}.");
			return;
		}

		string expectedTrait = "Qualification=" + row.Id;
		if (!string.Equals(evidence.Trait, expectedTrait, StringComparison.Ordinal))
		{
			errors.Add($"{at}: trait must be {expectedTrait}, found {evidence.Trait}.");
		}

		string[] parts = (evidence.Test ?? string.Empty).Split('.');
		IReadOnlyList<string>? traits = TestSourceIndex.TraitsOfMethod(file, parts[0], parts[^1]);
		if (traits is null)
		{
			errors.Add($"{at}: no method {parts[^1]} of {parts[0]} is declared in {file}.");
		}
		else if (!Contains(traits, row.Id))
		{
			errors.Add($"{at}: the method has no [Trait(\"Qualification\", \"{row.Id}\")] in its attribute block.");
		}
	}

	private static void ExpectEqual(List<string> errors, string at, string field, string? actual, string? expected)
	{
		if (!string.Equals(actual, expected, StringComparison.Ordinal))
		{
			errors.Add($"{at}: receipt {field} is '{actual}', expected '{expected}'.");
		}
	}

	private static bool Contains(IEnumerable<string> values, string value)
	{
		foreach (string candidate in values)
		{
			if (string.Equals(candidate, value, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static string At(QualificationMatrix.Row row, QualificationMatrix.Cell cell)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{row.Id} {cell.Level}");
	}
}
