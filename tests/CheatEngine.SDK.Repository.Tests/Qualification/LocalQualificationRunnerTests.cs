using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.Qualification;

/// <summary>
///     The local exact-host runner (<c>eng/qualification</c>) without Cheat Engine: only its CI guard runs the script; the
///     other tests read it statically or import its pure module into <c>pwsh</c> with synthetic input. Nothing here takes
///     the <c>Global\ce-lab</c> mutex, touches HKCU or reads the Cheat Engine installation.
/// </summary>
public sealed class LocalQualificationRunnerTests
{
	private const string RunnerScript = "eng/qualification/Invoke-LocalQualification.ps1";
	private const string RunnerModule = "eng/qualification/QualificationRunner.psm1";
	private const string Scenarios = "eng/qualification/scenarios.json";
	private const string DriverTemplate = "eng/qualification/driver/zz_cesdk_qualification.template.lua";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly string[] CiMarkers = ["CI", "GITHUB_ACTIONS", "TF_BUILD"];

	private static readonly string[] CheckpointB =
	[
		"Q02", "Q03", "Q04", "Q05", "Q05.a", "Q06", "Q07", "Q08", "Q09.a", "Q09.b", "Q14", "Q15", "Q17", "Q18", "Q19",
		"Q39", "Q40"
	];

	private static string ModuleImport =>
		"Import-Module " + PowerShellProcess.Quote(QualificationDocuments.Absolute(RunnerModule)) + " -Force; ";

	[Theory]
	[InlineData("CI")]
	[InlineData("GITHUB_ACTIONS")]
	[InlineData("TF_BUILD")]
	public void Runner_refuses_to_run_under_CI_before_any_side_effect(string marker)
	{
		string workRoot = Path.Combine(Path.GetTempPath(), "cesdk-guard-" + Guid.NewGuid().ToString("N"));
		Dictionary<string, string?> environment = new(StringComparer.Ordinal);
		foreach (string name in CiMarkers)
		{
			environment[name] = string.Equals(name, marker, StringComparison.Ordinal) ? "true" : null;
		}

		PowerShellProcess.Result result = PowerShellProcess.RunFile(QualificationDocuments.Absolute(RunnerScript),
			["-Scenario", "Q04", "-PackagePath", "missing.nupkg", "-WorkRoot", workRoot], environment,
			TestContext.Current.CancellationToken);

		Assert.True(result.ExitCode == 3, result.Transcript);
		Assert.Contains(marker + " is set", result.Error, StringComparison.Ordinal);
		Assert.False(Directory.Exists(workRoot), "The guard must run before anything is created.");
	}

	[Fact]
	public void No_workflow_references_the_local_qualification_runner()
	{
		List<string> scanned = [];
		List<string> offenders = [];
		string github = QualificationDocuments.Absolute(".github");
		foreach (string file in Directory.EnumerateFiles(github, "*.*", SearchOption.AllDirectories))
		{
			if (!file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
				!file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string relative = Infrastructure.RepositoryRoot.ToRelative(file);
			scanned.Add(relative);
			if (File.ReadAllText(file).Contains("eng/qualification", StringComparison.OrdinalIgnoreCase))
			{
				offenders.Add(relative);
			}
		}

		Assert.Contains(scanned, static file => file.StartsWith(".github/workflows/", StringComparison.Ordinal));
		Assert.True(offenders.Count == 0,
			"The local runner starts Cheat Engine and must never run in CI: " + string.Join(", ", offenders));
	}

	[Fact]
	public void Runner_guard_is_the_first_statement_and_the_script_runs_in_strict_mode()
	{
		JsonElement facts = RunJson($$"""
			$errors = $null
			$ast = [System.Management.Automation.Language.Parser]::ParseFile({{PowerShellProcess.Quote(QualificationDocuments.Absolute(RunnerScript))}}, [ref] $null, [ref] $errors)
			$statements = @($ast.EndBlock.Statements)
			[ordered]@{
				parseErrors = @($errors).Count
				requiredVersion = "$($ast.ScriptRequirements.RequiredPSVersion)"
				supportsShouldProcess = $ast.ParamBlock.Attributes.Extent.Text -join ' '
				firstStatement = $statements[0].Extent.Text
				strictMode = @($statements | Where-Object { $_.Extent.Text -eq 'Set-StrictMode -Version Latest' }).Count
				stopOnError = @($statements | Where-Object { $_.Extent.Text -eq '$ErrorActionPreference = ''Stop''' }).Count
				traps = @($ast.EndBlock.Traps | ForEach-Object { $_.Extent.Text })
			} | ConvertTo-Json -Compress
			""");

		Assert.Equal(0, facts.GetProperty("parseErrors").GetInt32());
		Assert.Equal("7.4", facts.GetProperty("requiredVersion").GetString());
		Assert.Contains("SupportsShouldProcess", facts.GetProperty("supportsShouldProcess").GetString(), StringComparison.Ordinal);
		string first = facts.GetProperty("firstStatement").GetString()!;
		foreach (string marker in CiMarkers)
		{
			Assert.Contains("'" + marker + "'", first, StringComparison.Ordinal);
		}

		Assert.Contains("exit 3", first, StringComparison.Ordinal);
		Assert.Equal(1, facts.GetProperty("strictMode").GetInt32());
		Assert.Equal(1, facts.GetProperty("stopOnError").GetInt32());

		// An unhandled error maps to the documented exit code 6, not to PowerShell's generic 1.
		JsonElement trap = Assert.Single(facts.GetProperty("traps").EnumerateArray());
		Assert.Contains("exit 6", trap.GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Runner_never_writes_to_the_Cheat_Engine_source_directory()
	{
		JsonElement report = RunJson(WriteTargetAnalysis(QualificationDocuments.Absolute(RunnerScript)));

		Assert.True(report.GetProperty("reads").GetInt32() >= 3, "The analysis found no read of $CheatEnginePath.");
		Assert.True(report.GetProperty("violations").GetArrayLength() == 0, report.GetProperty("violations").GetRawText());
	}

	[Fact]
	public void Receipt_builder_produces_a_schema_valid_receipt_from_a_recorded_event_log()
	{
		JsonElement result = RunJson(ModuleImport + $$"""
			$plan = Get-Content -LiteralPath {{PowerShellProcess.Quote(QualificationDocuments.Absolute(Scenarios))}} -Raw | ConvertFrom-Json -Depth 64
			$scenario = @($plan.scenarios | Where-Object id -eq 'Q04')[0]
			$status = [ordered]@{ schema = 'ce77-live-probe-status-v1'; bootstrap = [ordered]@{ calls = 1; opaqueSecondInt = 0; pluginHostLastInitRecordArgument = 0; interpretation = 'none' } } | ConvertTo-Json -Compress
			$lines = @(
				[ordered]@{ tMs = 0; source = 'Runner'; kind = 'TargetReady'; message = '{"pid":4242}' }
				[ordered]@{ tMs = 2100; source = 'Driver'; kind = 'StepResult'; message = (@{ step = 1; id = 'open'; action = 'openTarget'; ok = $true; values = @(@{ type = 'boolean'; value = $true }) } | ConvertTo-Json -Compress -Depth 8) }
				[ordered]@{ tMs = 2300; source = 'Driver'; kind = 'StepResult'; message = (@{ step = 2; id = 'load'; action = 'loadPlugin'; ok = $true; values = @(@{ type = 'integer'; value = 0 }) } | ConvertTo-Json -Compress -Depth 8) }
				[ordered]@{ tMs = 2400; source = 'Driver'; kind = 'StepResult'; message = (@{ step = 3; id = 'status'; action = 'call'; ok = $true; values = @(@{ type = 'string'; value = $status }) } | ConvertTo-Json -Compress -Depth 8) }
			)
			$redacted = ConvertTo-RedactedSessionEvent -Raw @($lines | ForEach-Object { [pscustomobject] $_ }) -Map (Get-RedactionMap -Paths ([ordered]@{ '<workRoot>' = 'C:\lab\work' })) -OffsetMs 0
			$outcome = Resolve-QualificationOutcome -Scenario $scenario -Steps $redacted.steps -Answers ([ordered]@{})
			$lines = $redacted.entries
			$started = [datetimeoffset]::new(2026, 9, 24, 10, 15, 30, [timespan]::Zero)
			$nupkg = 'bfa967cc650ad859e5fd3164b53ae2081b6165fd5f2fa16af08b89621dfb70a4'
			$receiptId = Get-QualificationReceiptId -StartedUtc $started -QualificationId 'Q04' -NupkgSha256 $nupkg
			$eventText = ConvertTo-QualificationJson -InputObject ([ordered]@{ schema = 'cheatengine-qualification-events/v0'; receiptId = $receiptId; events = @($lines) })
			$receipt = ConvertTo-QualificationReceipt -Context ([ordered]@{
				QualificationId = 'Q04'; Level = 'C3'; ProfileId = 'ce-7.7.0.10621-x64-managed-hostfxr'; Operator = 'AriusII'; LoadRoute = 'LuaLoadPlugin'
				StartedUtc = $started; FinishedUtc = $started.AddSeconds(11); CreatedUtc = $started.AddSeconds(12); CeStartMs = 2000; Indicative = $false
				Repository = [ordered]@{ name = 'CheatEngineNet/CheatEngine.SDK'; treeHash = '40d7d7f741856c7e372e94bbd8532b06651eff5b'; commit = 'e77fb34c1f4e9c0e9d0e4a4a3b6c7d8e9f0a1b2c'; pullRequest = [ordered]@{ number = 86; headSha = 'e77fb34c1f4e9c0e9d0e4a4a3b6c7d8e9f0a1b2c' } }
				Runner = [ordered]@{ script = 'eng/qualification/Invoke-LocalQualification.ps1'; scriptSha256 = ('0' * 64); sourceRepository = 'CheatEngineNet/CheatEngine.SDK'; sourceCommit = 'e77fb34c1f4e9c0e9d0e4a4a3b6c7d8e9f0a1b2c'; mutex = 'Global\ce-lab' }
				Package = [ordered]@{ id = 'CheatEngine.SDK'; version = '2.0.0-alpha.0.12'; nupkgSha256 = $nupkg; contentHashSha512 = 'DOHZH8TYAm/L/qVXbpPlnoHh34r0GPB9erQYfBy0gyG84KP2iPVW6tFiuSX89K825zxz2u/S21JcFADMQVaUKw=='; source = 'CiArtifact'; ciRunUrl = 'https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/1234567890' }
				Host = [ordered]@{ ceExeName = 'cheatengine-x86_64.exe'; ceExeSha256 = '9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d'; ceFileVersion = '7.7.0.10621'; luaDllSha256 = 'c95dcdfa0f60f97b43d970d77fd1bb907af4de04b500a3c89a99600b20b35bd2'; runtimeconfigSha256 = '68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b'; autorunSha256 = ('1' * 64); sandboxCopy = $true; osVersion = 'Microsoft Windows NT 10.0.26200.0'; dotnetRuntimes = @('Microsoft.NETCore.App 10.0.12') }
				Bridge = [ordered]@{ sha256 = '889dc4c231d182f9b7baa9e29880555aad949f42023dde232fe327542c3c5387'; sourceFingerprint = '3342be23f88976d9209a24bc0d8b9db512482a24d8db90381a836ea4f5595a56:2871368515be4c6fd235e49e793d5557e7c50229fcc8fbfd903efd39f9b754a8' }
				Bundles = @([ordered]@{ name = 'LiveProbe'; manifestSha256 = ('2' * 64); files = @([ordered]@{ path = 'CheatEngine.SDK.LiveProbe.dll'; sha256 = ('3' * 64) }) })
				Target = [ordered]@{ kind = 'QualificationTarget'; arch = 'x64'; sha256 = ('4' * 64) }
				Registry = [ordered]@{ key = 'HKCU\Software\Cheat Engine'; exportBeforeSha256 = ('5' * 64); exportAfterSha256 = ('5' * 64); restored = $false; diff = [ordered]@{ added = 0; removed = 0; changed = 0; valueNames = @() } }
				Preconditions = @('LiveProbe loaded from the exact CI package.'); Operation = 'Read the raw integer.'; Expected = 'Recorded without interpretation.'
				Outcome = $outcome; EventLogSha256 = (Get-QualificationTextSha256 -Text $eventText); Redactions = @('<workRoot>', '<sandbox>')
			})
			[ordered]@{ receipt = (ConvertTo-QualificationJson -InputObject $receipt); events = $eventText } | ConvertTo-Json -Compress -Depth 4
			""");

		JsonElement receipt = QualificationDocuments.ParseJson(result.GetProperty("receipt").GetString()!);
		string eventText = result.GetProperty("events").GetString()!;
		JsonElement supportProfile = QualificationDocuments.LoadJson(QualificationDocuments.SupportProfilePath);

		IReadOnlyList<string> receiptErrors = ReceiptRules.Validate(receipt, supportProfile);
		IReadOnlyList<string> eventErrors = ReceiptRules.ValidateEventLog(QualificationDocuments.ParseJson(eventText),
			"R-20260924T101530Z-Q04-bfa967cc");

		Assert.True(receiptErrors.Count == 0, string.Join(Environment.NewLine, receiptErrors));
		Assert.True(eventErrors.Count == 0, string.Join(Environment.NewLine, eventErrors));
		Assert.Equal("R-20260924T101530Z-Q04-bfa967cc", receipt.GetProperty("receiptId").GetString());
		Assert.Equal("Passed", receipt.GetProperty("status").GetString());
		Assert.Equal("Functional", receipt.GetProperty("passKind").GetString());
		Assert.Equal(QualificationDocuments.Sha256OfNormalizedText(eventText),
			receipt.GetProperty("eventLog").GetProperty("sha256").GetString());
		Assert.Contains("observed status:bootstrap.opaqueSecondInt = 0", receipt.GetProperty("observed").GetString(),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Redaction_removes_user_paths_and_keeps_scenario_values()
	{
		const string Root = @"C:\Users\alice\AppData\Local\CheatEngineNet\qualification";
		string text = string.Join(" | ",
			Root + @"\sandbox\cheatengine-x86_64.exe",
			(Root + @"\runs\1\bundles\LiveProbe\CheatEngine.SDK.LiveProbe.dll").Replace('\\', '/'),
			"{\"pluginAssemblyLocation\":\"" + (Root + @"\runs\1\bundles\LiveProbe\CheatEngine.SDK.LiveProbe.dll").Replace(@"\", @"\\") + "\"}",
			@"C:\Users\alice\.nuget\packages\other.dll",
			"host LAB-PC-07 user alice",
			"\"opaqueSecondInt\":0,\"pid\":4242,\"imageBase\":\"0x7FF6A0B40000\"");

		JsonElement result = RunJson(ModuleImport + $$"""
			$map = Get-RedactionMap -Paths ([ordered]@{ '<sandbox>' = {{PowerShellProcess.Quote(Root + @"\sandbox")}}; '<workRoot>' = {{PowerShellProcess.Quote(Root)}}; '<bundle:LiveProbe>' = {{PowerShellProcess.Quote(Root + @"\runs\1\bundles\LiveProbe")}} }) -UserName 'alice' -MachineName 'LAB-PC-07'
			[ordered]@{ text = (ConvertTo-RedactedText -Text {{PowerShellProcess.Quote(text)}} -Map $map) } | ConvertTo-Json -Compress
			""");
		string redacted = result.GetProperty("text").GetString()!;

		Assert.Contains("<sandbox>\\cheatengine-x86_64.exe", redacted, StringComparison.Ordinal);
		Assert.Contains("<bundle:LiveProbe>/CheatEngine.SDK.LiveProbe.dll", redacted, StringComparison.Ordinal);
		Assert.Contains("\"pluginAssemblyLocation\":\"<bundle:LiveProbe>\\\\CheatEngine.SDK.LiveProbe.dll\"", redacted,
			StringComparison.Ordinal);
		Assert.Contains("<userProfile>", redacted, StringComparison.Ordinal);
		Assert.Contains("host <machine> user <user>", redacted, StringComparison.Ordinal);
		Assert.DoesNotContain("alice", redacted, StringComparison.OrdinalIgnoreCase);
		Assert.False(TextRules.ContainsAbsoluteLocalPath(redacted), redacted);
		Assert.Contains("\"opaqueSecondInt\":0,\"pid\":4242,\"imageBase\":\"0x7FF6A0B40000\"", redacted,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Registry_diff_reports_value_names_only()
	{
		const string Before = """
			Windows Registry Editor Version 5.00

			[HKEY_CURRENT_USER\Software\Cheat Engine]
			"First Time User"=dword:00000000
			"ceshare secret"="token-4f1d9a"
			"Long"=hex:01,02,\
			  03,04

			[HKEY_CURRENT_USER\Software\Cheat Engine\Plugins64]
			""";
		const string After = """
			Windows Registry Editor Version 5.00

			[HKEY_CURRENT_USER\Software\Cheat Engine]
			"First Time User"=dword:00000001
			"Long"=hex:01,02,\
			  03,04
			"Added"="private-value-77"

			[HKEY_CURRENT_USER\Software\Cheat Engine\Plugins64]
			"0"="C:\\sandbox\\plugin.dll"

			[HKEY_CURRENT_USER\Software\Cheat Engine\New Key]
			""";

		JsonElement diff = RunJson(ModuleImport + $$"""
			$before = ConvertFrom-RegistryExport -Text {{PowerShellProcess.Quote(Before)}}
			$after = ConvertFrom-RegistryExport -Text {{PowerShellProcess.Quote(After)}}
			Compare-RegistrySnapshot -Before $before -After $after -RootKey 'HKEY_CURRENT_USER\Software\Cheat Engine' | ConvertTo-Json -Compress
			""");
		string text = diff.GetRawText();

		Assert.Equal(3, diff.GetProperty("added").GetInt32());
		Assert.Equal(1, diff.GetProperty("removed").GetInt32());
		Assert.Equal(1, diff.GetProperty("changed").GetInt32());
		Assert.Equal(["Added", "First Time User", "New Key\\", "Plugins64\\0", "ceshare secret"],
			diff.GetProperty("valueNames").EnumerateArray().Select(static name => name.GetString()!).Order(StringComparer.Ordinal), StringComparer.Ordinal);
		foreach (string data in (string[]) ["token-4f1d9a", "private-value-77", "plugin.dll", "dword", "hex:"])
		{
			Assert.DoesNotContain(data, text, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Bundle_closure_check_rejects_a_missing_bridge_or_a_workspace_project_entry()
	{
		string bundle = Path.Combine(Path.GetTempPath(), "cesdk-bundle-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(bundle);
		try
		{
			File.WriteAllBytes(Path.Combine(bundle, "Plugin.dll"), [0x4D, 0x5A, 0x00, 0x01]);
			foreach (string assembly in (string[]) ["Abi", "Annotations", "Engine", "Hosting", "Lua", "Lua.Interop"])
			{
				File.WriteAllText(Path.Combine(bundle, "CheatEngine.SDK." + assembly + ".dll"), string.Empty);
			}

			File.WriteAllText(Path.Combine(bundle, "Plugin.deps.json"),
				"""{ "libraries": { "CheatEngine.SDK.Hosting/1.0.0": { "type": "project", "path": "D:/work/libs/Hosting" } } }""");
			File.WriteAllText(Path.Combine(bundle, "Plugin.runtimeconfig.json"), "{}");
			string packagedBridge = new('a', 64);

			JsonElement missingBridge = Closure(bundle, packagedBridge);
			File.WriteAllText(Path.Combine(bundle, "cheatengine-sdk-lua-bridge.dll"), "not the packaged bridge");
			JsonElement otherBridge = Closure(bundle, packagedBridge);

			string[] first = [.. missingBridge.EnumerateArray().Select(static problem => problem.GetString()!)];
			string[] second = [.. otherBridge.EnumerateArray().Select(static problem => problem.GetString()!)];
			Assert.Contains(first, static problem => problem.Contains("bridge", StringComparison.Ordinal) && problem.Contains("missing", StringComparison.Ordinal));
			Assert.Contains(first, static problem => problem.Contains("workspace project entry", StringComparison.Ordinal));
			Assert.Contains(first, static problem => problem.Contains("absolute path", StringComparison.Ordinal));
			Assert.Contains(first, static problem => problem.Contains("CESDK.CESDK.CEPluginInitialize", StringComparison.Ordinal));
			Assert.Contains(second, static problem => problem.Contains("differs from the package", StringComparison.Ordinal));
		}
		finally
		{
			Directory.Delete(bundle, true);
		}
	}

	[Fact]
	public void Only_Cheat_Engine_executables_count_as_another_instance()
	{
		// A false positive refuses the preflight and, after a session, turns a due HKCU restore into exit code 7.
		JsonElement result = RunJson(ModuleImport + """
			$names = 'cheatengine-x86_64', 'cheatengine-x86_64-SSE4-AVX2', 'cheatengine-i386', 'Cheat Engine', 'CHEATENGINE-X86_64',
				'CheatEngine.Client.Repository.Tests', 'CheatEngine.SDK.Tests', 'CheatEngine.SDK.QualificationTarget', 'cheatengine-x86_64-old', 'Tutorial-x86_64', 'gtutorial-i386'
			$result = [ordered]@{}
			foreach ($name in $names) { $result[$name] = Test-CheatEngineProcessName -Name $name }
			$result | ConvertTo-Json -Compress
			""");

		string[] cheatEngine = ["cheatengine-x86_64", "cheatengine-x86_64-SSE4-AVX2", "cheatengine-i386", "Cheat Engine", "CHEATENGINE-X86_64"];
		foreach (JsonProperty name in result.EnumerateObject())
		{
			Assert.True(name.Value.GetBoolean() == cheatEngine.Contains(name.Name, StringComparer.Ordinal), name.Name);
		}
	}

	[Fact]
	public void Content_hash_is_the_lock_file_value_the_restore_recorded_not_the_file_bytes_hash()
	{
		// For a signed package the restore's .nupkg.metadata contentHash (what lock files hold) differs from the
		// .nupkg.sha512 file (SHA-512 of the file bytes); the receipt records the former.
		string lockFileHash = new string('A', 86) + "==";
		string bytesHash = new string('Q', 86) + "==";
		string packages = Path.Combine(Path.GetTempPath(), "cesdk-packages-" + Guid.NewGuid().ToString("N"));
		string version = Path.Combine(packages, "cheatengine.sdk", "2.0.0-alpha.0.12");
		Directory.CreateDirectory(version);
		try
		{
			File.WriteAllText(Path.Combine(version, "cheatengine.sdk.2.0.0-alpha.0.12.nupkg.sha512"), bytesHash);
			string bytesOnly = ContentHash(packages);
			File.WriteAllText(Path.Combine(version, ".nupkg.metadata"),
				$$"""{ "version": 2, "contentHash": "{{lockFileHash}}", "source": "qualified-package" }""");
			string withMetadata = ContentHash(packages);
			Directory.Delete(version, true);
			string absent = ContentHash(packages);

			Assert.Equal(bytesHash, bytesOnly);
			Assert.Equal(lockFileHash, withMetadata);
			Assert.Equal("absent", absent);
		}
		finally
		{
			Directory.Delete(packages, true);
		}
	}

	[Fact]
	public void Every_Checkpoint_B_scenario_exists_and_cites_harness_commands_that_exist()
	{
		JsonElement plan = QualificationDocuments.LoadJson(Scenarios);
		QualificationMatrix matrix = QualificationMatrix.Read(QualificationDocuments.LoadJson(QualificationDocuments.MatrixPath));
		HashSet<string> commands = HarnessLuaFunctions();
		List<string> ids = [];
		List<string> problems = [];
		foreach (JsonElement scenario in plan.GetProperty("scenarios").EnumerateArray())
		{
			string id = scenario.GetProperty("id").GetString()!;
			ids.Add(id);
			string level = scenario.GetProperty("level").GetString()!;
			if (matrix.Find(id) is not { } row || !row.Levels.ContainsKey(level))
			{
				problems.Add($"{id}: the matrix has no {level} cell for it.");
				continue;
			}

			string support = scenario.GetProperty("support").GetString()!;
			if (support is "Manual" or "NotApplicable")
			{
				if (!scenario.TryGetProperty("reason", out JsonElement reason) || string.IsNullOrWhiteSpace(reason.GetString()))
				{
					problems.Add($"{id}: a {support} scenario states its reason.");
				}

				if (string.Equals(support, "NotApplicable", StringComparison.Ordinal) &&
					!string.Equals(row.Levels[level].Status, "NotApplicable", StringComparison.Ordinal))
				{
					problems.Add($"{id}: scenarios.json says NotApplicable but the matrix cell is {row.Levels[level].Status}.");
				}

				continue;
			}

			problems.AddRange(CheckSteps(id, scenario, commands));
		}

		Assert.Equal(CheckpointB.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Driver_templates_are_valid_Lua()
	{
		JsonElement drivers = RunJson(ModuleImport + $$"""
			$template = Get-Content -LiteralPath {{PowerShellProcess.Quote(QualificationDocuments.Absolute(DriverTemplate))}} -Raw
			$plan = Get-Content -LiteralPath {{PowerShellProcess.Quote(QualificationDocuments.Absolute(Scenarios))}} -Raw | ConvertFrom-Json -Depth 64
			$result = foreach ($scenario in $plan.scenarios | Where-Object { $_.PSObject.Properties.Name -contains 'steps' }) {
				$values = [ordered]@{
					RUN_ID = '20260924T101530Z'; EVENTS_PATH = 'W:/run/events.jsonl'; PROGRESS_PATH = 'W:/run/progress.txt'
					HANDSHAKE_DIR = 'W:/run/handshake'; DONE_PATH = 'W:/run/done.txt'; STEPS = @($scenario.steps)
					BUNDLES = [ordered]@{ LiveProbe = 'W:/bundles/LiveProbe/CheatEngine.SDK.LiveProbe.dll'; CoexistenceSharedA = 'W:/a.dll' }
					TARGETS = [ordered]@{ x64 = 4242 }
				}
				[ordered]@{ id = $scenario.id; text = (Expand-QualificationDriver -Template $template -Values $values) }
			}
			ConvertTo-Json -InputObject @($result) -Compress -Depth 4
			""");

		List<string> problems = [];
		foreach (JsonElement driver in drivers.EnumerateArray())
		{
			string id = driver.GetProperty("id").GetString()!;
			string text = driver.GetProperty("text").GetString()!;
			if (Regex.IsMatch(text, "__[A-Z][A-Z_]*__", RegexOptions.None, RegexTimeout))
			{
				problems.Add($"{id}: a placeholder was not substituted.");
			}

			string? error = LuaSyntaxChecker.Compile(text, "zz_cesdk_qualification_" + id);
			if (error is not null)
			{
				problems.Add($"{id}: {error}");
			}
		}

		Assert.True(drivers.GetArrayLength() >= 14, "Too few drivers were generated.");
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		Assert.Null(LuaSyntaxChecker.Compile(QualificationDocuments.ReadNormalizedText(DriverTemplate)
			.Replace("__STEPS__", "{}", StringComparison.Ordinal)
			.Replace("__BUNDLES__", "{}", StringComparison.Ordinal)
			.Replace("__TARGETS__", "{}", StringComparison.Ordinal)
			.Replace("__RUN_ID__", "'r'", StringComparison.Ordinal)
			.Replace("__EVENTS_PATH__", "'e'", StringComparison.Ordinal)
			.Replace("__PROGRESS_PATH__", "'p'", StringComparison.Ordinal)
			.Replace("__HANDSHAKE_DIR__", "'h'", StringComparison.Ordinal)
			.Replace("__DONE_PATH__", "'d'", StringComparison.Ordinal), "template"));
		Assert.NotNull(LuaSyntaxChecker.Compile("local x = = 1", "broken"));
	}

	private static IEnumerable<string> CheckSteps(string id, JsonElement scenario, HashSet<string> commands)
	{
		HashSet<string> stepIds = new(StringComparer.Ordinal);
		foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
		{
			string stepId = step.GetProperty("id").GetString()!;
			if (!stepIds.Add(stepId))
			{
				yield return $"{id}: step id '{stepId}' repeats.";
			}

			string kind = step.GetProperty("kind").GetString()!;
			if (string.Equals(kind, "Operator", StringComparison.Ordinal))
			{
				if (string.IsNullOrWhiteSpace(step.GetProperty("instruction").GetString()))
				{
					yield return $"{id}.{stepId}: an operator step needs an instruction.";
				}

				continue;
			}

			string action = step.GetProperty("action").GetString()!;
			if (string.Equals(action, "call", StringComparison.Ordinal) &&
				!commands.Contains(step.GetProperty("function").GetString()!))
			{
				yield return $"{id}.{stepId}: no harness declares the Lua function '{step.GetProperty("function").GetString()}'.";
			}
			else if (action is not ("call" or "loadPlugin" or "openTarget" or "wait"))
			{
				yield return $"{id}.{stepId}: unknown action '{action}'.";
			}
		}

		foreach (JsonElement check in scenario.GetProperty("passRule").GetProperty("checks").EnumerateArray())
		{
			if (!stepIds.Contains(check.GetProperty("step").GetString()!))
			{
				yield return $"{id}: a pass-rule check names the unknown step '{check.GetProperty("step").GetString()}'.";
			}
		}
	}

	private static HashSet<string> HarnessLuaFunctions()
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (string directory in (string[])
				 ["tests/CheatEngine.SDK.LiveProbe", "tests/CheatEngine.SDK.LivePlugin", "tests/CheatEngine.SDK.LivePlugin.Coexistence"])
		{
			foreach (string file in Directory.EnumerateFiles(QualificationDocuments.Absolute(directory), "*.cs",
						 SearchOption.AllDirectories))
			{
				string source = File.ReadAllText(file);
				foreach (Match match in Regex.Matches(source,
							 "\\[LuaFunction\\(\"(?<name>[A-Za-z_][A-Za-z0-9_]*)\"\\)\\]|TryRegister\\([^,]+,\\s*\"(?<name>[A-Za-z_][A-Za-z0-9_]*)\"u8\\)",
							 RegexOptions.None, RegexTimeout))
				{
					names.Add(match.Groups["name"].Value);
				}
			}
		}

		return names;
	}

	private static JsonElement Closure(string bundle, string packagedBridge)
	{
		return RunJson(ModuleImport +
					   $"ConvertTo-Json -Compress -InputObject @(Test-QualificationBundleClosure -BundleDirectory {PowerShellProcess.Quote(bundle)} -PluginFileName 'Plugin.dll' -PackagedBridgeSha256 '{packagedBridge}')");
	}

	private static string ContentHash(string packages)
	{
		JsonElement result = RunJson(ModuleImport +
									 $"$hash = Get-RestoredPackageContentHash -PackagesDirectory {PowerShellProcess.Quote(packages)} -Id 'CheatEngine.SDK' -Version '2.0.0-alpha.0.12'; " +
									 "ConvertTo-Json -Compress -InputObject $(if ($null -eq $hash) { 'absent' } else { $hash })");
		return result.GetString()!;
	}

	private static JsonElement RunJson(string script)
	{
		PowerShellProcess.Result result = PowerShellProcess.RunCommand(script, null, TestContext.Current.CancellationToken);
		Assert.True(result.ExitCode == 0, result.Transcript);
		string json = result.Output.Trim();
		Assert.False(string.IsNullOrEmpty(json), result.Transcript);
		return QualificationDocuments.ParseJson(json);
	}

	// Scope-aware analysis of the runner: inside each function and at script level, every variable derived from
	// $CheatEnginePath is tracked, and no write (cmdlet, .NET file API, robocopy destination) may target one of them.
	private static string WriteTargetAnalysis(string scriptPath)
	{
		return $$"""
			$errors = $null
			$ast = [System.Management.Automation.Language.Parser]::ParseFile({{PowerShellProcess.Quote(scriptPath)}}, [ref] $null, [ref] $errors)
			function Get-Names($node) { if ($null -eq $node) { return @() }; @($node.FindAll({ $args[0] -is [System.Management.Automation.Language.VariableExpressionAst] }, $true) | ForEach-Object { $_.VariablePath.UserPath }) }
			$writeCommands = 'Set-Content','Add-Content','Out-File','New-Item','Remove-Item','Rename-Item','Move-Item','Clear-Content','Set-Item','Expand-Archive'
			$violations = [System.Collections.Generic.List[string]]::new()
			$reads = 0
			$scopes = [ordered]@{}
			foreach ($node in $ast.FindAll({ $true }, $true)) {
				$key = 'script'
				for ($parent = $node.Parent; $null -ne $parent; $parent = $parent.Parent) {
					if ($parent -is [System.Management.Automation.Language.FunctionDefinitionAst]) { $key = "$($parent.Name)@$($parent.Extent.StartOffset)"; break }
				}
				if (-not $scopes.Contains($key)) { $scopes[$key] = [System.Collections.Generic.List[object]]::new() }
				$scopes[$key].Add($node)
			}
			foreach ($nodes in $scopes.Values) {
				$aliases = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
				[void] $aliases.Add('CheatEnginePath')
				do {
					$count = $aliases.Count
					foreach ($assignment in $nodes | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] }) {
						if ((Get-Names $assignment.Right | Where-Object { $aliases.Contains($_) }) -and $assignment.Left -is [System.Management.Automation.Language.VariableExpressionAst]) { [void] $aliases.Add($assignment.Left.VariablePath.UserPath) }
					}
					foreach ($loop in $nodes | Where-Object { $_ -is [System.Management.Automation.Language.ForEachStatementAst] }) {
						if (Get-Names $loop.Condition | Where-Object { $aliases.Contains($_) }) { [void] $aliases.Add($loop.Variable.VariablePath.UserPath) }
					}
				} while ($aliases.Count -gt $count)
				$reads += @($nodes | Where-Object { $_ -is [System.Management.Automation.Language.VariableExpressionAst] -and $_.VariablePath.UserPath -eq 'CheatEnginePath' }).Count
				foreach ($command in $nodes | Where-Object { $_ -is [System.Management.Automation.Language.CommandAst] }) {
					$name = $command.GetCommandName()
					$elements = @($command.CommandElements)
					if ($name -in $writeCommands) {
						if (Get-Names $command | Where-Object { $aliases.Contains($_) }) { $violations.Add("line $($command.Extent.StartLineNumber): $name targets the installation") }
					}
					elseif ($name -eq 'Copy-Item') {
						for ($i = 0; $i + 1 -lt $elements.Count; $i++) {
							if ($elements[$i] -is [System.Management.Automation.Language.CommandParameterAst] -and $elements[$i].ParameterName -eq 'Destination' -and (Get-Names $elements[$i + 1] | Where-Object { $aliases.Contains($_) })) { $violations.Add("line $($command.Extent.StartLineNumber): Copy-Item destination is the installation") }
						}
					}
					elseif ($name -eq 'Invoke-Native' -and $command.Extent.Text -match 'robocopy') {
						$items = @(@($command.FindAll({ $args[0] -is [System.Management.Automation.Language.ArrayLiteralAst] }, $true))[0].Elements)
						for ($i = 1; $i -lt $items.Count; $i++) { if (Get-Names $items[$i] | Where-Object { $aliases.Contains($_) }) { $violations.Add("line $($command.Extent.StartLineNumber): robocopy destination is the installation") } }
					}
				}
				foreach ($call in $nodes | Where-Object { $_ -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $_.Static }) {
					$type = $call.Expression.Extent.Text
					$member = $call.Member.Extent.Text
					if ($type -match 'IO\.(File|Directory)\]|ZipFileExtensions' -and $member -match '^(Write|Append|Delete|Move|Copy|Create|Replace|Set|Extract)') {
						$arguments = @($call.Arguments)
						$target = if ($member -match '^(Move|Copy|Extract)') { $arguments[1] } else { $arguments[0] }
						if (Get-Names $target | Where-Object { $aliases.Contains($_) }) { $violations.Add("line $($call.Extent.StartLineNumber): $type::$member writes into the installation") }
					}
				}
			}
			[ordered]@{ reads = $reads; violations = @($violations) } | ConvertTo-Json -Compress
			""";
	}
}
