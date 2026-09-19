namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Packs <c>src/CheatEngine.SDK/CheatEngine.SDK.csproj</c> once, to a throwaway local feed, and restores + builds three throwaway
///     consumer plugin projects against it: a default one that takes every package default, one that sets
///     <c>AllowUnsafeBlocks=false</c> itself, and one that sets <c>CheatEngineSdkGenerateEntryPoint=false</c>. Every fact
///     <c>Packaging/*.cs</c> asserts on is read here, once, through <see cref="Xunit.IClassFixture{TFixture}" />,
///     because the pipeline (a real <c>dotnet pack</c> plus three real <c>dotnet restore</c>/<c>dotnet build</c> runs)
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
    ///     Whether <c>CESDK.CESDK</c> exists for the consumer that set <c>CheatEngineSdkGenerateEntryPoint=false</c> (it must
    ///     not).
    /// </summary>
    public bool EntryPointOffTypeExists { get; private set; }

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

        var defaultConsumer =
            ThrowawayConsumer.Create(_tempRoot.FullName, "DefaultConsumer", PackageVersion, feedDirectory);
        await RestoreAndBuildAsync(defaultConsumer, packagesDirectory).ConfigureAwait(false);
        DefaultProperties = await defaultConsumer.GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks",
                "EnableDynamicLoading", "CheatEngineSdkGenerateEntryPoint")
            .ConfigureAwait(false);
        (DefaultEntryPointTypeExists, DefaultEntryPointMethodExists) =
            EntryPointProbe.Probe(defaultConsumer.AssemblyPath);
        DefaultNativeBridgePath = defaultConsumer.NativeBridgePath;

        var publishDirectory = Path.Combine(_tempRoot.FullName, "published-default");
        var publishResult = await defaultConsumer.PublishAsync(PublishTimeout, publishDirectory).ConfigureAwait(false);
        if (publishResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet publish' failed for '{defaultConsumer.ProjectPath}' (exit {publishResult.ExitCode}):{Environment.NewLine}{publishResult.CombinedOutput}");
        DefaultPublishedNativeBridgePath = Path.Combine(publishDirectory, "cheatengine-sdk-lua-bridge.dll");

        var explicitFalseConsumer = ThrowawayConsumer.Create(
            _tempRoot.FullName, "ExplicitUnsafeFalseConsumer", PackageVersion, feedDirectory,
            "    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>\n");
        await RestoreAndBuildAsync(explicitFalseConsumer, packagesDirectory).ConfigureAwait(false);
        ExplicitUnsafeFalseProperties = await explicitFalseConsumer
            .GetPropertiesAsync(BuildTimeout, "AllowUnsafeBlocks").ConfigureAwait(false);

        var entryPointOffConsumer = ThrowawayConsumer.Create(
            _tempRoot.FullName, "EntryPointOffConsumer", PackageVersion, feedDirectory,
            "    <CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>\n");
        await RestoreAndBuildAsync(entryPointOffConsumer, packagesDirectory).ConfigureAwait(false);
        (EntryPointOffTypeExists, _) = EntryPointProbe.Probe(entryPointOffConsumer.AssemblyPath);
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
    ///     Packing builds every project the umbrella embeds (the six libs, the four shipping components), through the
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
