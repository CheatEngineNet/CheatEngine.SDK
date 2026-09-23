namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     The frozen CI interface (shared contract section 1): job ids and names, runners, timeouts, the Sonar expectation,
///     the reserved artifact names. Changing a value here is a contract change, reviewed with the workflow it describes.
/// </summary>
internal static class WorkflowContract
{
	/// <summary>The reusable pipeline.</summary>
	public const string Pipeline = "ci.yml";

	/// <summary>The reusable Sonar workflow.</summary>
	public const string Sonar = "sonar.yml";

	/// <summary>The composite action every dotnet job uses.</summary>
	public const string SetupAction = "./.github/actions/setup-dotnet";

	/// <summary>The composite action file.</summary>
	public const string SetupActionFile = ".github/actions/setup-dotnet/action.yml";

	/// <summary>The caller job id that, with <see cref="CallerJobName" />, produces the required check "CI / Gate".</summary>
	public const string CallerJobId = "ci";

	/// <summary>The caller job name.</summary>
	public const string CallerJobName = "CI";

	/// <summary>The gate job id.</summary>
	public const string GateJobId = "gate";

	/// <summary>The gate job name.</summary>
	public const string GateJobName = "Gate";

	/// <summary>SONAR_EXPECTED (contract 1.7), whitespace-normalized.</summary>
	public const string SonarExpected =
		"${{ inputs.sonar && github.event_name != 'merge_group' && github.actor != 'dependabot[bot]' && " +
		"(github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository) }}";

	/// <summary>The environment variable naming the exact nupkg the Release leg packed (contract 1.8).</summary>
	public const string ExactPackageVariable = "CESDK_PACKAGED_UMBRELLA_NUPKG";

	/// <summary>The xUnit v3 trait the Debug leg filters out: the packaging tests run in the Release leg only.</summary>
	public const string PackagingTrait = "Category=Packaging";

	/// <summary>The C0-only live probe: compiled by CI, never loaded or run.</summary>
	public const string LiveProbeProject = "tests/CheatEngine.SDK.LiveProbe/CheatEngine.SDK.LiveProbe.csproj";

	/// <summary>Retention of every <c>binlogs-*</c> artifact.</summary>
	public const string BinlogRetention = "5";

	/// <summary>The only runner labels a job may use (never a floating <c>-latest</c> label).</summary>
	public static readonly HashSet<string> RunnerLabels = new(StringComparer.Ordinal) { "windows-2025", "ubuntu-24.04" };

	/// <summary>The workflows that call ci.yml and so produce "CI / Gate".</summary>
	public static readonly string[] Callers = ["pull-request-ci.yml", "main-ci.yml", "release.yml"];

	/// <summary>The workflows of the pipeline itself, which declare <c>defaults.run.shell: pwsh</c>.</summary>
	public static readonly string[] PipelineWorkflows = [Pipeline, Sonar, "main-ci.yml", "pull-request-ci.yml"];

	/// <summary>Workflows whose every job is reachable from a release, Sonar or CodeQL run: no package cache there.</summary>
	public static readonly string[] CacheFreeWorkflows = [Pipeline, Sonar, "codeql.yml", "release.yml"];

	/// <summary>Jobs that may be absent from <c>gate.needs</c>: advisory, <c>continue-on-error: true</c>.</summary>
	public static readonly HashSet<string> AdvisoryJobs = new(StringComparer.Ordinal) { "client-canary" };

	/// <summary>The jobs of ci.yml after Wave 1 (contract 1.6): id, name, runner and timeout in minutes.</summary>
	public static readonly PipelineJob[] Jobs =
	[
		new("native", "Build native bridge", "windows-2025", 15),
		new("build-test", "Build and test (${{ matrix.configuration }})", "windows-2025", 45),
		new("aot", "Native AOT publication probe", "windows-2025", 25),
		new("sonar", "Sonar", null, null),
		new("lint", "Lint", "ubuntu-24.04", 10),
		new("format", "Format", "ubuntu-24.04", 10),
		new("dependency-review", "Dependency review", "ubuntu-24.04", 10),
		new("lock-files", "Lock files", "windows-2025", 15),
		new(GateJobId, GateJobName, "ubuntu-24.04", 5)
	];

	/// <summary>Job ids reserved for later waves, accepted with exactly these names once the integrator wires them.</summary>
	public static readonly Dictionary<string, string> ReservedJobs = new(StringComparer.Ordinal)
	{
		["native-host-emulator"] = "Native host emulator",
		["lua-surface"] = "Lua surface catalogue",
		["client-canary"] = "Client canary (advisory)",
		["examples"] = "Compile examples"
	};

	/// <summary>
	///     Every artifact name a workflow may upload (contract 1.9, the orchestrator's <c>attestation-bundles</c>, and the
	///     names of the advisory governance workflows), with its retention in days, or null where the contract leaves it
	///     to the producer. <c>{configuration}</c> is Debug or
	///     Release; <c>binlogs-*</c> names are checked by pattern with <see cref="BinlogRetention" />.
	/// </summary>
	public static readonly Dictionary<string, string?> ReservedArtifacts = new(StringComparer.Ordinal)
	{
		["lua-protection-bridge"] = "14",
		["classic-abi-fixture-facts"] = "14",
		["nuget-package"] = "${{ inputs.package-retention-days }}",
		["build-info"] = "${{ inputs.package-retention-days }}",
		["coverage"] = "7",
		["coverage-report"] = "7",
		["test-results-{configuration}"] = "7",
		["test-dumps-{configuration}"] = "5",
		["release-notes"] = "90",
		["native-host-emulator"] = "14",
		["lua-surface-report"] = "30",
		["client-canary-report"] = "14",
		["attestation-bundles"] = null,
		// Advisory workflows outside the gate: dependency-submission.yml hands its snapshot from the detect job to the
		// submit job, and scheduled-health.yml keeps its canary, repeated-test and bridge-drift reports.
		["dependency-snapshot"] = "5",
		["health-sdk-canary"] = "14",
		["health-test-repeat"] = "14",
		["health-bridge-drift"] = "30"
	};

	/// <summary>
	///     Known violations in files other work items own, each with the work that removes it. The list only shrinks: a
	///     test fails when an entry no longer matches a violation, so the entry is deleted in the commit that fixes it.
	/// </summary>
	public static readonly PendingViolation[] Pending = [];

	/// <summary>Reports <paramref name="violations" /> minus the pending ones, and pending entries that no longer match.</summary>
	public static void AssertNoViolations(string rule, IReadOnlyCollection<Violation> violations)
	{
		List<string> unexpected = [];
		HashSet<PendingViolation> matched = [];
		foreach (Violation violation in violations)
		{
			PendingViolation? pending = FindPending(rule, violation);
			if (pending is null)
			{
				unexpected.Add(violation.Message);
			}
			else
			{
				matched.Add(pending);
			}
		}

		foreach (PendingViolation pending in Pending)
		{
			if (string.Equals(pending.Rule, rule, StringComparison.Ordinal) && !matched.Contains(pending))
			{
				unexpected.Add(
					$"The pending {rule} violation {pending.File} '{pending.Subject}' is fixed: remove it from {nameof(WorkflowContract)}.{nameof(Pending)}.");
			}
		}

		Assert.True(unexpected.Count == 0, string.Join(Environment.NewLine, unexpected));
	}

	private static PendingViolation? FindPending(string rule, Violation violation)
	{
		foreach (PendingViolation pending in Pending)
		{
			if (string.Equals(pending.Rule, rule, StringComparison.Ordinal) &&
				string.Equals(pending.File, violation.File, StringComparison.Ordinal) &&
				string.Equals(pending.Subject, violation.Subject, StringComparison.Ordinal))
			{
				return pending;
			}
		}

		return null;
	}

	/// <summary>Rule names used by <see cref="Pending" />.</summary>
	public static class Rules
	{
		/// <summary>A job without a pinned runner label or a timeout.</summary>
		public const string Runner = "runner";

		/// <summary>A job that runs dotnet without the composite setup action.</summary>
		public const string DotnetSetup = "dotnet-setup";

		/// <summary>A native command whose exit code is not checked on the next line.</summary>
		public const string ExitCode = "exit-code";
	}
}
