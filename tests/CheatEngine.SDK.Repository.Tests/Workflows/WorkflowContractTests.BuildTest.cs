using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     The build-test and aot jobs of ci.yml (contract 1.8): one build, Pack before Test in Release, the exact package
///     handed to the packaging tests, the Debug trait filter, dumps, the module inventory, binary logs and artifacts.
/// </summary>
public sealed partial class WorkflowContractTests
{
	private const string BuildTestJob = "build-test";

	/// <summary>The MTP options of the Test step and the Microsoft.Testing.Extensions package that provides each one.</summary>
	private static readonly Dictionary<string, string> s_testOptionExtensions = new(StringComparer.Ordinal)
	{
		["--report-trx"] = "Microsoft.Testing.Extensions.TrxReport",
		["--report-gh"] = "Microsoft.Testing.Extensions.GitHubActionsReport",
		["--hangdump"] = "Microsoft.Testing.Extensions.HangDump",
		["--crashdump"] = "Microsoft.Testing.Extensions.CrashDump",
		["--coverage"] = "Microsoft.Testing.Extensions.CodeCoverage"
	};

	[Fact]
	public void Build_test_runs_both_configurations_without_fail_fast()
	{
		WorkflowJob job = Pipeline().Job(BuildTestJob);
		YamlMappingNode strategy = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(job.Node, "strategy"));
		Assert.Equal("false", WorkflowFile.Scalar(strategy, "fail-fast"));
		YamlMappingNode matrix = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(strategy, "matrix"));
		Assert.Equal(["Debug", "Release"], WorkflowFile.ScalarValues(Assert.IsType<YamlSequenceNode>(WorkflowFile.Sequence(matrix, "configuration"))));
		Assert.Equal(["native"], job.Needs());

		// One build of the whole solution per leg, logged for failure analysis; every later step reuses it.
		string build = WorkflowFile.Scalar(job.Step("Build"), "run") ?? "";
		Assert.Contains("dotnet build CheatEngine.SDK.slnx -c $env:CONFIGURATION --no-restore", build, StringComparison.Ordinal);
		Assert.Contains("\"-bl:artifacts/logs/build-test-$env:CONFIGURATION.binlog\"", build, StringComparison.Ordinal);
	}

	[Fact]
	public void Release_leg_packs_before_testing_and_exports_the_exact_nupkg()
	{
		WorkflowJob job = Pipeline().Job(BuildTestJob);
		int build = job.StepIndex("Build");
		int pack = job.StepIndex("Pack");
		int test = job.StepIndex("Test");
		int verify = job.StepIndex("Verify the tested package is unchanged");
		int upload = job.StepIndex("Upload package");
		Assert.True(build >= 0 && build < pack && pack < test && test < verify && verify < upload,
			"build-test must run Build, Pack, Test, then verify and upload the tested package, in that order.");

		YamlMappingNode packStep = job.Steps[pack];
		Assert.Equal("pack", WorkflowFile.Scalar(packStep, "id"));
		Assert.Equal("matrix.configuration == 'Release'", WorkflowFile.Scalar(packStep, "if"));
		Assert.Equal("${{ inputs.package-version }}", WorkflowJob.Env(packStep, "PACKAGE_VERSION"));
		string packRun = WorkflowFile.Scalar(packStep, "run") ?? "";
		Assert.Contains("dotnet pack src/CheatEngine.SDK -c Release --no-restore -o artifacts/nuget -bl:artifacts/logs/pack-Release.binlog",
			packRun, StringComparison.Ordinal);
		Assert.Contains("./eng/ci/Test-SdkPackage.ps1 -PackageDirectory artifacts/nuget -PackageVersion $env:PACKAGE_VERSION", packRun,
			StringComparison.Ordinal);

		// The packaging tests consume the packed file itself, in the Release leg only, and log its SHA-256.
		YamlMappingNode testStep = job.Steps[test];
		Assert.Equal("${{ matrix.configuration == 'Release' && steps.pack.outputs.nupkg || '' }}",
			WorkflowJob.Env(testStep, WorkflowContract.ExactPackageVariable));
		Assert.Equal("${{ steps.pack.outputs.sha256 }}", WorkflowJob.Env(testStep, "PACKED_NUPKG_SHA256"));
		string testRun = WorkflowFile.Scalar(testStep, "run") ?? "";
		Assert.Contains("$consumed -ne $env:PACKED_NUPKG_SHA256", testRun, StringComparison.Ordinal);
		Assert.Contains("Out-File -FilePath $env:GITHUB_STEP_SUMMARY", testRun, StringComparison.Ordinal);

		Assert.Equal("artifacts/nuget/*.nupkg", WorkflowJob.With(job.Steps[upload], "path"));

		// The script asserts one package, its exact name when a version is required, the SBOM and the CI-built bridge.
		string script = ReadRepositoryText("eng/ci/Test-SdkPackage.ps1");
		Assert.Contains("$sbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'", script, StringComparison.Ordinal);
		Assert.Contains("\"nupkg=$($package.FullName)\"", script, StringComparison.Ordinal);
		Assert.Contains("\"sha256=$sha256\"", script, StringComparison.Ordinal);
		Assert.Contains("$package.Name -cne \"$packageId.$PackageVersion.nupkg\"", script, StringComparison.Ordinal);
	}

	[Fact]
	public void Debug_leg_excludes_packaging_tests_by_trait_never_by_skip()
	{
		string run = WorkflowFile.Scalar(Pipeline().Job(BuildTestJob).Step("Test"), "run") ?? "";

		// A skipped test fails the run in both legs; packaging is excluded by an xUnit v3 trait filter instead.
		int options = run.IndexOf("$options = @(", StringComparison.Ordinal);
		int debug = run.IndexOf("if ($env:CONFIGURATION -eq 'Debug') {", StringComparison.Ordinal);
		int release = run.IndexOf("else {", debug + 1, StringComparison.Ordinal);
		int failSkips = run.IndexOf("'--fail-skips', 'on'", StringComparison.Ordinal);
		int filter = run.IndexOf($"'--filter-not-trait', '{WorkflowContract.PackagingTrait}'", StringComparison.Ordinal);
		Assert.True(options >= 0 && options < failSkips && failSkips < debug,
			"--fail-skips on must be a common option of both legs.");
		Assert.True(debug < filter && filter < release, "The packaging trait filter belongs to the Debug leg only.");

		// No other way to hide a test: no second filter, no ignored exit code, no retries in the required run.
		Assert.Equal(1, Occurrences(run, "--filter"));
		foreach (string forbidden in new[] { "--ignore-exit-code", "--retry-failed-tests", "TESTINGPLATFORM_EXITCODE_IGNORE" })
		{
			Assert.DoesNotContain(forbidden, run, StringComparison.Ordinal);
		}

		// The Debug leg keeps the managed ABI comparison against the native fixture mandatory (audit A04-04).
		Assert.Contains("$env:CE77_NATIVE_ABI_REQUIRED = 'true'", run, StringComparison.Ordinal);
	}

	[Fact]
	public void Test_step_runs_every_module_once_with_the_contract_options()
	{
		string run = WorkflowFile.Scalar(Pipeline().Job(BuildTestJob).Step("Test"), "run") ?? "";

		Assert.Equal(1, Occurrences(run, "dotnet test"));
		Assert.Contains("dotnet test @options", run, StringComparison.Ordinal);
		foreach (string option in new[]
				 {
					 "'--solution', 'CheatEngine.SDK.slnx'", "'--no-build'", "'--results-directory', $env:RESULTS", "'--report-trx'",
					 "'--report-gh', '--report-gh-groups', 'off'", "'--hangdump', '--hangdump-timeout'", "'--crashdump'",
					 "'--coverage', '--coverage-output-format', 'xml'"
				 })
		{
			Assert.Contains(option, run, StringComparison.Ordinal);
		}

		// SDK 10 passes MTP options directly: a '--' separator would hand them to the wrong parser.
		Assert.DoesNotContain("'--',", run, StringComparison.Ordinal);
	}

	[Fact]
	public void Every_test_module_references_the_extensions_the_test_step_uses()
	{
		// dotnet test passes every option to every module; a module without the extension fails with exit code 5.
		string run = WorkflowFile.Scalar(Pipeline().Job(BuildTestJob).Step("Test"), "run") ?? "";
		XDocument props = XDocument.Parse(ReadRepositoryText("eng/Tests.props"));
		HashSet<string> referenced = new(StringComparer.Ordinal);
		foreach (XElement group in props.Descendants("ItemGroup"))
		{
			if (!((string?) group.Attribute("Condition") ?? "").Contains("$(MSBuildProjectName.EndsWith('.Tests'))", StringComparison.Ordinal))
			{
				continue;
			}

			foreach (XElement reference in group.Elements("PackageReference"))
			{
				referenced.Add((string?) reference.Attribute("Include") ?? "");
			}
		}

		foreach ((string option, string package) in s_testOptionExtensions)
		{
			Assert.Contains($"'{option}'", run, StringComparison.Ordinal);
			Assert.True(referenced.Contains(package),
				$"The Test step passes {option}; every *.Tests project needs {package} through eng/Tests.props.");
		}
	}

	[Fact]
	public void Hang_dump_timeout_is_well_below_the_build_test_job_timeout()
	{
		WorkflowJob job = Pipeline().Job(BuildTestJob);
		string run = WorkflowFile.Scalar(job.Step("Test"), "run") ?? "";
		Match hang = HangDumpTimeout().Match(run);
		Assert.True(hang.Success, "The Test step must pass '--hangdump-timeout', '<minutes>m'.");
		int hangMinutes = int.Parse(hang.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
		int jobMinutes = int.Parse(WorkflowFile.Scalar(job.Node, "timeout-minutes") ?? "0", NumberStyles.None, CultureInfo.InvariantCulture);

		// The dump must be written, uploaded and the job reported well before the runner kills it.
		Assert.True(hangMinutes > 0 && hangMinutes * 2 <= jobMinutes,
			$"--hangdump-timeout {hangMinutes}m must be at most half of build-test's timeout-minutes ({jobMinutes}).");
	}

	[Fact]
	public void Test_module_inventory_runs_in_both_legs()
	{
		WorkflowJob job = Pipeline().Job(BuildTestJob);
		YamlMappingNode inventory = job.Step("Check test module inventory");
		Assert.True(job.StepIndex("Test") < job.StepIndex("Check test module inventory"));
		Assert.Equal("${{ !cancelled() && steps.test.outcome != 'skipped' }}", WorkflowFile.Scalar(inventory, "if"));
		string run = WorkflowFile.Scalar(inventory, "run") ?? "";
		Assert.Contains("./eng/ci/Test-TestModuleInventory.ps1 @options", run, StringComparison.Ordinal);
		Assert.Contains("$options.RequireCoverage = $true", run, StringComparison.Ordinal);

		// The expected set is every tests/**/*.Tests.csproj git knows, never a hard-coded list.
		string script = ReadRepositoryText("eng/ci/Test-TestModuleInventory.ps1");
		Assert.Contains("'tests/*.Tests.csproj'", script, StringComparison.Ordinal);
		Assert.Contains("'^(?<module>.+)_(?<tfm>net\\d+\\.\\d+)_(?<arch>x64|x86|arm64)\\.trx$'", script, StringComparison.Ordinal);
		Assert.Contains("executed no test", script, StringComparison.Ordinal);
	}

	[Fact]
	public void Every_uploaded_artifact_name_is_reserved()
	{
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			HashSet<string> names = new(StringComparer.Ordinal);
			foreach (WorkflowJob job in workflow.Jobs())
			{
				foreach (YamlMappingNode step in job.StepsUsing("actions/upload-artifact@"))
				{
					string template = WorkflowJob.With(step, "name") ?? "";
					string retention = WorkflowJob.With(step, "retention-days") ?? "";
					foreach (string name in ExpandConfiguration(template))
					{
						Assert.True(names.Add(name), $"{workflow.FileName} uploads '{name}' twice; artifact names are unique per run.");
					}

					string? expectedRetention = ExpectedRetention(template);
					Assert.True(expectedRetention is not null || IsReservedWithoutRetention(template),
						$"{job.Location} uploads '{template}', which is not a reserved artifact name (shared contract 1.9).");
					if (expectedRetention is not null)
					{
						Assert.True(string.Equals(expectedRetention, retention, StringComparison.Ordinal),
							$"{job.Location} keeps '{template}' {retention} days; the contract says {expectedRetention}.");
					}
				}
			}
		}
	}

	[Fact]
	public void Binlogs_are_uploaded_only_on_failure_and_never_from_sonar_or_release()
	{
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			bool mayLogBuilds = workflow.FileName is not (WorkflowContract.Sonar or "release.yml");
			foreach (WorkflowJob job in workflow.Jobs())
			{
				// A binary log records the environment the build saw: never where a token or a signing step lives.
				Assert.True(mayLogBuilds || !BinaryLogSwitch().IsMatch(job.RunText()),
					$"{job.Location} writes a binary log; sonar.yml and release.yml never do.");
				foreach (YamlMappingNode step in job.StepsUsing("actions/upload-artifact@"))
				{
					string name = WorkflowJob.With(step, "name") ?? "";
					if (name.StartsWith("binlogs-", StringComparison.Ordinal) || name.StartsWith("test-dumps-", StringComparison.Ordinal))
					{
						Assert.True(mayLogBuilds, $"{job.Location} uploads '{name}'; sonar.yml and release.yml never do.");
						Assert.True(string.Equals(WorkflowFile.Scalar(step, "if"), "failure()", StringComparison.Ordinal),
							$"{job.Location} uploads '{name}' outside 'if: failure()'.");
					}
				}
			}
		}

		// The jobs that build, pack or publish keep their logs for a failed run.
		WorkflowFile pipeline = Pipeline();
		Assert.Single(pipeline.Job(BuildTestJob).StepsUsing("actions/upload-artifact@"),
			static step => string.Equals(WorkflowJob.With(step, "name"), "binlogs-build-test-${{ matrix.configuration }}", StringComparison.Ordinal));
		Assert.Single(pipeline.Job("aot").StepsUsing("actions/upload-artifact@"),
			static step => string.Equals(WorkflowJob.With(step, "name"), "binlogs-aot", StringComparison.Ordinal));
		Assert.Equal(3, BinaryLogSwitch().Count(pipeline.Job("aot").RunText()));
	}

	[Fact]
	public void Jobs_that_pack_or_test_fetch_full_history()
	{
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			foreach (WorkflowJob job in workflow.Jobs())
			{
				string run = job.RunText();
				bool packs = VersionedPack().IsMatch(run) || run.Contains("dotnet-sonarscanner", StringComparison.Ordinal);
				bool builds = VersionedBuild().IsMatch(run);
				if (!packs && !builds)
				{
					continue;
				}

				// MinVer computes the version from tags and history; a shallow clone packs 0.0.0-alpha.0.
				YamlMappingNode checkout = Assert.Single(job.StepsUsing("actions/checkout@"));
				bool fullHistory = string.Equals(WorkflowJob.With(checkout, "fetch-depth"), "0", StringComparison.Ordinal);
				bool skipsVersioning = !packs && run.Contains("MinVerSkip=true", StringComparison.Ordinal);
				Assert.True(fullHistory || skipsVersioning,
					$"{job.Location} builds, packs, tests or analyses: check out with fetch-depth: 0 (MinVer).");
			}
		}
	}

	[Fact]
	public void Aot_job_publishes_the_native_aot_probes()
	{
		WorkflowJob aot = Pipeline().Job("aot");
		Assert.Equal(["native"], aot.Needs());

		// The three probes restore locked through the composite action; their locks carry the win-x64 ILCompiler.
		string[] probes =
		[
			"tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj",
			"tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj",
			"tests/CheatEngine.SDK.NativeAotLoaderHarness/CheatEngine.SDK.NativeAotLoaderHarness.csproj"
		];
		YamlMappingNode setup = Assert.Single(aot.StepsUsing(WorkflowContract.SetupAction));
		Assert.Equal(probes, RestoreTargets(setup), StringComparer.Ordinal);

		// Publication and inspection only: a NativeAOT publish is never presented as a Cheat Engine load (audit A20-07).
		string run = aot.RunText();
		Assert.Contains("dotnet publish tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj -c Release --no-restore", run,
			StringComparison.Ordinal);
		Assert.Contains("$libraryProject = 'tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj'",
			run, StringComparison.Ordinal);
		Assert.Contains("dotnet publish $libraryProject -c Release --no-restore", run, StringComparison.Ordinal);
		Assert.Contains("& $harness --analyze $library", run, StringComparison.Ordinal);
		Assert.Contains("lua-protection-bridge", WorkflowJob.With(Assert.Single(aot.StepsUsing("actions/download-artifact@")), "name"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Live_probe_is_compiled_by_the_ci_solution_build()
	{
		WorkflowJob job = Pipeline().Job(BuildTestJob);
		bool inSolution = SolutionProjects().Contains(WorkflowContract.LiveProbeProject);
		int explicitStep = job.StepIndex("Compile live probe");
		bool restoredExplicitly = RestoreTargets(Assert.Single(job.StepsUsing(WorkflowContract.SetupAction)))
			.Contains(WorkflowContract.LiveProbeProject);

		// C0 only (PR-CQ-60): compiled once per run, by the solution build as soon as the probe is in the solution.
		if (inSolution)
		{
			Assert.True(explicitStep < 0 && !restoredExplicitly,
				$"{WorkflowContract.LiveProbeProject} is in CheatEngine.SDK.slnx, so the solution build compiles it: delete the " +
				"'Compile live probe' step of build-test and its line in the step's composite restore list.");
		}
		else
		{
			Assert.True(explicitStep > job.StepIndex("Build") && restoredExplicitly,
				"Until the live probe joins CheatEngine.SDK.slnx, build-test restores it and compiles it in a 'Compile live probe' step.");
			YamlMappingNode step = job.Steps[explicitStep];
			Assert.Equal("matrix.configuration == 'Release'", WorkflowFile.Scalar(step, "if"));
			Assert.Contains($"dotnet build {WorkflowContract.LiveProbeProject} -c Release --no-restore",
				WorkflowFile.Scalar(step, "run"), StringComparison.Ordinal);
		}

		// Never loaded, run or shipped by CI: nothing uploads its output.
		foreach (WorkflowJob pipelineJob in Pipeline().Jobs())
		{
			foreach (YamlMappingNode upload in pipelineJob.StepsUsing("actions/upload-artifact@"))
			{
				Assert.DoesNotContain("LiveProbe", WorkflowJob.With(upload, "path") ?? "", StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	/// <summary>The newline-separated <c>restore</c> input of a composite setup step.</summary>
	private static List<string> RestoreTargets(YamlMappingNode setupStep)
	{
		List<string> targets = [];
		foreach (string line in (WorkflowJob.With(setupStep, "restore") ?? "").Split('\n'))
		{
			if (line.Trim().Length > 0)
			{
				targets.Add(line.Trim());
			}
		}

		return targets;
	}

	/// <summary>The project paths CheatEngine.SDK.slnx lists.</summary>
	private static HashSet<string> SolutionProjects()
	{
		HashSet<string> projects = new(StringComparer.Ordinal);
		foreach (XElement project in XDocument.Load(RepositoryRoot.SolutionPath).Descendants("Project"))
		{
			projects.Add(((string?) project.Attribute("Path") ?? "").Replace('\\', '/'));
		}

		return projects;
	}

	private static int Occurrences(string text, string value)
	{
		int count = 0;
		for (int index = text.IndexOf(value, StringComparison.Ordinal);
			 index >= 0;
			 index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	private static IEnumerable<string> ExpandConfiguration(string template)
	{
		const string placeholder = "${{ matrix.configuration }}";
		if (!template.Contains(placeholder, StringComparison.Ordinal))
		{
			return [template];
		}

		return
		[
			template.Replace(placeholder, "Debug", StringComparison.Ordinal),
			template.Replace(placeholder, "Release", StringComparison.Ordinal)
		];
	}

	/// <summary>The contract retention of a reserved name, or null when the name is not reserved with one.</summary>
	private static string? ExpectedRetention(string template)
	{
		string key = template.Replace("${{ matrix.configuration }}", "{configuration}", StringComparison.Ordinal);
		if (WorkflowContract.ReservedArtifacts.TryGetValue(key, out string? retention))
		{
			return retention;
		}

		return BinlogName().IsMatch(template) ? WorkflowContract.BinlogRetention : null;
	}

	private static bool IsReservedWithoutRetention(string template)
	{
		return WorkflowContract.ReservedArtifacts.TryGetValue(template, out string? retention) && retention is null;
	}

	[GeneratedRegex(@"'--hangdump-timeout',\s*'(?<minutes>\d+)m'", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex HangDumpTimeout();

	[GeneratedRegex(@"\bdotnet\s+pack\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex VersionedPack();

	[GeneratedRegex(@"\bdotnet\s+(?:build|test|publish)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex VersionedBuild();

	[GeneratedRegex(@"(?:^|\s|"")[-/]bl(?::|\s|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex BinaryLogSwitch();

	[GeneratedRegex(@"^binlogs-[a-z0-9-]+?(?:-\$\{\{ matrix\.configuration \}\})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex BinlogName();
}
