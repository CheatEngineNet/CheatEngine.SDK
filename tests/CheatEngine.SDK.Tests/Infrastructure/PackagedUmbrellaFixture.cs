using System.Globalization;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Packs <c>src/CheatEngine.SDK/CheatEngine.SDK.csproj</c> once, to a throwaway local feed, then restores + builds
///     direct
///     and indirect plugin consumers against it: a default one that takes every package default, one that sets
///     <c>AllowUnsafeBlocks=false</c> itself, one that sets <c>CheatEngineSdkGenerateEntryPoint=false</c>, and one that
///     reaches the umbrella only through a second packed package. It also builds direct consumers for every supported
///     and explicitly unsupported <c>PlatformTarget</c> value, runs a package-only executable against the bundled
///     offline Lua fixture, and publishes then runs a separate package-only trim and Native AOT executable. Every fact
///     <c>Packaging/*.cs</c> asserts on is read here, once, through <see cref="Xunit.IClassFixture{TFixture}" />,
///     because the pipeline (real package, restore, build, clean/rebuild and publish operations)
///     is too expensive to repeat per test.
/// </summary>
/// <remarks>
///     Every consumer restore is pointed (<c>dotnet restore --packages</c>) at <see cref="PackagesDirectory" />, a
///     directory scoped to this fixture's own <c>_tempRoot</c>, never the machine-wide global-packages folder
///     (typically <c>%USERPROFILE%\.nuget\packages</c>). This matters because every pack in one session gets the SAME
///     MinVer-derived version (git commit height only changes on a new commit), and NuGet treats a package id+version
///     already extracted in the global-packages folder as immutable: a later restore against a freshly re-packed
///     <c>.nupkg</c> with different content but the same version would silently reuse whatever was extracted there by
///     an earlier run - this fixture's own previous run, a developer's manual restore, or another parallel build on
///     the same machine - with no error or warning. That would defeat this project's whole point (verifying the
///     freshly packed umbrella, not some earlier one) for every consumer-based assertion
///     (<c>EntryPointTests</c>, <c>BuildPropertyDefaultsTests</c>); only the nuspec/file-list tests, which read the
///     <c>.nupkg</c> directly via <see cref="NupkgInspector" />, would be unaffected. Starting every fixture run from an
///     empty, run-scoped packages directory removes the sharing that makes that possible.
/// </remarks>
public sealed class PackagedUmbrellaFixture : IAsyncLifetime
{
	private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan RuntimeRunTimeout = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan AotPublishTimeout = TimeSpan.FromMinutes(10);

	private static readonly (string Key, string ConsumerName, string? PlatformTarget)[] PlatformTargetConsumers =
	[
		("Unset", "UnsetPlatformTargetConsumer", null),
		("AnyCPU", "AnyCpuPlatformTargetConsumer", "AnyCPU"),
		("x64", "X64PlatformTargetConsumer", "x64"),
		("x86", "X86PlatformTargetConsumer", "x86"),
		("ARM", "ArmPlatformTargetConsumer", "ARM"),
		("ARM64", "Arm64PlatformTargetConsumer", "ARM64"),
		("Itanium", "ItaniumPlatformTargetConsumer", "Itanium"),
		("Unsupported", "UnsupportedPlatformTargetConsumer", "Unsupported")
	];

	private readonly Dictionary<string, string> _platformTargetConsumerBuildOutput = new(StringComparer.Ordinal);
	private readonly Dictionary<string, bool> _platformTargetConsumerBuildSucceeded = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _platformTargetConsumerEffectiveValues = new(StringComparer.Ordinal);

	private DirectoryInfo? _tempRoot;

	/// <summary>The <c>PackageVersion</c> MinVer gave the packed <c>.nupkg</c> (read back from its file name).</summary>
	public string PackageVersion
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     The fixture-local NuGet global-packages folder (<c>dotnet restore --packages</c>) every consumer restore
	///     extracts into, isolated per fixture run under this fixture's own <c>_tempRoot</c> so a stale extraction left
	///     by an earlier run can never shadow the nupkg this run just packed (see this class's remarks).
	/// </summary>
	public string PackagesDirectory
	{
		get;
		private set;
	} = "";

	/// <summary>Every entry path inside the packed <c>.nupkg</c>.</summary>
	public IReadOnlyList<string> PackageEntries
	{
		get;
		private set;
	} = [];

	/// <summary>The <c>id</c> of every <c>&lt;dependency&gt;</c> in the packed <c>.nuspec</c>, across every group.</summary>
	public IReadOnlyList<string> NuspecDependencyIds
	{
		get;
		private set;
	} = [];

	/// <summary>Full path of the packed <c>.nupkg</c> the fixture read.</summary>
	public string PackagePath
	{
		get;
		private set;
	} = "";

	/// <summary>The packed <c>.nuspec</c>.</summary>
	public XDocument Nuspec
	{
		get;
		private set;
	} = new();

	/// <summary>Whether <c>CESDK.CESDK</c> exists in the default consumer's built assembly.</summary>
	public bool DefaultEntryPointTypeExists
	{
		get;
		private set;
	}

	/// <summary>Whether that type declares a two-parameter <c>CEPluginInitialize</c>.</summary>
	public bool DefaultEntryPointMethodExists
	{
		get;
		private set;
	}

	/// <summary>Path of the native bridge copied into the default consumer's build output.</summary>
	public string DefaultNativeBridgePath
	{
		get;
		private set;
	} = "";

	/// <summary>Whether normal MSBuild clean bookkeeping removed the direct-only native bridge before rebuilding.</summary>
	public bool DefaultNativeBridgeWasRemovedByClean
	{
		get;
		private set;
	}

	/// <summary>The direct consumer's atomic plugin deployment directory.</summary>
	public string DefaultDeploymentDirectory
	{
		get;
		private set;
	} = "";

	/// <summary>Path of the direct consumer's runtime configuration file.</summary>
	public string DefaultRuntimeConfigPath
	{
		get;
		private set;
	} = "";

	/// <summary>Path of the direct consumer's dependency manifest.</summary>
	public string DefaultDepsJsonPath
	{
		get;
		private set;
	} = "";

	/// <summary>Path of the native bridge copied into the default consumer's publish output.</summary>
	public string DefaultPublishedNativeBridgePath
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     <c>AllowUnsafeBlocks</c>, <c>EnableDynamicLoading</c>, <c>CheatEngineSdkGenerateEntryPoint</c> for the default
	///     consumer.
	/// </summary>
	public IReadOnlyDictionary<string, string> DefaultProperties
	{
		get;
		private set;
	} =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary><c>AllowUnsafeBlocks</c> for the consumer that set it to <see langword="false" /> itself.</summary>
	public IReadOnlyDictionary<string, string> ExplicitUnsafeFalseProperties
	{
		get;
		private set;
	} =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>
	///     Whether the manual <c>CESDK.CESDK</c> bootstrap exists for the consumer that set
	///     <c>CheatEngineSdkGenerateEntryPoint=false</c>.
	/// </summary>
	public bool EntryPointOffTypeExists
	{
		get;
		private set;
	}

	/// <summary>Whether that manual bootstrap declares the host-required two-parameter initialization method.</summary>
	public bool EntryPointOffMethodExists
	{
		get;
		private set;
	}

	/// <summary>Whether a packed consumer with a Lua function and explicit unsafe opt-in built successfully.</summary>
	public bool LuaFunctionOptInConsumerBuildSucceeded
	{
		get;
		private set;
	}

	/// <summary>
	///     Whether the default packaged consumer built the historical <c>AobScanner.TryScan</c> overload compilation
	///     probe.
	/// </summary>
	public bool LegacyAobConsumerBuildSucceeded
	{
		get;
		private set;
	}

	/// <summary>Whether a packed consumer with a Lua function but no unsafe opt-in unexpectedly built successfully.</summary>
	public bool LuaFunctionWithoutUnsafeConsumerBuildSucceeded
	{
		get;
		private set;
	}

	/// <summary>Build output from the Lua-function consumer that intentionally leaves unsafe compilation disabled.</summary>
	public string LuaFunctionWithoutUnsafeConsumerBuildOutput
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     Whether the package-only Lua runtime executable completed its controlled, offline Lua fixture run
	///     successfully.
	/// </summary>
	/// <remarks>
	///     The executable receives this repository's bundled test-only <c>native/cheat-engine/lua53-64.dll</c> as an
	///     explicit argument. That DLL is an offline controlled Lua fixture, never a Cheat Engine host; this result does
	///     not claim a live Cheat Engine load or host interaction.
	/// </remarks>
	public bool PackedRuntimeConsumerRunSucceeded
	{
		get;
		private set;
	}

	/// <summary>Console output captured from the package-only Lua runtime executable.</summary>
	public string PackedRuntimeConsumerRunOutput
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     Whether the package-only consumer with two otherwise valid Lua exports of one name unexpectedly built.
	/// </summary>
	public bool DuplicateLuaFunctionConsumerBuildSucceeded
	{
		get;
		private set;
	}

	/// <summary>Build output from the package-only duplicate-Lua-function consumer.</summary>
	public string DuplicateLuaFunctionConsumerBuildOutput
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     Whether the package-only trim and Native AOT executable published successfully as a self-contained
	///     <c>win-x64</c> executable.
	/// </summary>
	/// <remarks>
	///     This proves only standalone package-consumer publication. It neither establishes nor implies that Cheat
	///     Engine can load, host, or unload an AOT plugin.
	/// </remarks>
	public bool PackedAotConsumerPublishSucceeded
	{
		get;
		private set;
	}

	/// <summary>Console output captured from the package-only trim and Native AOT publish operation.</summary>
	public string PackedAotConsumerPublishOutput
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     The AOT-relevant diagnostics configured as errors by the package-only consumer before it is published.
	/// </summary>
	public string PackedAotConsumerWarningsAsErrors
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     Whether the package-only AOT consumer explicitly enabled the unsafe generated Lua thunks require.
	/// </summary>
	public string PackedAotConsumerAllowUnsafeBlocks
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     The evaluated Native AOT publication settings and diagnostic suppressions for the package-only consumer.
	/// </summary>
	public IReadOnlyDictionary<string, string> PackedAotConsumerProperties
	{
		get;
		private set;
	} =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>
	///     Whether the published package-only trim and Native AOT executable completed successfully.
	/// </summary>
	/// <remarks>
	///     This standalone execution is not a Cheat Engine plugin load, host, or unload result.
	/// </remarks>
	public bool PackedAotConsumerRunSucceeded
	{
		get;
		private set;
	}

	/// <summary>Console output captured from the published package-only trim and Native AOT executable.</summary>
	public string PackedAotConsumerRunOutput
	{
		get;
		private set;
	} = "";

	/// <summary>The package-controlled properties evaluated by a consumer that references only the carrier package.</summary>
	public IReadOnlyDictionary<string, string> IndirectProperties
	{
		get;
		private set;
	} =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>Whether the indirect consumer incorrectly received the generated bootstrap.</summary>
	public bool IndirectEntryPointTypeExists
	{
		get;
		private set;
	}

	/// <summary>Path where an indirect consumer would incorrectly receive the direct-only native bridge at build time.</summary>
	public string IndirectNativeBridgePath
	{
		get;
		private set;
	} = "";

	/// <summary>Path where an indirect consumer would incorrectly receive the direct-only native bridge at publish time.</summary>
	public string IndirectPublishedNativeBridgePath
	{
		get;
		private set;
	} = "";

	/// <summary>
	///     Whether the direct package target completed the build for each named <c>PlatformTarget</c> consumer. The
	///     keys are <c>Unset</c>, <c>AnyCPU</c>, <c>x64</c>, <c>x86</c>, <c>ARM</c>, <c>ARM64</c>, <c>Itanium</c> and
	///     <c>Unsupported</c>.
	/// </summary>
	public IReadOnlyDictionary<string, bool> PlatformTargetConsumerBuildSucceeded =>
		_platformTargetConsumerBuildSucceeded;

	/// <summary>Diagnostic output from each named <c>PlatformTarget</c> consumer build.</summary>
	public IReadOnlyDictionary<string, string> PlatformTargetConsumerBuildOutput => _platformTargetConsumerBuildOutput;

	/// <summary>
	///     The evaluated <c>PlatformTarget</c> property for each named consumer, captured before its build target runs.
	/// </summary>
	public IReadOnlyDictionary<string, string> PlatformTargetConsumerEffectiveValues =>
		_platformTargetConsumerEffectiveValues;

	/// <inheritdoc />
	public async ValueTask InitializeAsync()
	{
		_tempRoot = Directory.CreateTempSubdirectory("cheatengine-sdk-umbrella-tests-");
		string feedDirectory = Path.Combine(_tempRoot.FullName, "feed");
		Directory.CreateDirectory(feedDirectory);
		string packagesDirectory = Path.Combine(_tempRoot.FullName, "packages");
		Directory.CreateDirectory(packagesDirectory);
		PackagesDirectory = packagesDirectory;

		await PackUmbrellaAsync(feedDirectory).ConfigureAwait(false);
		ReadPackedNupkg(feedDirectory);

		await InitializeDefaultConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializeExplicitUnsafeFalseConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializeEntryPointOffConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializeLuaFunctionConsumersAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializePackedRuntimeConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializeDuplicateLuaFunctionConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializePackedAotConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializeIndirectConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
		await InitializePlatformTargetConsumersAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
			.ConfigureAwait(false);
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (_tempRoot is not null)
		{
			try
			{
				_tempRoot.Delete(true);
			}
			catch (IOException)
			{
				// Best effort: a file a virus scanner or editor still has open must not fail the test run.
				_tempRoot = null;
			}
			catch (UnauthorizedAccessException)
			{
				// Best effort: an external process may temporarily deny the recursive cleanup operation.
				_tempRoot = null;
			}
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>
	///     Packing builds every project the umbrella embeds (the six libs, four active shipping components and their
	///     shared loader dependency), through the
	///     repository's own shared <c>artifacts/</c> directory - the one resource another build running at the same
	///     time might be touching, so a file-lock error is retried rather than treated as a real failure. A plain,
	///     unrelated compile error is not retried.
	/// </summary>
	private static async Task PackUmbrellaAsync(string feedDirectory)
	{
		const int maxAttempts = 5;
		string cheatEngineSdkProjectPath = RepositoryLayout.PathOf(UmbrellaPackage.ProjectPath);
		ProcessResult result = default;
		for (int attempt = 1; attempt <= maxAttempts; attempt++)
		{
			result = await ProcessRunner
				.RunAsync("dotnet", $"pack \"{cheatEngineSdkProjectPath}\" -c Release -o \"{feedDirectory}\" --nologo",
					RepositoryLayout.Root, PackTimeout)
				.ConfigureAwait(false);
			if (result.ExitCode == 0)
			{
				return;
			}

			if (attempt == maxAttempts || !LooksLikeFileLockContention(result.CombinedOutput))
			{
				break;
			}

			await Task.Delay(TimeSpan.FromSeconds(20 * attempt), TestContext.Current.CancellationToken)
				.ConfigureAwait(false);
		}

		throw new InvalidOperationException(
			$"'dotnet pack' of the umbrella package failed (exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}):{Environment.NewLine}{result.CombinedOutput}");
	}

	private static bool LooksLikeFileLockContention(string output)
	{
		return output.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
			   || output.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
			   || output.Contains("MSB3021", StringComparison.Ordinal)
			   || output.Contains("MSB3027", StringComparison.Ordinal)
			   || output.Contains("MSB3061", StringComparison.Ordinal);
	}

	private async Task InitializeDefaultConsumerAsync(string tempRoot, string feedDirectory, string packagesDirectory)
	{
		ThrowawayConsumer consumer = ThrowawayConsumer.Create(tempRoot, "DefaultConsumer", PackageVersion,
			feedDirectory,
			new ThrowawayConsumer.CreateOptions
			{
				IncludeLegacyAobConsumer = true,
				IncludeTargetBoundAllocationConsumer = true,
				IncludeRecordAndSymbolContract = true,
				IncludeValueScanConsumer = true
			});
		await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
		LegacyAobConsumerBuildSucceeded = File.Exists(Path.Combine(consumer.Directory, "LegacyAobConsumer.cs"));
		DefaultProperties = await consumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks", "EnableDynamicLoading",
				"CheatEngineSdkGenerateEntryPoint")
			.ConfigureAwait(false);
		DefaultNativeBridgeWasRemovedByClean = await CleanAndRebuildAsync(consumer).ConfigureAwait(false);

		(DefaultEntryPointTypeExists, DefaultEntryPointMethodExists) = EntryPointProbe.Probe(consumer.AssemblyPath);
		DefaultNativeBridgePath = consumer.NativeBridgePath;
		DefaultDeploymentDirectory = Path.GetDirectoryName(consumer.AssemblyPath)!;
		DefaultRuntimeConfigPath = Path.Combine(DefaultDeploymentDirectory, "DefaultConsumer.runtimeconfig.json");
		DefaultDepsJsonPath = Path.Combine(DefaultDeploymentDirectory, "DefaultConsumer.deps.json");

		string publishDirectory = Path.Combine(tempRoot, "published-default");
		await PublishAsync(consumer, publishDirectory).ConfigureAwait(false);
		DefaultPublishedNativeBridgePath = Path.Combine(publishDirectory, "cheatengine-sdk-lua-bridge.dll");
	}

	private async Task InitializeExplicitUnsafeFalseConsumerAsync(string tempRoot, string feedDirectory,
		string packagesDirectory)
	{
		ThrowawayConsumer consumer = ThrowawayConsumer.Create(tempRoot, "ExplicitUnsafeFalseConsumer", PackageVersion,
			feedDirectory,
			new ThrowawayConsumer.CreateOptions
			{
				ExtraProperties = "    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>\n"
			});
		await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
		ExplicitUnsafeFalseProperties = await consumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks")
			.ConfigureAwait(false);
	}

	private async Task InitializeEntryPointOffConsumerAsync(string tempRoot, string feedDirectory,
		string packagesDirectory)
	{
		ThrowawayConsumer consumer = ThrowawayConsumer.Create(tempRoot, "EntryPointOffConsumer", PackageVersion,
			feedDirectory,
			new ThrowawayConsumer.CreateOptions
			{
				ExtraProperties = "    <CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>\n"
			});
		// CESDK0003 deliberately makes the handoff explicit: disabling generation transfers ownership of the exact
		// host lookup identity to the plugin author. If the generator ignored the false switch, this source would also
		// make the consumer fail with the duplicate CESDK.CESDK type - so a successful build proves both contracts.
		await File.WriteAllTextAsync(Path.Combine(consumer.Directory, "ManualBootstrap.cs"), """
			namespace CESDK;

			public static class CESDK
			{
			    public static int CEPluginInitialize(System.IntPtr bootstrap, int opaqueArgument) => 1;
			}
			""", TestContext.Current.CancellationToken).ConfigureAwait(false);
		await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
		(EntryPointOffTypeExists, EntryPointOffMethodExists) = EntryPointProbe.Probe(consumer.AssemblyPath);
	}

	private async Task InitializeLuaFunctionConsumersAsync(string tempRoot, string feedDirectory,
		string packagesDirectory)
	{
		ThrowawayConsumer optInConsumer = ThrowawayConsumer.Create(tempRoot, "LuaFunctionOptInConsumer", PackageVersion,
			feedDirectory,
			new ThrowawayConsumer.CreateOptions
			{
				ExtraProperties = "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n",
				IncludeLuaFunction = true
			});
		ProcessResult optInRestore =
			await optInConsumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
		EnsureSucceeded(optInRestore, "dotnet restore", optInConsumer.ProjectPath);
		ProcessResult optInBuild = await optInConsumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		LuaFunctionOptInConsumerBuildSucceeded = optInBuild.ExitCode == 0;
		EnsureSucceeded(optInBuild, "dotnet build", optInConsumer.ProjectPath);

		ThrowawayConsumer withoutUnsafeConsumer = ThrowawayConsumer.Create(tempRoot, "LuaFunctionWithoutUnsafeConsumer",
			PackageVersion,
			feedDirectory,
			new ThrowawayConsumer.CreateOptions { IncludeLuaFunction = true });
		ProcessResult restore = await withoutUnsafeConsumer.RestoreAsync(RestoreTimeout, packagesDirectory)
			.ConfigureAwait(false);
		EnsureSucceeded(restore, "dotnet restore", withoutUnsafeConsumer.ProjectPath);

		ProcessResult build = await withoutUnsafeConsumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		LuaFunctionWithoutUnsafeConsumerBuildSucceeded = build.ExitCode == 0;
		LuaFunctionWithoutUnsafeConsumerBuildOutput = build.CombinedOutput;
	}

	private async Task InitializePackedRuntimeConsumerAsync(string tempRoot, string feedDirectory,
		string packagesDirectory)
	{
		ThrowawayConsumer consumer = ThrowawayConsumer.CreateRuntimeExecutable(tempRoot, "PackedRuntimeConsumer",
			PackageVersion,
			feedDirectory, "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n");
		ProcessResult restore = await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
		EnsureSucceeded(restore, "dotnet restore", consumer.ProjectPath);
		ProcessResult build = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		EnsureSucceeded(build, "dotnet build", consumer.ProjectPath);

		string bundledLuaPath = RepositoryLayout.PathOf("native/cheat-engine/lua53-64.dll");
		if (!File.Exists(bundledLuaPath))
		{
			throw new InvalidOperationException(
				$"The package-only Lua runtime consumer requires the bundled test-only Lua fixture at '{bundledLuaPath}'.");
		}

		ProcessResult run = await consumer.RunAsync(bundledLuaPath, RuntimeRunTimeout).ConfigureAwait(false);
		PackedRuntimeConsumerRunSucceeded = run.ExitCode == 0;
		PackedRuntimeConsumerRunOutput = run.CombinedOutput;
	}

	private async Task InitializeDuplicateLuaFunctionConsumerAsync(string tempRoot, string feedDirectory,
		string packagesDirectory)
	{
		ThrowawayConsumer consumer = ThrowawayConsumer.CreateInvalidDuplicateLuaFunctionConsumer(tempRoot,
			"DuplicateLuaFunctionConsumer", PackageVersion, feedDirectory,
			"    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n");
		ProcessResult restore = await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
		EnsureSucceeded(restore, "dotnet restore", consumer.ProjectPath);

		ProcessResult build = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		DuplicateLuaFunctionConsumerBuildSucceeded = build.ExitCode == 0;
		DuplicateLuaFunctionConsumerBuildOutput = build.CombinedOutput;
	}

	private async Task InitializePackedAotConsumerAsync(string tempRoot, string feedDirectory, string packagesDirectory)
	{
		const string consumerName = "PackedAotConsumer";
		ThrowawayConsumer consumer = ThrowawayConsumer.CreateAotExecutable(tempRoot, consumerName, PackageVersion,
			feedDirectory,
			"""
			    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
			    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
			    <SelfContained>true</SelfContained>
			    <PublishTrimmed>true</PublishTrimmed>
			    <PublishAot>true</PublishAot>
			    <VerifyReferenceAotCompatibility>true</VerifyReferenceAotCompatibility>
			    <WarningsAsErrors>IL2026;IL3050;IL3058</WarningsAsErrors>
			""");
		ProcessResult restore = await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
		EnsureSucceeded(restore, "dotnet restore", consumer.ProjectPath);
		IReadOnlyDictionary<string, string> aotProperties = await consumer
			.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks", "PublishAot", "PublishTrimmed", "SelfContained",
				"RuntimeIdentifier", "VerifyReferenceAotCompatibility", "WarningsAsErrors", "NoWarn")
			.ConfigureAwait(false);
		PackedAotConsumerProperties = aotProperties;
		PackedAotConsumerAllowUnsafeBlocks = aotProperties["AllowUnsafeBlocks"];
		PackedAotConsumerWarningsAsErrors = aotProperties["WarningsAsErrors"];
		ProcessResult build = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		EnsureSucceeded(build, "dotnet build", consumer.ProjectPath);

		string publishDirectory = Path.Combine(tempRoot, "published-packed-aot");
		ProcessResult publish = await consumer.PublishAsync(AotPublishTimeout, publishDirectory).ConfigureAwait(false);
		PackedAotConsumerPublishSucceeded = publish.ExitCode == 0;
		PackedAotConsumerPublishOutput = publish.CombinedOutput;
		if (!PackedAotConsumerPublishSucceeded)
		{
			return;
		}

		string bundledLuaPath = RepositoryLayout.PathOf("native/cheat-engine/lua53-64.dll");
		if (!File.Exists(bundledLuaPath))
		{
			throw new InvalidOperationException(
				$"The package-only AOT consumer requires the bundled test-only Lua fixture at '{bundledLuaPath}'.");
		}

		ProcessResult run = await consumer.RunPublishedAsync(publishDirectory, bundledLuaPath, RuntimeRunTimeout)
			.ConfigureAwait(false);
		PackedAotConsumerRunSucceeded = run.ExitCode == 0;
		PackedAotConsumerRunOutput = run.CombinedOutput;
	}

	private async Task InitializeIndirectConsumerAsync(string tempRoot, string feedDirectory, string packagesDirectory)
	{
		ThrowawayPackageCarrier carrier = ThrowawayPackageCarrier.Create(tempRoot, PackageVersion, feedDirectory);
		ProcessResult carrierRestore =
			await carrier.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
		EnsureSucceeded(carrierRestore, "dotnet restore", carrier.ProjectPath);
		ProcessResult carrierPack = await carrier.PackAsync(PackTimeout, feedDirectory).ConfigureAwait(false);
		EnsureSucceeded(carrierPack, "dotnet pack", carrier.ProjectPath);

		ThrowawayConsumer consumer = ThrowawayConsumer.CreateIndirect(tempRoot, ThrowawayPackageCarrier.PackageId,
			ThrowawayPackageCarrier.PackageVersion, feedDirectory);
		await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
		IndirectProperties = await consumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks",
				"EnableDynamicLoading",
				"CheatEngineSdkGenerateEntryPoint")
			.ConfigureAwait(false);
		(IndirectEntryPointTypeExists, _) = EntryPointProbe.Probe(consumer.AssemblyPath);
		IndirectNativeBridgePath = consumer.NativeBridgePath;

		string publishDirectory = Path.Combine(tempRoot, "published-indirect");
		await PublishAsync(consumer, publishDirectory).ConfigureAwait(false);
		IndirectPublishedNativeBridgePath = Path.Combine(publishDirectory, "cheatengine-sdk-lua-bridge.dll");
	}

	private async Task InitializePlatformTargetConsumersAsync(string tempRoot, string feedDirectory,
		string packagesDirectory)
	{
		foreach ((string key, string consumerName, string? platformTarget) in PlatformTargetConsumers)
		{
			ThrowawayConsumer consumer = ThrowawayConsumer.Create(tempRoot, consumerName, PackageVersion, feedDirectory,
				new ThrowawayConsumer.CreateOptions { PlatformTarget = platformTarget });
			ProcessResult restore =
				await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
			EnsureSucceeded(restore, "dotnet restore", consumer.ProjectPath);

			IReadOnlyDictionary<string, string> effectiveProperties = await consumer
				.GetPropertiesAsync(BuildTimeout, "PlatformTarget")
				.ConfigureAwait(false);
			_platformTargetConsumerEffectiveValues[key] = effectiveProperties["PlatformTarget"];

			ProcessResult build = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
			_platformTargetConsumerBuildSucceeded[key] = build.ExitCode == 0;
			_platformTargetConsumerBuildOutput[key] = build.CombinedOutput;
		}
	}

	private static async Task<bool> CleanAndRebuildAsync(ThrowawayConsumer consumer)
	{
		ProcessResult clean = await consumer.CleanAsync(BuildTimeout).ConfigureAwait(false);
		EnsureSucceeded(clean, "dotnet clean", consumer.ProjectPath);
		bool bridgeWasRemoved = !File.Exists(consumer.NativeBridgePath);
		ProcessResult rebuild = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		EnsureSucceeded(rebuild, "dotnet build after clean", consumer.ProjectPath);
		return bridgeWasRemoved;
	}

	private static async Task PublishAsync(ThrowawayConsumer consumer, string outputDirectory)
	{
		ProcessResult publish = await consumer.PublishAsync(PublishTimeout, outputDirectory).ConfigureAwait(false);
		EnsureSucceeded(publish, "dotnet publish", consumer.ProjectPath);
	}

	private static void EnsureSucceeded(ProcessResult result, string operation, string projectPath)
	{
		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"'{operation}' failed for '{projectPath}' (exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}):{Environment.NewLine}{result.CombinedOutput}");
		}
	}

	private void ReadPackedNupkg(string feedDirectory)
	{
		string[] nupkgPaths = Directory.GetFiles(feedDirectory, $"{UmbrellaPackage.Id}.*.nupkg");
		if (nupkgPaths.Length != 1)
		{
			throw new InvalidOperationException(
				$"Expected exactly one {UmbrellaPackage.Id}.*.nupkg in '{feedDirectory}', found {nupkgPaths.Length.ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", nupkgPaths)}");
		}

		string fileName = Path.GetFileName(nupkgPaths[0]);
		PackageVersion = fileName[(UmbrellaPackage.Id.Length + 1)..^".nupkg".Length];

		(IReadOnlyList<string> entries, XDocument nuspec) = NupkgInspector.Read(nupkgPaths[0]);
		PackagePath = nupkgPaths[0];
		Nuspec = nuspec;
		PackageEntries = entries;
		NuspecDependencyIds = NupkgInspector.GetDependencyIds(nuspec);
	}

	private static async Task RestoreAndBuildAsync(ThrowawayConsumer consumer, string packagesDirectory)
	{
		ProcessResult restoreResult =
			await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
		if (restoreResult.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"'dotnet restore' failed for '{consumer.ProjectPath}' (exit {restoreResult.ExitCode.ToString(CultureInfo.InvariantCulture)}):{Environment.NewLine}{restoreResult.CombinedOutput}");
		}

		ProcessResult buildResult = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
		if (buildResult.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"'dotnet build' failed for '{consumer.ProjectPath}' (exit {buildResult.ExitCode.ToString(CultureInfo.InvariantCulture)}):{Environment.NewLine}{buildResult.CombinedOutput}");
		}
	}
}
