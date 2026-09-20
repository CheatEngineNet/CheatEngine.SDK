namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Packs <c>src/CheatEngine.SDK/CheatEngine.SDK.csproj</c> once, to a throwaway local feed, then restores + builds
///     direct
///     and indirect plugin consumers against it: a default one that takes every package default, one that sets
///     <c>AllowUnsafeBlocks=false</c> itself, one that sets <c>CheatEngineSdkGenerateEntryPoint=false</c>, and one that
///     reaches the umbrella only through a second packed package. It also builds direct consumers for every supported
///     and explicitly unsupported <c>PlatformTarget</c> value. Every fact
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
    public string PackageVersion { get; private set; } = "";

    /// <summary>
    ///     The fixture-local NuGet global-packages folder (<c>dotnet restore --packages</c>) every consumer restore
    ///     extracts into, isolated per fixture run under this fixture's own <c>_tempRoot</c> so a stale extraction left
    ///     by an earlier run can never shadow the nupkg this run just packed (see this class's remarks).
    /// </summary>
    public string PackagesDirectory { get; private set; } = "";

    /// <summary>Every entry path inside the packed <c>.nupkg</c>.</summary>
    public IReadOnlyList<string> PackageEntries { get; private set; } = [];

    /// <summary>The <c>id</c> of every <c>&lt;dependency&gt;</c> in the packed <c>.nuspec</c>, across every group.</summary>
    public IReadOnlyList<string> NuspecDependencyIds { get; private set; } = [];

    /// <summary>Whether <c>CESDK.CESDK</c> exists in the default consumer's built assembly.</summary>
    public bool DefaultEntryPointTypeExists { get; private set; }

    /// <summary>Whether that type declares a two-parameter <c>CEPluginInitialize</c>.</summary>
    public bool DefaultEntryPointMethodExists { get; private set; }

    /// <summary>Path of the native bridge copied into the default consumer's build output.</summary>
    public string DefaultNativeBridgePath { get; private set; } = "";

    /// <summary>Whether normal MSBuild clean bookkeeping removed the direct-only native bridge before rebuilding.</summary>
    public bool DefaultNativeBridgeWasRemovedByClean { get; private set; }

    /// <summary>The direct consumer's atomic plugin deployment directory.</summary>
    public string DefaultDeploymentDirectory { get; private set; } = "";

    /// <summary>Path of the direct consumer's runtime configuration file.</summary>
    public string DefaultRuntimeConfigPath { get; private set; } = "";

    /// <summary>Path of the direct consumer's dependency manifest.</summary>
    public string DefaultDepsJsonPath { get; private set; } = "";

    /// <summary>Path of the native bridge copied into the default consumer's publish output.</summary>
    public string DefaultPublishedNativeBridgePath { get; private set; } = "";

    /// <summary>
    ///     <c>AllowUnsafeBlocks</c>, <c>EnableDynamicLoading</c>, <c>CheatEngineSdkGenerateEntryPoint</c> for the default
    ///     consumer.
    /// </summary>
    public IReadOnlyDictionary<string, string> DefaultProperties { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary><c>AllowUnsafeBlocks</c> for the consumer that set it to <c>false</c> itself.</summary>
    public IReadOnlyDictionary<string, string> ExplicitUnsafeFalseProperties { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     Whether the manual <c>CESDK.CESDK</c> bootstrap exists for the consumer that set
    ///     <c>CheatEngineSdkGenerateEntryPoint=false</c>.
    /// </summary>
    public bool EntryPointOffTypeExists { get; private set; }

    /// <summary>Whether that manual bootstrap declares the host-required two-parameter initialization method.</summary>
    public bool EntryPointOffMethodExists { get; private set; }

    /// <summary>Whether a packed consumer with a Lua function and explicit unsafe opt-in built successfully.</summary>
    public bool LuaFunctionOptInConsumerBuildSucceeded { get; private set; }

    /// <summary>Whether a packed consumer with a Lua function but no unsafe opt-in unexpectedly built successfully.</summary>
    public bool LuaFunctionWithoutUnsafeConsumerBuildSucceeded { get; private set; }

    /// <summary>Build output from the Lua-function consumer that intentionally leaves unsafe compilation disabled.</summary>
    public string LuaFunctionWithoutUnsafeConsumerBuildOutput { get; private set; } = "";

    /// <summary>The package-controlled properties evaluated by a consumer that references only the carrier package.</summary>
    public IReadOnlyDictionary<string, string> IndirectProperties { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the indirect consumer incorrectly received the generated bootstrap.</summary>
    public bool IndirectEntryPointTypeExists { get; private set; }

    /// <summary>Path where an indirect consumer would incorrectly receive the direct-only native bridge at build time.</summary>
    public string IndirectNativeBridgePath { get; private set; } = "";

    /// <summary>Path where an indirect consumer would incorrectly receive the direct-only native bridge at publish time.</summary>
    public string IndirectPublishedNativeBridgePath { get; private set; } = "";

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
        var feedDirectory = Path.Combine(_tempRoot.FullName, "feed");
        Directory.CreateDirectory(feedDirectory);
        var packagesDirectory = Path.Combine(_tempRoot.FullName, "packages");
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
        await InitializeIndirectConsumerAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
            .ConfigureAwait(false);
        await InitializePlatformTargetConsumersAsync(_tempRoot.FullName, feedDirectory, packagesDirectory)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_tempRoot is not null)
            try
            {
                _tempRoot.Delete(true);
            }
            catch (IOException)
            {
                // Best effort: a file a virus scanner or editor still has open must not fail the test run.
            }
            catch (UnauthorizedAccessException)
            {
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
        var cheatEngineSdkProjectPath = RepositoryLayout.PathOf(UmbrellaPackage.ProjectPath);
        ProcessResult result = default;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = await ProcessRunner
                .RunAsync("dotnet", $"pack \"{cheatEngineSdkProjectPath}\" -c Release -o \"{feedDirectory}\" --nologo",
                    RepositoryLayout.Root, PackTimeout)
                .ConfigureAwait(false);
            if (result.ExitCode == 0) return;

            if (attempt == maxAttempts || !LooksLikeFileLockContention(result.CombinedOutput)) break;

            await Task.Delay(TimeSpan.FromSeconds(20 * attempt)).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"'dotnet pack' of the umbrella package failed (exit {result.ExitCode}):{Environment.NewLine}{result.CombinedOutput}");
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
        var consumer = ThrowawayConsumer.Create(tempRoot, "DefaultConsumer", PackageVersion, feedDirectory);
        await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
        DefaultProperties = await consumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks", "EnableDynamicLoading",
                "CheatEngineSdkGenerateEntryPoint")
            .ConfigureAwait(false);
        DefaultNativeBridgeWasRemovedByClean = await CleanAndRebuildAsync(consumer).ConfigureAwait(false);

        (DefaultEntryPointTypeExists, DefaultEntryPointMethodExists) = EntryPointProbe.Probe(consumer.AssemblyPath);
        DefaultNativeBridgePath = consumer.NativeBridgePath;
        DefaultDeploymentDirectory = Path.GetDirectoryName(consumer.AssemblyPath)!;
        DefaultRuntimeConfigPath = Path.Combine(DefaultDeploymentDirectory, "DefaultConsumer.runtimeconfig.json");
        DefaultDepsJsonPath = Path.Combine(DefaultDeploymentDirectory, "DefaultConsumer.deps.json");

        var publishDirectory = Path.Combine(tempRoot, "published-default");
        await PublishAsync(consumer, publishDirectory).ConfigureAwait(false);
        DefaultPublishedNativeBridgePath = Path.Combine(publishDirectory, "cheatengine-sdk-lua-bridge.dll");
    }

    private async Task InitializeExplicitUnsafeFalseConsumerAsync(string tempRoot, string feedDirectory,
        string packagesDirectory)
    {
        var consumer = ThrowawayConsumer.Create(tempRoot, "ExplicitUnsafeFalseConsumer", PackageVersion, feedDirectory,
            "    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>\n");
        await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
        ExplicitUnsafeFalseProperties = await consumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks")
            .ConfigureAwait(false);
    }

    private async Task InitializeEntryPointOffConsumerAsync(string tempRoot, string feedDirectory,
        string packagesDirectory)
    {
        var consumer = ThrowawayConsumer.Create(tempRoot, "EntryPointOffConsumer", PackageVersion, feedDirectory,
            "    <CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>\n");
        // CESDK0003 deliberately makes the handoff explicit: disabling generation transfers ownership of the exact
        // host lookup identity to the plugin author. If the generator ignored the false switch, this source would also
        // make the consumer fail with the duplicate CESDK.CESDK type - so a successful build proves both contracts.
        File.WriteAllText(Path.Combine(consumer.Directory, "ManualBootstrap.cs"), """
            namespace CESDK;

            public static class CESDK
            {
                public static int CEPluginInitialize(System.IntPtr bootstrap, int opaqueArgument) => 1;
            }
            """);
        await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
        (EntryPointOffTypeExists, EntryPointOffMethodExists) = EntryPointProbe.Probe(consumer.AssemblyPath);
    }

    private async Task InitializeLuaFunctionConsumersAsync(string tempRoot, string feedDirectory,
        string packagesDirectory)
    {
        var optInConsumer = ThrowawayConsumer.Create(tempRoot, "LuaFunctionOptInConsumer", PackageVersion,
            feedDirectory,
            "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n", includeLuaFunction: true);
        var optInRestore = await optInConsumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
        EnsureSucceeded(optInRestore, "dotnet restore", optInConsumer.ProjectPath);
        var optInBuild = await optInConsumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
        LuaFunctionOptInConsumerBuildSucceeded = optInBuild.ExitCode == 0;
        EnsureSucceeded(optInBuild, "dotnet build", optInConsumer.ProjectPath);

        var withoutUnsafeConsumer = ThrowawayConsumer.Create(tempRoot, "LuaFunctionWithoutUnsafeConsumer",
            PackageVersion,
            feedDirectory, includeLuaFunction: true);
        var restore = await withoutUnsafeConsumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
        EnsureSucceeded(restore, "dotnet restore", withoutUnsafeConsumer.ProjectPath);

        var build = await withoutUnsafeConsumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
        LuaFunctionWithoutUnsafeConsumerBuildSucceeded = build.ExitCode == 0;
        LuaFunctionWithoutUnsafeConsumerBuildOutput = build.CombinedOutput;
    }

    private async Task InitializeIndirectConsumerAsync(string tempRoot, string feedDirectory, string packagesDirectory)
    {
        var carrier = ThrowawayPackageCarrier.Create(tempRoot, PackageVersion, feedDirectory);
        var carrierRestore = await carrier.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
        EnsureSucceeded(carrierRestore, "dotnet restore", carrier.ProjectPath);
        var carrierPack = await carrier.PackAsync(PackTimeout, feedDirectory).ConfigureAwait(false);
        EnsureSucceeded(carrierPack, "dotnet pack", carrier.ProjectPath);

        var consumer = ThrowawayConsumer.CreateIndirect(tempRoot, ThrowawayPackageCarrier.PackageId,
            ThrowawayPackageCarrier.PackageVersion, feedDirectory);
        await RestoreAndBuildAsync(consumer, packagesDirectory).ConfigureAwait(false);
        IndirectProperties = await consumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks",
                "EnableDynamicLoading",
                "CheatEngineSdkGenerateEntryPoint")
            .ConfigureAwait(false);
        (IndirectEntryPointTypeExists, _) = EntryPointProbe.Probe(consumer.AssemblyPath);
        IndirectNativeBridgePath = consumer.NativeBridgePath;

        var publishDirectory = Path.Combine(tempRoot, "published-indirect");
        await PublishAsync(consumer, publishDirectory).ConfigureAwait(false);
        IndirectPublishedNativeBridgePath = Path.Combine(publishDirectory, "cheatengine-sdk-lua-bridge.dll");
    }

    private async Task InitializePlatformTargetConsumersAsync(string tempRoot, string feedDirectory,
        string packagesDirectory)
    {
        foreach (var (key, consumerName, platformTarget) in PlatformTargetConsumers)
        {
            var consumer = ThrowawayConsumer.Create(tempRoot, consumerName, PackageVersion, feedDirectory,
                platformTarget: platformTarget);
            var restore = await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
            EnsureSucceeded(restore, "dotnet restore", consumer.ProjectPath);

            var effectiveProperties = await consumer.GetPropertiesAsync(BuildTimeout, "PlatformTarget")
                .ConfigureAwait(false);
            _platformTargetConsumerEffectiveValues[key] = effectiveProperties["PlatformTarget"];

            var build = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
            _platformTargetConsumerBuildSucceeded[key] = build.ExitCode == 0;
            _platformTargetConsumerBuildOutput[key] = build.CombinedOutput;
        }
    }

    private static async Task<bool> CleanAndRebuildAsync(ThrowawayConsumer consumer)
    {
        var clean = await consumer.CleanAsync(BuildTimeout).ConfigureAwait(false);
        EnsureSucceeded(clean, "dotnet clean", consumer.ProjectPath);
        var bridgeWasRemoved = !File.Exists(consumer.NativeBridgePath);
        var rebuild = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
        EnsureSucceeded(rebuild, "dotnet build after clean", consumer.ProjectPath);
        return bridgeWasRemoved;
    }

    private static async Task PublishAsync(ThrowawayConsumer consumer, string outputDirectory)
    {
        var publish = await consumer.PublishAsync(PublishTimeout, outputDirectory).ConfigureAwait(false);
        EnsureSucceeded(publish, "dotnet publish", consumer.ProjectPath);
    }

    private static void EnsureSucceeded(ProcessResult result, string operation, string projectPath)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'{operation}' failed for '{projectPath}' (exit {result.ExitCode}):{Environment.NewLine}{result.CombinedOutput}");
    }

    private void ReadPackedNupkg(string feedDirectory)
    {
        var nupkgPaths = Directory.GetFiles(feedDirectory, $"{UmbrellaPackage.Id}.*.nupkg");
        if (nupkgPaths.Length != 1)
            throw new InvalidOperationException(
                $"Expected exactly one {UmbrellaPackage.Id}.*.nupkg in '{feedDirectory}', found {nupkgPaths.Length}: {string.Join(", ", nupkgPaths)}");

        var fileName = Path.GetFileName(nupkgPaths[0]);
        PackageVersion = fileName[(UmbrellaPackage.Id.Length + 1)..^".nupkg".Length];

        var (entries, nuspec) = NupkgInspector.Read(nupkgPaths[0]);
        PackageEntries = entries;
        NuspecDependencyIds = NupkgInspector.GetDependencyIds(nuspec);
    }

    private static async Task RestoreAndBuildAsync(ThrowawayConsumer consumer, string packagesDirectory)
    {
        var restoreResult = await consumer.RestoreAsync(RestoreTimeout, packagesDirectory).ConfigureAwait(false);
        if (restoreResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet restore' failed for '{consumer.ProjectPath}' (exit {restoreResult.ExitCode}):{Environment.NewLine}{restoreResult.CombinedOutput}");

        var buildResult = await consumer.BuildAsync(BuildTimeout).ConfigureAwait(false);
        if (buildResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet build' failed for '{consumer.ProjectPath}' (exit {buildResult.ExitCode}):{Environment.NewLine}{buildResult.CombinedOutput}");
    }
}
