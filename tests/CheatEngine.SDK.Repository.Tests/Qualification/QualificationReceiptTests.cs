using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.Qualification;

/// <summary>
///     The v0 receipt contract: a complete, redacted C3/C4 receipt of the exact host and CI package is accepted; a
///     receipt that cites the documentary profile, leaks a local path, omits its pass kind or its tree and package
///     identity, or comes from a local pack is refused. Committed receipts are also checked where they are committed.
/// </summary>
public sealed class QualificationReceiptTests
{
	private const string SampleReceipt =
		"""
		{
		  "schema": "cheatengine-qualification-receipt/v0",
		  "receiptId": "R-20260924T101530Z-Q04-bfa967cc",
		  "qualificationId": "Q04",
		  "level": "C3",
		  "profileId": "ce-7.7.0.10621-x64-managed-hostfxr",
		  "operator": "AriusII",
		  "loadRoute": "LuaLoadPlugin",
		  "repository": {
		    "name": "CheatEngineNet/CheatEngine.SDK",
		    "treeHash": "40d7d7f741856c7e372e94bbd8532b06651eff5b",
		    "commit": "e77fb34c1f4e9c0e9d0e4a4a3b6c7d8e9f0a1b2c",
		    "pullRequest": { "number": 86, "headSha": "e77fb34c1f4e9c0e9d0e4a4a3b6c7d8e9f0a1b2c" }
		  },
		  "runner": {
		    "script": "eng/qualification/Invoke-LocalQualification.ps1",
		    "scriptSha256": "21a0270b7f66a1e4c25933f13a1e5a1bbb4757578072930c8189131f9c6aaae1",
		    "sourceRepository": "CheatEngineNet/CheatEngine.SDK",
		    "sourceCommit": "e77fb34c1f4e9c0e9d0e4a4a3b6c7d8e9f0a1b2c",
		    "mutex": "Global\\ce-lab"
		  },
		  "package": {
		    "id": "CheatEngine.SDK",
		    "version": "2.0.0-alpha.0.12",
		    "nupkgSha256": "bfa967cc650ad859e5fd3164b53ae2081b6165fd5f2fa16af08b89621dfb70a4",
		    "contentHashSha512": "DOHZH8TYAm/L/qVXbpPlnoHh34r0GPB9erQYfBy0gyG84KP2iPVW6tFiuSX89K825zxz2u/S21JcFADMQVaUKw==",
		    "source": "CiArtifact",
		    "ciRunUrl": "https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/1234567890"
		  },
		  "host": {
		    "ceExeName": "cheatengine-x86_64.exe",
		    "ceExeSha256": "9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d",
		    "ceFileVersion": "7.7.0.10621",
		    "luaDllSha256": "c95dcdfa0f60f97b43d970d77fd1bb907af4de04b500a3c89a99600b20b35bd2",
		    "runtimeconfigSha256": "68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b",
		    "autorunSha256": "b4def8217cadae26d4da633fd2a4e58e326cbb5d570afdc3989484da07af3579",
		    "sandboxCopy": true,
		    "osVersion": "Microsoft Windows NT 10.0.26200.0",
		    "dotnetRuntimes": [ "Microsoft.NETCore.App 10.0.12 x64", "Microsoft.WindowsDesktop.App 10.0.12 x64" ]
		  },
		  "bridge": {
		    "sha256": "889dc4c231d182f9b7baa9e29880555aad949f42023dde232fe327542c3c5387",
		    "sourceFingerprint": "3342be23f88976d9209a24bc0d8b9db512482a24d8db90381a836ea4f5595a56:2871368515be4c6fd235e49e793d5557e7c50229fcc8fbfd903efd39f9b754a8"
		  },
		  "bundles": [
		    {
		      "name": "LiveProbe",
		      "manifestSha256": "1e6ed65d77d6364eeaed5a745ba5c4985ae2b700dd85d7cf7f027bdf294a33fc",
		      "files": [
		        { "path": "CheatEngine.SDK.LiveProbe.dll", "sha256": "5e689e2b01672bf33996e75d5e372ff60c536ce1599a1458e867cd8f4bef5160" },
		        { "path": "cheatengine-sdk-lua-bridge.dll", "sha256": "889dc4c231d182f9b7baa9e29880555aad949f42023dde232fe327542c3c5387" }
		      ]
		    }
		  ],
		  "target": {
		    "kind": "QualificationTarget",
		    "arch": "x64",
		    "sha256": "34a04005bcaf206eec990bd9637d9fdb6725e0a0c0d4aebf003f17f4c956eb5c"
		  },
		  "registry": {
		    "key": "HKCU\\Software\\Cheat Engine",
		    "exportBeforeSha256": "ed72904e38a86ce1c168326fb13f14acf5655d8b82e94fdb74673f7f9b030b71",
		    "exportAfterSha256": "ed72904e38a86ce1c168326fb13f14acf5655d8b82e94fdb74673f7f9b030b71",
		    "restored": false,
		    "diff": { "added": 0, "removed": 0, "changed": 0, "valueNames": [] }
		  },
		  "preconditions": [ "LiveProbe loaded from the exact CI package through loadPlugin." ],
		  "operation": "Load the plugin and read ce77_live_probe_status_json().",
		  "expected": "The raw second bootstrap integer is recorded without interpretation.",
		  "observed": "bootstrap.opaqueSecondInt = 0; interpretation none.",
		  "status": "Passed",
		  "passKind": "Functional",
		  "evidenceKind": "ObservedHost",
		  "justification": null,
		  "timings": {
		    "startedUtc": "2026-09-24T10:15:30Z",
		    "finishedUtc": "2026-09-24T10:15:41Z",
		    "durationMs": 11000,
		    "ceStartMs": 2100
		  },
		  "eventLog": {
		    "path": "R-20260924T101530Z-Q04-bfa967cc.events.json",
		    "sha256": "862417b9e7c3720bcb3263cd873b09892d787823b6f9a0f453e42824c5a4d4b6",
		    "format": "cheatengine-qualification-events/v0",
		    "redactions": [ "<workRoot>", "<sandbox>", "<bundle:LiveProbe>" ]
		  },
		  "transferJustification": null,
		  "createdUtc": "2026-09-24T10:15:42Z"
		}
		""";

	private const string SampleEvents =
		"""
		{
		  "schema": "cheatengine-qualification-events/v0",
		  "receiptId": "R-20260924T101530Z-Q04-bfa967cc",
		  "events": [
		    { "tMs": 0, "source": "Runner", "kind": "Preflight", "message": "Host, Lua module and runtime configuration equal profile ce-7.7.0.10621-x64-managed-hostfxr." },
		    { "tMs": 2100, "source": "Driver", "kind": "LoadPlugin", "message": "loadPlugin('<bundle:LiveProbe>/CheatEngine.SDK.LiveProbe.dll') returned 0." },
		    { "tMs": 2400, "source": "Plugin", "kind": "LuaResult", "message": "{\"schema\":\"ce77-live-probe-status-v1\",\"bootstrap\":{\"opaqueSecondInt\":0,\"interpretation\":\"none\"}}" },
		    { "tMs": 9800, "source": "Driver", "kind": "CloseCE", "message": "closeCE() requested." }
		  ]
		}
		""";

	private static JsonElement SupportProfile => QualificationDocuments.LoadJson(QualificationDocuments.SupportProfilePath);

	[Fact]
	public void A_complete_sample_receipt_is_accepted()
	{
		JsonElement receipt = QualificationDocuments.ParseJson(SampleReceipt);

		IReadOnlyList<string> errors = ReceiptRules.Validate(receipt, SupportProfile);
		IReadOnlyList<string> eventErrors = ReceiptRules.ValidateEventLog(QualificationDocuments.ParseJson(SampleEvents),
			"R-20260924T101530Z-Q04-bfa967cc");

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.True(eventErrors.Count == 0, string.Join(Environment.NewLine, eventErrors));
	}

	[Fact]
	public void A_receipt_citing_the_public_source_profile_is_refused()
	{
		IReadOnlyList<string> errors = Validate(static receipt =>
			receipt["profileId"] = QualificationContract.DocumentaryProfileId);

		Assert.Contains(errors, static error => error.Contains("never qualifiable", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("observed", @"Loaded from C:\Users\someone\ce\CheatEngine.SDK.LiveProbe.dll.")]
	[InlineData("observed", "Loaded from d:/CheatEngine/bundles/plugin.dll.")]
	[InlineData("operation", "Open file:///c:/sandbox/cheatengine-x86_64.exe.")]
	[InlineData("expected", @"Nothing is read from \Users\someone.")]
	public void A_receipt_with_an_absolute_local_path_is_refused(string field, string text)
	{
		IReadOnlyList<string> errors = Validate(receipt => receipt[field] = text);

		Assert.Contains(errors, static error => error.Contains("absolute local path", StringComparison.Ordinal));
	}

	[Fact]
	public void A_passed_receipt_without_a_pass_kind_is_refused()
	{
		IReadOnlyList<string> missing = Validate(static receipt => receipt.Remove("passKind"));
		IReadOnlyList<string> nulled = Validate(static receipt => receipt["passKind"] = null);
		IReadOnlyList<string> notApplicableWithPassKind = Validate(static receipt =>
		{
			receipt["status"] = "NotApplicable";
			receipt["justification"] = "The run used another host.";
		});

		Assert.Contains(missing, static error => error.Contains("'passKind'", StringComparison.Ordinal));
		Assert.NotEmpty(nulled);
		Assert.Contains(notApplicableWithPassKind, static error => error.Contains("/passKind", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("repository", "treeHash")]
	[InlineData("package", "nupkgSha256")]
	[InlineData("package", "contentHashSha512")]
	[InlineData("repository", "pullRequest")]
	public void A_host_level_receipt_without_tree_and_package_identity_is_refused(string section, string field)
	{
		IReadOnlyList<string> errors = Validate(receipt => receipt[section]!.AsObject().Remove(field));

		Assert.Contains(errors, error => error.Contains("'" + field + "'", StringComparison.Ordinal));
	}

	[Fact]
	public void A_receipt_whose_package_source_is_a_local_pack_is_refused()
	{
		IReadOnlyList<string> localPack = Validate(static receipt => receipt["package"]!["source"] = "LocalPack");
		IReadOnlyList<string> artifactWithoutRun = Validate(static receipt => receipt["package"]!["ciRunUrl"] = null);

		Assert.Contains(localPack, static error => error.Contains("/package/source", StringComparison.Ordinal));
		Assert.Contains(artifactWithoutRun, static error => error.Contains("/package/ciRunUrl", StringComparison.Ordinal));
	}

	[Fact]
	public void Receipt_id_encodes_its_time_and_qualification_id()
	{
		JsonElement sample = QualificationDocuments.ParseJson(SampleReceipt);

		IReadOnlyList<string> otherTime = Validate(static receipt =>
			receipt["timings"]!["startedUtc"] = "2026-09-24T10:15:31Z");
		IReadOnlyList<string> otherScenario = Validate(static receipt => receipt["qualificationId"] = "Q05");
		IReadOnlyList<string> otherPackage = Validate(static receipt =>
			receipt["package"]!["nupkgSha256"] = "0000000000000000000000000000000000000000000000000000000000000000");

		Assert.Equal("R-20260924T101530Z-Q04-bfa967cc", ReceiptRules.ExpectedReceiptId(sample));
		Assert.Contains(otherTime, static error => error.Contains("R-20260924T101531Z-Q04-bfa967cc", StringComparison.Ordinal));
		Assert.Contains(otherScenario, static error => error.Contains("R-20260924T101530Z-Q05-bfa967cc", StringComparison.Ordinal));
		Assert.Contains(otherPackage, static error => error.Contains("R-20260924T101530Z-Q04-00000000", StringComparison.Ordinal));
	}

	[Fact]
	public void A_fixture_level_receipt_is_refused()
	{
		IReadOnlyList<string> errors = Validate(static receipt => receipt["level"] = "C2");

		Assert.Contains(errors, static error => error.Contains("/level", StringComparison.Ordinal));
	}

	[Fact]
	public void A_run_on_another_host_is_accepted_only_as_a_justified_not_applicable_receipt()
	{
		const string OtherHost = "9d861d651ab9d1dc3c09ae34c8ed5dee3d1a29b080784c3c48773494c9350230";

		IReadOnlyList<string> passed = Validate(static receipt => receipt["host"]!["ceExeSha256"] = OtherHost);
		IReadOnlyList<string> notApplicable = Validate(static receipt =>
		{
			receipt["host"]!["ceExeSha256"] = OtherHost;
			receipt["status"] = "NotApplicable";
			receipt["passKind"] = null;
			receipt["justification"] = "Preflight: the executable is the SSE4-AVX2 variant, which is not profiled.";
		});
		IReadOnlyList<string> unjustified = Validate(static receipt =>
		{
			receipt["host"]!["ceExeSha256"] = OtherHost;
			receipt["status"] = "NotApplicable";
			receipt["passKind"] = null;
		});

		Assert.Contains(passed, static error => error.Contains("host.ceExeSha256", StringComparison.Ordinal));
		Assert.True(notApplicable.Count == 0, string.Join(Environment.NewLine, notApplicable));
		Assert.Contains(unjustified, static error => error.Contains("/justification", StringComparison.Ordinal));
	}

	[Fact]
	public void Every_committed_receipt_is_valid_and_its_event_log_hash_matches()
	{
		List<string> errors = [];
		foreach (string receipt in QualificationDocuments.CommittedReceipts())
		{
			errors.AddRange(ReceiptRules.ValidateCommitted(receipt, SupportProfile));
		}

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
	}

	[Fact]
	public void Committed_event_logs_contain_no_user_path_or_raw_debug_output()
	{
		// The detectors are proven on synthetic lines first, so an empty receipts folder cannot hide a broken rule.
		Assert.True(TextRules.ContainsRawDebugOutput("00000042\t12.50000000\t[12345] [CheatEngine.SDK.Hosting] x"));
		Assert.True(TextRules.ContainsRawDebugOutput("[CheatEngine.SDK.Hosting] Information: Plugin 7 enabled."));
		Assert.False(TextRules.ContainsRawDebugOutput("{\"schema\":\"ce77-live-probe-status-v1\"}"));
		Assert.True(TextRules.ContainsAbsoluteLocalPath(@"C:\Users\someone\AppData\Local"));
		Assert.False(TextRules.ContainsAbsoluteLocalPath("<workRoot>/bundles/LiveProbe and https://github.com/x"));

		List<string> problems = [];
		foreach (string log in QualificationDocuments.CommittedEventLogs())
		{
			JsonElement events = QualificationDocuments.LoadJson(log);
			foreach (string path in TextRules.AbsoluteLocalPaths(events))
			{
				problems.Add($"{log}: unredacted local path at {path}");
			}

			foreach (JsonElement item in events.GetProperty("events").EnumerateArray())
			{
				if (TextRules.ContainsRawDebugOutput(item.GetProperty("message").GetString() ?? string.Empty))
				{
					problems.Add($"{log}: raw debug output at tMs {item.GetProperty("tMs").GetInt64()}");
				}
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Every_committed_receipt_is_referenced_by_the_matrix_cell_it_qualifies()
	{
		IReadOnlyList<string> receipts = QualificationDocuments.CommittedReceipts();
		if (receipts.Count == 0)
		{
			return;
		}

		QualificationMatrix matrix = QualificationMatrix.Read(QualificationDocuments.LoadJson(QualificationDocuments.MatrixPath));
		List<string> unreferenced = [];
		foreach (string path in receipts)
		{
			JsonElement receipt = QualificationDocuments.LoadJson(path);
			string receiptId = receipt.GetProperty("receiptId").GetString()!;
			QualificationMatrix.Row? row = matrix.Find(receipt.GetProperty("qualificationId").GetString()!);
			bool referenced = row is not null &&
							  row.Levels.TryGetValue(receipt.GetProperty("level").GetString()!,
								  out QualificationMatrix.Cell? cell) &&
							  cell.Evidence.Any(evidence =>
								  string.Equals(evidence.ReceiptId, receiptId, StringComparison.Ordinal) &&
								  string.Equals(evidence.Path, path, StringComparison.Ordinal));
			if (!referenced)
			{
				unreferenced.Add(path);
			}
		}

		Assert.True(unreferenced.Count == 0,
			"Commit the matrix cell update together with these receipts: " + string.Join(", ", unreferenced));
	}

	private static IReadOnlyList<string> Validate(Action<JsonObject> change)
	{
		JsonObject receipt = JsonNode.Parse(SampleReceipt)!.AsObject();
		change(receipt);
		return ReceiptRules.Validate(QualificationDocuments.ParseJson(receipt.ToJsonString()), SupportProfile);
	}
}
