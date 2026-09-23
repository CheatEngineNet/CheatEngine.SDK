using System.Text.Json.Nodes;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>eng/release/Test-ReleaseQualification.ps1</c> is the Checkpoint F gate of a release (audit README, AR-06): a
///     stable tag needs matrix rows Q02-Q10, Q40 and Q41 passed at every required level for the released tree, or waived
///     in the release notes; a prerelease only reports the open rows. The matrices here are synthetic and follow the v0
///     shape of shared contract 2.2; a host (C3/C4) pass counts only for the tree it names or with a transfer
///     justification, and C1/C2 evidence never replaces a required C3/C4 level.
/// </summary>
public sealed class ReleaseQualificationGateTests : IDisposable
{
	private const string Script = "eng/release/Test-ReleaseQualification.ps1";
	private const string ReleaseTree = "0123456789abcdef0123456789abcdef01234567";
	private const string OtherTree = "fedcba9876543210fedcba9876543210fedcba98";

	private static readonly string[] s_gatingRows = ["Q02", "Q03", "Q04", "Q05", "Q06", "Q07", "Q08", "Q09", "Q10", "Q40", "Q41"];

	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cheatengine-sdk-gate-");

	/// <inheritdoc />
	public void Dispose()
	{
		_directory.Delete(true);
	}

	[Fact]
	public async Task Qualified_matrix_passes_a_stable_release()
	{
		JsonObject matrix = QualifiedMatrix();

		ProcessResult run = await RunAsync(matrix, notes: "### Added\n\n- Something.\n", "Enforce");

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Contains("| Q40 | Passed |", run.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("| Open |", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Stable_release_fails_when_a_gating_row_is_not_executed()
	{
		JsonObject matrix = QualifiedMatrix();
		Cell(matrix, "Q05", "C3")["status"] = "NotExecuted";
		Cell(matrix, "Q05", "C3").Remove("passKind");

		ProcessResult run = await RunAsync(matrix, notes: "", "Enforce");

		Assert.Equal(1, run.ExitCode);
		Assert.Contains("| Q05 | Open | C3 is NotExecuted |", run.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("::error::A stable release requires qualification of Q05 ", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Waived_row_listed_in_release_notes_passes_the_gate()
	{
		JsonObject matrix = QualifiedMatrix();
		Cell(matrix, "Q40", "C3")["status"] = "NotExecuted";
		Cell(matrix, "Q40", "C3").Remove("passKind");
		const string notes = "### Changed\n\n- Q40: not a waiver outside the section.\n\n### Qualification waivers\n\n" +
							 "- Q40: the clean-folder load in Cheat Engine 7.7 is scheduled for 2.0.1.\n";
		const string wrongSection = "### Changed\n\n- Q40: not a waiver outside the section.\n";

		ProcessResult waived = await RunAsync(matrix, notes, "Enforce");
		ProcessResult notWaived = await RunAsync(matrix, wrongSection, "Enforce");

		Assert.True(waived.ExitCode == 0, waived.CombinedOutput);
		Assert.Contains("| Q40 | Waived | waived: the clean-folder load in Cheat Engine 7.7 is scheduled for 2.0.1. (C3 is NotExecuted) |",
			waived.StandardOutput, StringComparison.Ordinal);
		Assert.Equal(1, notWaived.ExitCode);
	}

	[Fact]
	public async Task Prerelease_reports_open_rows_without_failing()
	{
		JsonObject matrix = QualifiedMatrix();
		Cell(matrix, "Q02", "C3")["status"] = "Failed";
		Cell(matrix, "Q41", "C2")["status"] = "NotExecuted";

		ProcessResult run = await RunAsync(matrix, notes: "", "Report");

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Contains("::notice title=Qualification gate::Open rows Q02, Q41;", run.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("| Q02 | Open | C3 is Failed |", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Stale_c3_cell_without_transfer_justification_does_not_count()
	{
		JsonObject matrix = QualifiedMatrix();
		Cell(matrix, "Q09", "C3")["treeHash"] = OtherTree;
		JsonObject transferred = QualifiedMatrix();
		Cell(transferred, "Q09", "C3")["treeHash"] = OtherTree;
		Cell(transferred, "Q09", "C3")["transferJustification"] = "Only the receipt commit changed the tree after the run.";

		ProcessResult stale = await RunAsync(matrix, notes: "", "Enforce");
		ProcessResult justified = await RunAsync(transferred, notes: "", "Enforce");

		Assert.Equal(1, stale.ExitCode);
		Assert.Contains($"| Q09 | Open | C3 passed on tree {OtherTree}, not on the release tree, without a transfer justification |",
			stale.StandardOutput, StringComparison.Ordinal);
		Assert.True(justified.ExitCode == 0, justified.CombinedOutput);
	}

	[Fact]
	public async Task Not_applicable_needs_a_justification_and_lower_levels_never_replace_c3()
	{
		JsonObject matrix = QualifiedMatrix();
		JsonObject cell = Cell(matrix, "Q06", "C3");
		cell["status"] = "NotApplicable";
		cell.Remove("passKind");
		JsonObject lowerOnly = QualifiedMatrix();
		JsonObject levels = Row(lowerOnly, "Q07")["levels"]!.AsObject();
		levels.Remove("C3");
		levels["C1"] = PassedCell(null);

		ProcessResult unjustified = await RunAsync(matrix, notes: "", "Enforce");
		cell["justification"] = "The scenario needs a second plugin loader, which this profile does not have.";
		ProcessResult justified = await RunAsync(matrix, notes: "", "Enforce");
		ProcessResult lower = await RunAsync(lowerOnly, notes: "", "Enforce");

		Assert.Equal(1, unjustified.ExitCode);
		Assert.Contains("| Q06 | Open | C3 is NotApplicable without a justification |", unjustified.StandardOutput, StringComparison.Ordinal);
		Assert.True(justified.ExitCode == 0, justified.CombinedOutput);
		Assert.Equal(1, lower.ExitCode);
		Assert.Contains("| Q07 | Open | C3 has no cell |", lower.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Missing_matrix_fails_a_stable_release()
	{
		string absent = Path.Combine(_directory.FullName, "absent", "matrix.json");

		ProcessResult stable = await RunFileAsync(absent, null, "Enforce");
		ProcessResult prerelease = await RunFileAsync(absent, null, "Report");

		Assert.Equal(1, stable.ExitCode);
		Assert.Contains("| Q02 | Open | docs/qualification/matrix.json is absent |", stable.StandardOutput, StringComparison.Ordinal);
		Assert.True(prerelease.ExitCode == 0, prerelease.CombinedOutput);
		Assert.Contains("::notice title=Qualification gate::Open rows Q02, Q03,", prerelease.StandardOutput, StringComparison.Ordinal);
	}

	private async Task<ProcessResult> RunAsync(JsonObject matrix, string notes, string mode)
	{
		string matrixPath = Path.Combine(_directory.FullName, "matrix.json");
		string notesPath = Path.Combine(_directory.FullName, "release-notes.md");
		await File.WriteAllTextAsync(matrixPath, matrix.ToJsonString(), TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(notesPath, notes, TestContext.Current.CancellationToken);
		return await RunFileAsync(matrixPath, notesPath, mode);
	}

	private static Task<ProcessResult> RunFileAsync(string matrixPath, string? notesPath, string mode)
	{
		List<string> arguments = ["-MatrixPath", matrixPath, "-TreeHash", ReleaseTree, "-Mode", mode];
		if (notesPath is not null)
		{
			arguments.AddRange(["-ReleaseNotesPath", notesPath]);
		}

		return PowerShellScript.RunFileAsync(Script, [.. arguments]);
	}

	/// <summary>
	///     A matrix where every gating row passes: C3 rows with a host receipt for the release tree (the sub-row Q05.a is
	///     present to show only parent rows are gated), Q41 at C0/C2 with automated evidence, plus a non-gating row.
	/// </summary>
	private static JsonObject QualifiedMatrix()
	{
		JsonArray rows = new();
		foreach (string id in s_gatingRows)
		{
			string[] required = id is "Q41" ? ["C0", "C2"] : ["C3"];
			JsonObject levels = new();
			foreach (string level in required)
			{
				levels[level] = PassedCell(level is "C3" ? ReleaseTree : null);
			}

			rows.Add(new JsonObject
			{
				["id"] = id,
				["parent"] = null,
				["requiredLevels"] = new JsonArray(Array.ConvertAll(required, static level => (JsonNode?) JsonValue.Create(level))),
				["levels"] = levels
			});
		}

		rows.Add(new JsonObject
		{
			["id"] = "Q05.a",
			["parent"] = "Q05",
			["requiredLevels"] = new JsonArray("C3"),
			["levels"] = new JsonObject { ["C3"] = new JsonObject { ["status"] = "NotExecuted", ["evidenceKind"] = "ToQualify" } }
		});
		rows.Add(new JsonObject
		{
			["id"] = "Q30",
			["parent"] = null,
			["requiredLevels"] = new JsonArray("C3", "C4"),
			["levels"] = new JsonObject { ["C3"] = new JsonObject { ["status"] = "NotExecuted", ["evidenceKind"] = "ToQualify" } }
		});
		return new JsonObject
		{
			["schema"] = "cheatengine-qualification-matrix/v0",
			["repository"] = "CheatEngineNet/CheatEngine.SDK",
			["rows"] = rows
		};
	}

	private static JsonObject PassedCell(string? treeHash)
	{
		JsonObject cell = new()
		{
			["status"] = "Passed",
			["passKind"] = "Functional",
			["evidenceKind"] = treeHash is null ? "ObservedSource" : "ObservedHost",
			["date"] = "2026-09-23"
		};
		if (treeHash is not null)
		{
			cell["profileId"] = "ce-7.7.0.10621-x64-managed-hostfxr";
			cell["treeHash"] = treeHash;
		}

		return cell;
	}

	private static JsonObject Row(JsonObject matrix, string id)
	{
		foreach (JsonNode? row in matrix["rows"]!.AsArray())
		{
			if (string.Equals((string?) row!["id"], id, StringComparison.Ordinal))
			{
				return row.AsObject();
			}
		}

		throw new InvalidOperationException($"The synthetic matrix has no row {id}.");
	}

	private static JsonObject Cell(JsonObject matrix, string id, string level)
	{
		return Row(matrix, id)["levels"]![level]!.AsObject();
	}
}
