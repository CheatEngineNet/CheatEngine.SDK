using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.Qualification;

/// <summary>
///     <c>docs/qualification/matrix.json</c>: Q01 to Q48 of the audit (analyses/20) with their required levels, one cell per
///     level, and evidence that is honest by construction. A C0-C2 cell passes only on traited tests of a CI module, a
///     C3/C4 cell only on committed receipts of the qualifiable profile, and a parent row equals its sub-rows' aggregate.
/// </summary>
public sealed class QualificationMatrixTests
{
	/// <summary>The audit's required levels (analyses/20, column "Niveau"): the oracle the matrix must reproduce.</summary>
	private static readonly Dictionary<string, string[]> AuditRequiredLevels = BuildOracle();

	private static readonly string[] SubRows =
	[
		"Q05.a", "Q08.a", "Q09.a", "Q09.b", "Q30.a", "Q30.b", "Q30.c", "Q30.d", "Q30.e", "Q31.a", "Q32.a", "Q32.b",
		"Q32.c", "Q32.d"
	];

	private static JsonElement MatrixJson => QualificationDocuments.LoadJson(QualificationDocuments.MatrixPath);

	private static QualificationMatrix Matrix => QualificationMatrix.Read(MatrixJson);

	private static JsonElement SupportProfile => QualificationDocuments.LoadJson(QualificationDocuments.SupportProfilePath);

	[Fact]
	public void Matrix_matches_its_v0_schema()
	{
		IReadOnlyList<string> errors =
			QualificationDocuments.Schema(QualificationContract.MatrixSchemaFile).Validate(MatrixJson);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.Equal(QualificationContract.Repository, Matrix.Repository);
		Assert.Equal(QualificationContract.AuditManifestSha256,
			MatrixJson.GetProperty("audit").GetProperty("manifestSha256").GetString());
		Assert.Equal([QualificationContract.DocumentaryProfileId, QualificationContract.QualifiableProfileId],
			Matrix.Profiles);
		IReadOnlyList<string> structure = QualificationRules.Structure(Matrix);
		Assert.True(structure.Count == 0, string.Join(Environment.NewLine, structure));
	}

	[Fact]
	public void Matrix_lists_Q01_to_Q48_exactly_once_with_the_audit_required_levels()
	{
		List<string> topRows = [];
		List<string> subRows = [];
		foreach (QualificationMatrix.Row row in Matrix.Rows)
		{
			(row.Parent is null ? topRows : subRows).Add(row.Id);
		}

		Assert.Equal([.. Enumerable.Range(1, 48).Select(static n => "Q" + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture))],
			topRows);
		Assert.Equal(SubRows, subRows, StringComparer.Ordinal);
		foreach (string id in topRows)
		{
			Assert.True(AuditRequiredLevels[id].SequenceEqual(Matrix.Find(id)!.RequiredLevels, StringComparer.Ordinal),
				$"{id}: requiredLevels [{string.Join(", ", Matrix.Find(id)!.RequiredLevels)}] differ from the audit [{string.Join(", ", AuditRequiredLevels[id])}].");
		}
	}

	[Fact]
	public void Every_required_level_has_a_cell()
	{
		List<string> missing = [];
		foreach (QualificationMatrix.Row row in Matrix.Rows)
		{
			foreach (string level in row.RequiredLevels)
			{
				if (!row.Levels.ContainsKey(level))
				{
					missing.Add(row.Id + " " + level);
				}
			}
		}

		Assert.True(missing.Count == 0, "Required levels without a cell: " + string.Join(", ", missing));
	}

	[Fact]
	public void Every_row_declares_a_scenario_with_an_expected_category()
	{
		foreach (QualificationMatrix.Row row in Matrix.Rows)
		{
			Assert.NotEmpty(row.Preconditions);
			Assert.False(string.IsNullOrWhiteSpace(row.Operation), row.Id + " has no operation.");
			Assert.False(string.IsNullOrWhiteSpace(row.Expected), row.Id + " has no expected result.");
			Assert.Contains(row.ExpectedCategory, QualificationContract.ExpectedCategories, StringComparer.Ordinal);
			Assert.False(string.IsNullOrWhiteSpace(row.TitleFr), row.Id + " has no audit wording.");
		}

		Assert.Equal("Effect", Matrix.Find("Q05.a")!.ExpectedCategory);
		Assert.Equal("Refused", Matrix.Find("Q08.a")!.ExpectedCategory);
		Assert.Equal("Refused", Matrix.Find("Q30.c")!.ExpectedCategory);
		Assert.Equal("Refused", Matrix.Find("Q30.d")!.ExpectedCategory);
	}

	[Fact]
	public void Not_applicable_cells_carry_a_justification()
	{
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in Matrix.Cells())
		{
			if (string.Equals(cell.Status, "NotApplicable", StringComparison.Ordinal))
			{
				Assert.False(string.IsNullOrWhiteSpace(cell.Justification), $"{row.Id} {cell.Level} has no justification.");
			}
		}

		Assert.NotEmpty(QualificationDocuments.Schema(QualificationContract.MatrixSchemaFile)
			.Validate(MutateCell("Q38", "C3", static cell => cell.Remove("justification"))));
	}

	[Fact]
	public void Pass_kind_is_present_exactly_when_the_cell_passed()
	{
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in Matrix.Cells())
		{
			bool passed = string.Equals(cell.Status, "Passed", StringComparison.Ordinal);
			Assert.True(passed == (cell.PassKind is not null), $"{row.Id} {cell.Level}: {cell.Status} with passKind {cell.PassKind}.");
		}

		JsonSchemaSubset schema = QualificationDocuments.Schema(QualificationContract.MatrixSchemaFile);
		Assert.NotEmpty(schema.Validate(MutateCell("Q02", "C1", static cell => cell.Remove("passKind"))));
		Assert.NotEmpty(schema.Validate(MutateCell("Q02", "C3", static cell => cell["passKind"] = "Functional")));
	}

	[Fact]
	public void Host_level_cells_cite_the_qualifiable_profile_only()
	{
		IReadOnlyList<string> errors = QualificationRules.Profiles(Matrix, SupportProfile);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in Matrix.Cells())
		{
			if (cell.IsHostLevel)
			{
				Assert.True(string.Equals(cell.ProfileId, QualificationContract.QualifiableProfileId, StringComparison.Ordinal),
					$"{row.Id} {cell.Level} cites {cell.ProfileId}.");
			}
		}

		Assert.Contains(QualificationRules.Profiles(Read(MutateCell("Q02", "C3",
				static cell => cell["profileId"] = QualificationContract.DocumentaryProfileId)), SupportProfile),
			static error => error.Contains("qualifiable", StringComparison.Ordinal));
	}

	[Fact]
	public void Public_source_profile_is_refused_for_any_passed_or_failed_cell()
	{
		JsonElement fixtureCell = MutateCell("Q01", "C1",
			static cell => cell["profileId"] = QualificationContract.DocumentaryProfileId);
		JsonElement failedHostCell = MutateCell("Q04", "C3", static cell =>
		{
			cell["status"] = "Failed";
			cell["profileId"] = QualificationContract.DocumentaryProfileId;
		});

		Assert.Contains(QualificationRules.Profiles(Read(fixtureCell), SupportProfile),
			static error => error.Contains("documentary profile", StringComparison.Ordinal));
		Assert.Contains(QualificationRules.Profiles(Read(failedHostCell), SupportProfile),
			static error => error.Contains("documentary profile", StringComparison.Ordinal));
	}

	[Fact]
	public void Host_level_passed_or_failed_cells_cite_committed_receipts_with_matching_hashes()
	{
		IReadOnlyList<string> errors = QualificationRules.Receipts(Matrix, QualificationRules.LoadCommittedReceipt);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		JsonElement uncommitted = MutateCell("Q04", "C3", static cell =>
		{
			cell["status"] = "Passed";
			cell["passKind"] = "Functional";
			cell["evidenceKind"] = "ObservedHost";
			cell["evidence"] = ReceiptEvidence("R-20260924T101530Z-Q04-bfa967cc", "Q04");
			cell["treeHash"] = "40d7d7f741856c7e372e94bbd8532b06651eff5b";
			cell["nupkgSha256"] = "bfa967cc650ad859e5fd3164b53ae2081b6165fd5f2fa16af08b89621dfb70a4";
			cell["date"] = "2026-09-24";
		});
		Assert.Contains(QualificationRules.Receipts(Read(uncommitted), QualificationRules.LoadCommittedReceipt),
			static error => error.Contains("is not committed", StringComparison.Ordinal));
	}

	[Fact]
	public void C1_or_C2_evidence_is_never_accepted_for_a_host_level_cell()
	{
		JsonElement hostCellWithTests = MutateCell("Q02", "C3", static cell =>
		{
			cell["status"] = "Passed";
			cell["passKind"] = "Functional";
			cell["evidenceKind"] = "ObservedHost";
			cell["evidence"] = JsonNode.Parse(MatrixJson.GetProperty("rows")[1].GetProperty("levels").GetProperty("C1")
				.GetProperty("evidence").GetRawText());
			cell["treeHash"] = "40d7d7f741856c7e372e94bbd8532b06651eff5b";
			cell["nupkgSha256"] = "bfa967cc650ad859e5fd3164b53ae2081b6165fd5f2fa16af08b89621dfb70a4";
			cell["date"] = "2026-09-24";
		});

		IReadOnlyList<string> errors =
			QualificationDocuments.Schema(QualificationContract.MatrixSchemaFile).Validate(hostCellWithTests);

		Assert.Contains(errors, static error => error.Contains("/levels/C3/evidence/0", StringComparison.Ordinal));
	}

	[Fact]
	public void Automated_evidence_resolves_to_a_traited_method_in_a_CI_test_module()
	{
		IReadOnlySet<string> projects = QualificationDocuments.SolutionProjects();

		IReadOnlyList<string> errors = QualificationRules.AutomatedEvidence(Matrix, projects);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.Contains(QualificationRules.AutomatedEvidence(Read(MutateEvidence("Q02", "C1",
				static evidence => evidence["test"] = "PluginInitRecordTests.Missing_method")), projects),
			static error => error.Contains("no method Missing_method", StringComparison.Ordinal));
		Assert.Contains(QualificationRules.AutomatedEvidence(Read(MutateEvidence("Q03", "C1",
				static evidence => evidence["test"] = "EnablePluginTests.Enable_before_the_bootstrap_fails")), projects),
			static error => error.Contains("has no [Trait", StringComparison.Ordinal));
		Assert.Contains(QualificationRules.AutomatedEvidence(Read(MutateEvidence("Q02", "C1", static evidence =>
			{
				evidence["project"] = "tests/CheatEngine.SDK.LiveProbe.Tests/CheatEngine.SDK.Missing.Tests.csproj";
			})), projects),
			static error => error.Contains("not a *.Tests module", StringComparison.Ordinal));
	}

	[Fact]
	public void Every_Qualification_trait_in_the_tests_appears_in_the_matrix_and_vice_versa()
	{
		TestSourceIndex index = TestSourceIndex.Scan();

		IReadOnlyList<string> errors = QualificationRules.TraitParity(Matrix, index);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.True(index.Traits.Count >= 50, $"Only {index.Traits.Count} Qualification traits were found; the scan is broken.");
		Assert.Contains(index.Traits, static trait =>
			string.Equals(trait.Test, "ReentrancyTests.Disable_nested_in_OnDisable_is_refused_and_OnDisable_runs_once",
				StringComparison.Ordinal) && string.Equals(trait.Value, "Q07", StringComparison.Ordinal));
	}

	[Fact]
	public void Parent_rows_aggregate_their_sub_rows()
	{
		IReadOnlyList<string> errors = QualificationRules.Aggregation(Matrix);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.Equal("Failed", QualificationRules.Aggregate(["Passed", "Failed", "NotExecuted"]));
		Assert.Equal("Passed", QualificationRules.Aggregate(["Passed", "NotApplicable"]));
		Assert.Equal("NotApplicable", QualificationRules.Aggregate(["NotApplicable", "NotApplicable"]));
		Assert.Equal("NotExecuted", QualificationRules.Aggregate(["Passed", "NotExecuted", "NotApplicable"]));
		Assert.Contains(QualificationRules.Aggregation(Read(MutateCell("Q30.c", "C3", static cell =>
			{
				cell["status"] = "Failed";
			}))),
			static error => error.StartsWith("Q30 C3", StringComparison.Ordinal));
	}

	[Fact]
	public void Evidence_kind_follows_status_and_level()
	{
		IReadOnlyList<string> errors = QualificationRules.EvidenceKinds(Matrix);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.Contains(QualificationRules.EvidenceKinds(Read(MutateCell("Q02", "C1",
				static cell => cell["evidenceKind"] = "ObservedHost"))),
			static error => error.Contains("expected ObservedSource", StringComparison.Ordinal));
		Assert.Contains(QualificationRules.EvidenceKinds(Read(MutateCell("Q02", "C3",
				static cell => cell["evidenceKind"] = "DeclaredRepo"))),
			static error => error.Contains("expected ToQualify", StringComparison.Ordinal));
	}

	[Fact]
	public void Cells_citing_another_tree_or_package_carry_a_transfer_justification()
	{
		const string Tree = "40d7d7f741856c7e372e94bbd8532b06651eff5b";
		const string ReceiptId = "R-20260924T101530Z-Q04-bfa967cc";
		JsonElement receipt = QualificationDocuments.ParseJson(
			"""{ "receiptId": "R-20260924T101530Z-Q04-bfa967cc", "qualificationId": "Q04", "level": "C3", "status": "Passed", "profileId": "ce-7.7.0.10621-x64-managed-hostfxr", "repository": { "treeHash": "1111111111111111111111111111111111111111" }, "package": { "nupkgSha256": "bfa967cc650ad859e5fd3164b53ae2081b6165fd5f2fa16af08b89621dfb70a4" } }""");
		Func<string, QualificationRules.CommittedReceipt?> loader = _ =>
			new QualificationRules.CommittedReceipt(receipt, "862417b9e7c3720bcb3263cd873b09892d787823b6f9a0f453e42824c5a4d4b6");

		QualificationMatrix stale = Read(MutateCell("Q04", "C3", cell => PassWithReceipt(cell, Tree, ReceiptId, null)));
		QualificationMatrix transferred = Read(MutateCell("Q04", "C3", cell => PassWithReceipt(cell, Tree, ReceiptId,
			"The receipt tree differs only in docs/; the qualified code and package are identical.")));

		Assert.Contains(QualificationRules.Receipts(stale, loader),
			static error => error.Contains("transferJustification", StringComparison.Ordinal));
		Assert.Empty(QualificationRules.Receipts(transferred, loader));
	}

	[Fact]
	public void Not_executed_cells_never_carry_evidence_or_a_date()
	{
		foreach ((QualificationMatrix.Row row, QualificationMatrix.Cell cell) in Matrix.Cells())
		{
			if (string.Equals(cell.Status, "NotExecuted", StringComparison.Ordinal))
			{
				Assert.True(cell.Evidence.Count == 0 && cell.Date is null && cell.TreeHash is null,
					$"{row.Id} {cell.Level} is NotExecuted but carries evidence, a date or a tree.");
			}
		}

		Assert.NotEmpty(QualificationDocuments.Schema(QualificationContract.MatrixSchemaFile).Validate(
			MutateCell("Q02", "C1", static cell => cell["status"] = "NotExecuted")));
	}

	[Fact]
	public void Client_owned_rows_are_not_applicable_in_the_SDK_matrix_and_point_to_the_Client_matrix()
	{
		foreach (QualificationMatrix.Row row in Matrix.Rows)
		{
			if (!string.Equals(row.Owner, "Client", StringComparison.Ordinal))
			{
				continue;
			}

			foreach (QualificationMatrix.Cell cell in row.Levels.Values)
			{
				Assert.Equal("NotApplicable", cell.Status);
				Assert.Contains("https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/docs/qualification/matrix.json",
					cell.Justification, StringComparison.Ordinal);
			}
		}

		Assert.Equal(["Q33", "Q43", "Q44", "Q45"],
			Matrix.Rows.Where(static row => string.Equals(row.Owner, "Client", StringComparison.Ordinal))
				.Select(static row => row.Id), StringComparer.Ordinal);
	}

	[Fact]
	public void Findings_link_the_rows_the_audit_register_names()
	{
		Dictionary<string, string[]> expected = new(StringComparer.Ordinal)
		{
			["F01"] = ["Q02", "Q03", "Q04", "Q05", "Q06", "Q07", "Q08"],
			["F02"] = ["Q41", "Q42"],
			["F03"] = ["Q09", "Q10"],
			["F04"] = ["Q19"],
			["F05"] = ["Q25", "Q26", "Q40", "Q48"],
			["F06"] = ["Q27"],
			["F07"] = ["Q28", "Q29"],
			["F08"] = ["Q31", "Q32"],
			["F09"] = ["Q38", "Q39"],
			["F11"] = ["Q23", "Q24"],
			["F12"] = ["Q15", "Q16"]
		};

		foreach ((string finding, string[] rows) in expected)
		{
			Assert.Equal(rows,
				Matrix.Rows.Where(row => row.Parent is null && row.Findings.Contains(finding, StringComparer.Ordinal))
					.Select(static row => row.Id), StringComparer.Ordinal);
		}
	}

	[Fact]
	public void Matrix_summary_in_the_readme_equals_the_matrix()
	{
		string readme = QualificationDocuments.ReadNormalizedText(QualificationDocuments.ReadmePath);
		string expected = QualificationMarkdown.MatrixSummary(Matrix);

		string? actual = QualificationMarkdown.GeneratedBlock(readme, QualificationMarkdown.MatrixSummaryMarker);

		Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
			$"Replace the {QualificationMarkdown.MatrixSummaryMarker} block of {QualificationDocuments.ReadmePath} with:{Environment.NewLine}{expected}");
	}

	private static Dictionary<string, string[]> BuildOracle()
	{
		Dictionary<string, string[]> oracle = new(StringComparer.Ordinal);
		void Add(string[] levels, params string[] ids)
		{
			foreach (string id in ids)
			{
				oracle.Add(id, levels);
			}
		}

		Add(["C0", "C1"], "Q01");
		Add(["C1", "C3"], "Q02", "Q03", "Q06", "Q07", "Q08", "Q25", "Q27", "Q28", "Q29", "Q32", "Q33", "Q39", "Q43",
			"Q44", "Q45", "Q46", "Q48");
		Add(["C3"], "Q04", "Q05", "Q18", "Q26", "Q31", "Q34", "Q35", "Q36", "Q37", "Q38", "Q40");
		Add(["C4"], "Q09", "Q10");
		Add(["C1", "C2"], "Q11");
		Add(["C2"], "Q12", "Q13");
		Add(["C2", "C3"], "Q14", "Q15", "Q17", "Q22", "Q23", "Q24", "Q47");
		Add(["C2", "C4"], "Q16");
		Add(["C3", "C4"], "Q19", "Q30", "Q42");
		Add(["C1", "C2", "C3"], "Q20", "Q21");
		Add(["C0", "C2"], "Q41");
		return oracle;
	}

	private static QualificationMatrix Read(JsonElement matrix)
	{
		return QualificationMatrix.Read(matrix);
	}

	private static JsonElement MutateCell(string rowId, string level, Action<JsonObject> change)
	{
		JsonNode document = JsonNode.Parse(MatrixJson.GetRawText())!;
		foreach (JsonNode? row in document["rows"]!.AsArray())
		{
			if (string.Equals((string?) row!["id"], rowId, StringComparison.Ordinal))
			{
				change(row["levels"]![level]!.AsObject());
			}
		}

		return QualificationDocuments.ParseJson(document.ToJsonString());
	}

	private static JsonElement MutateEvidence(string rowId, string level, Action<JsonObject> change)
	{
		return MutateCell(rowId, level, cell => change(cell["evidence"]![0]!.AsObject()));
	}

	private static JsonArray ReceiptEvidence(string receiptId, string rowId)
	{
		return
		[
			new JsonObject
			{
				["kind"] = "Receipt",
				["receiptId"] = receiptId,
				["path"] = QualificationDocuments.ReceiptDirectory + "/" + rowId + "/" + receiptId + ".json",
				["sha256"] = "862417b9e7c3720bcb3263cd873b09892d787823b6f9a0f453e42824c5a4d4b6"
			}
		];
	}

	private static void PassWithReceipt(JsonObject cell, string tree, string receiptId, string? transfer)
	{
		cell["status"] = "Passed";
		cell["passKind"] = "Functional";
		cell["evidenceKind"] = "ObservedHost";
		cell["evidence"] = ReceiptEvidence(receiptId, "Q04");
		cell["treeHash"] = tree;
		cell["nupkgSha256"] = "bfa967cc650ad859e5fd3164b53ae2081b6165fd5f2fa16af08b89621dfb70a4";
		cell["transferJustification"] = transfer;
		cell["date"] = "2026-09-24";
	}
}
