using System.Globalization;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     A minimal, disposable plugin project scaffolded on disk that references the packed <c>CheatEngine.SDK</c> package
///     from a
///     local, offline feed: everything a real plugin author's project would be, and nothing this repository's own
///     build gates (warnings as errors, analyzers, <c>Directory.Build.props</c>) that a plugin author never opts into.
/// </summary>
internal sealed class ThrowawayConsumer
{
    private const string PluginSource = """
                                        using CheatEngine.SDK.Annotations.Plugin;
                                        using CheatEngine.SDK.Hosting.Plugin;

                                        namespace ThrowawayPlugin;

                                        [CheatEnginePlugin("Throwaway consumer plugin")]
                                        public sealed class Plugin : CheatEnginePlugin
                                        {
                                            protected override void OnEnable()
                                            {
                                            }

                                            protected override void OnDisable()
                                            {
                                            }
                                        }
                                        """;

    private const string LuaFunctionSource = """
                                             using CheatEngine.SDK.Annotations.Lua;
                                             using CheatEngine.SDK.Lua.Registration;
                                             using CheatEngine.SDK.Lua.State;

                                             namespace ThrowawayPlugin;

                                             internal static partial class Functions
                                             {
                                                 [LuaFunction("throwaway_ping")]
                                                 public static long Ping() => 1;

                                                 // Compile the generated lease surface from the freshly packed package.
                                                 // This method is intentionally not a lifecycle recipe and is never invoked here.
                                                 public static void CompileLeaseConsumer(LuaState state)
                                                 {
                                                     LuaRegistrationResult registration = TryRegisterLuaFunctions(state,
                                                         LuaRegistrationCollisionPolicy.RejectExisting);
                                                     registration.Lease?.Dispose();
                                                 }
                                             }
                                             """;

    private const string LegacyAobSource = """
                                           using CheatEngine.SDK.Engine.Objects;
                                           using CheatEngine.SDK.Engine.Scanning.Aob;

                                           namespace ThrowawayPlugin;

                                           internal static class LegacyAobConsumer
                                           {
                                               internal static void CompileOnly()
                                               {
                                                   if (AobScanner.TryScan("90", out Owned<StringList>? defaultResults))
                                                       defaultResults.Dispose();

                                                   if (AobScanner.TryScan("90", AobScanOptions.Default,
                                                           out Owned<StringList>? configuredResults))
                                                       configuredResults.Dispose();
                                               }
                                           }
                                           """;

    private const string TargetBoundAllocationSource = """
                                                   using CheatEngine.SDK.Engine.Allocation;
                                                   using CheatEngine.SDK.Engine.Errors;
                                                   using CheatEngine.SDK.Engine.Targets;
                                                   using CheatEngine.SDK.Engine.Values;
                                                   using CheatEngine.SDK.Lua.Calls;

                                                   namespace ThrowawayPlugin;

                                                   internal sealed class TargetBoundAllocationBackend : ITargetBoundMemoryAllocationOperations
                                                   {
                                                       public TargetMemoryAllocationOutcome AllocateBoundWithOutcome(
                                                           TargetAllocationRequest request, out TargetProcessIncarnation incarnation,
                                                           out TargetSelectionObservation observation)
                                                       {
                                                           observation = TargetSelection.ObserveCurrent();
                                                           incarnation = observation.Incarnation.GetValueOrDefault();
                                                           return TargetMemoryAllocationOutcome.Failed(observation.IsQualified
                                                               ? TargetMemoryOperationOutcome.Failed(
                                                                   EngineFailureKind.ExpectedOperationFailure)
                                                               : CreateObservationFailure(observation));
                                                       }

                                                       public bool TryDeallocateBound(TargetProcessIncarnation expected, Address address,
                                                           TargetAllocationSize size, out TargetIdentityCheck targetCheck)
                                                       {
                                                           var outcome = DeallocateBoundWithOutcome(expected, address, size, out targetCheck);
                                                           return targetCheck.IsCurrent && outcome.IsSuccess;
                                                       }

                                                       public TargetMemoryOperationOutcome DeallocateBoundWithOutcome(
                                                           TargetProcessIncarnation expected, Address address, TargetAllocationSize size,
                                                           out TargetIdentityCheck targetCheck)
                                                       {
                                                           targetCheck = TargetSelection.ValidateCurrent(expected);
                                                           return targetCheck.IsCurrent
                                                               ? TargetMemoryOperationOutcome.Succeeded()
                                                               : CreateTargetCheckFailure(targetCheck);
                                                       }

                                                       private static TargetMemoryOperationOutcome CreateObservationFailure(
                                                           TargetSelectionObservation observation)
                                                       {
                                                           return observation.Status == TargetSelectionObservationStatus.LuaFailure
                                                               ? TargetMemoryOperationOutcome.Failed(
                                                                   EngineFailureKind.ProtectedLuaFailure, LuaStatus.RuntimeError)
                                                               : TargetMemoryOperationOutcome.Failed(
                                                                   EngineFailureKind.TargetIdentityUnavailable);
                                                       }

                                                       private static TargetMemoryOperationOutcome CreateTargetCheckFailure(
                                                           TargetIdentityCheck targetCheck)
                                                       {
                                                           return targetCheck.Kind is TargetIdentityCheckKind.TargetChanged
                                                               or TargetIdentityCheckKind.ProcessReused
                                                               ? TargetMemoryOperationOutcome.Failed(
                                                                   EngineFailureKind.TargetIdentityMismatch)
                                                               : TargetMemoryOperationOutcome.Failed(
                                                                   EngineFailureKind.TargetIdentityUnavailable);
                                                       }
                                                   }
                                                   """;

    private ThrowawayConsumer(string directory, string projectPath, string assemblyPath)
    {
        Directory = directory;
        ProjectPath = projectPath;
        AssemblyPath = assemblyPath;
    }

    /// <summary>The consumer project's own directory.</summary>
    public string Directory { get; }

    /// <summary>Full path of the generated <c>.csproj</c>.</summary>
    public string ProjectPath { get; }

    /// <summary>
    ///     Where a Release build places the compiled plugin assembly. Only valid after <see cref="BuildAsync" /> has
    ///     succeeded.
    /// </summary>
    public string AssemblyPath { get; }

    /// <summary>The native protection bridge copied beside the built plugin.</summary>
    public string NativeBridgePath =>
        Path.Combine(Path.GetDirectoryName(AssemblyPath)!, "cheatengine-sdk-lua-bridge.dll");

    /// <summary>
    ///     Scaffolds a project named <paramref name="name" /> under <paramref name="parentDirectory" />: an
    ///     net10.0 class library with one <c>PackageReference</c> to <c>CheatEngine.SDK</c> restored only from
    ///     <paramref name="localFeedDirectory" /> (and nuget.org, for the .NET SDK's own implicit packages, from the
    ///     machine's warm cache), one minimal but valid plugin class, and whatever <paramref name="extraProperties" />
    ///     adds to its single <c>PropertyGroup</c>. When <paramref name="includeLuaFunction" /> is <see langword="true" />,
    ///     the project also declares one valid <c>[LuaFunction]</c> export. When <paramref name="includeLegacyAobConsumer" />
    ///     is <see langword="true" />, it compiles both historical <c>AobScanner.TryScan</c> overloads against the packed
    ///     SDK. When <paramref name="includeTargetBoundAllocationConsumer" /> is <see langword="true" />, it compiles an
    ///     independent implementation of the target-bound allocation backend seam against that package.
    ///     <paramref name="platformTarget" /> defaults to x64, but may be <see langword="null" /> to prove the package behavior
    ///     when the consumer does not declare it.
    /// </summary>
    public static ThrowawayConsumer Create(string parentDirectory, string name, string cheatEngineSdkVersion,
        string localFeedDirectory, string extraProperties = "", string? platformTarget = "x64",
        bool includeLuaFunction = false, bool includeLegacyAobConsumer = false,
        bool includeTargetBoundAllocationConsumer = false)
    {
        var directory = Path.Combine(parentDirectory, name);
        System.IO.Directory.CreateDirectory(directory);

        var projectPath = Path.Combine(directory, $"{name}.csproj");
        var platformTargetProperty = platformTarget is null
            ? ""
            : $"    <PlatformTarget>{platformTarget}</PlatformTarget>\n";
        File.WriteAllText(projectPath, $"""
                                        <Project Sdk="Microsoft.NET.Sdk">
                                          <PropertyGroup>
                                            <TargetFramework>net10.0</TargetFramework>
                                            <!-- The packaged target accepts an unset PlatformTarget, AnyCPU or x64; this ordinary scaffold defaults to x64. -->
                                        {platformTargetProperty}    <Nullable>enable</Nullable>
                                        {extraProperties}  </PropertyGroup>
                                          <ItemGroup>
                                            <PackageReference Include="{UmbrellaPackage.Id}" Version="{cheatEngineSdkVersion}" />
                                          </ItemGroup>
                                        </Project>
                                        """);

        File.WriteAllText(Path.Combine(directory, "Plugin.cs"), PluginSource);
        if (includeLuaFunction)
            File.WriteAllText(Path.Combine(directory, "Functions.cs"), LuaFunctionSource);
        if (includeLegacyAobConsumer)
            File.WriteAllText(Path.Combine(directory, "LegacyAobConsumer.cs"), LegacyAobSource);
        if (includeTargetBoundAllocationConsumer)
            File.WriteAllText(Path.Combine(directory, "TargetBoundAllocationBackend.cs"), TargetBoundAllocationSource);
        // <clear/>: this consumer's restore must depend only on the two sources named here, never on whatever
        // machine- or user-level NuGet.Config the CI/dev box happens to carry (same reasoning as the repo's own
        // root nuget.config).
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), $"""
                                                                    <?xml version="1.0" encoding="utf-8"?>
                                                                    <configuration>
                                                                      <packageSources>
                                                                        <clear />
                                                                        <add key="cheatengine-sdk-local" value="{localFeedDirectory}" />
                                                                        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                                                                      </packageSources>
                                                                    </configuration>
                                                                    """);

        var assemblyPath = Path.Combine(directory, "bin", "Release", "net10.0", $"{name}.dll");
        return new ThrowawayConsumer(directory, projectPath, assemblyPath);
    }

    /// <summary>
    ///     Scaffolds a valid plugin that reaches the umbrella package only through a packed relay package. The source
    ///     intentionally remains the same as a direct consumer: package compile references must still flow, while the
    ///     package's direct-only build assets must not.
    /// </summary>
    public static ThrowawayConsumer CreateIndirect(string parentDirectory, string carrierPackageId,
        string carrierPackageVersion, string localFeedDirectory)
    {
        const string consumerName = "IndirectConsumer";
        var directory = Path.Combine(parentDirectory, consumerName);
        System.IO.Directory.CreateDirectory(directory);

        var projectPath = Path.Combine(directory, $"{consumerName}.csproj");
        File.WriteAllText(projectPath, $"""
                                        <Project Sdk="Microsoft.NET.Sdk">
                                          <PropertyGroup>
                                            <TargetFramework>net10.0</TargetFramework>
                                            <PlatformTarget>x64</PlatformTarget>
                                            <Nullable>enable</Nullable>
                                          </PropertyGroup>
                                          <ItemGroup>
                                            <PackageReference Include="{carrierPackageId}" Version="{carrierPackageVersion}" />
                                          </ItemGroup>
                                        </Project>
                                        """);
        File.WriteAllText(Path.Combine(directory, "Plugin.cs"), PluginSource);
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), $"""
                                                                    <?xml version="1.0" encoding="utf-8"?>
                                                                    <configuration>
                                                                      <packageSources>
                                                                        <clear />
                                                                        <add key="cheatengine-sdk-local" value="{localFeedDirectory}" />
                                                                        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                                                                      </packageSources>
                                                                    </configuration>
                                                                    """);

        var assemblyPath = Path.Combine(directory, "bin", "Release", "net10.0", $"{consumerName}.dll");
        return new ThrowawayConsumer(directory, projectPath, assemblyPath);
    }

    /// <summary>
    ///     Restores from the local feed only (see <see cref="Create" />); no other consumer step restores again.
    ///     <paramref name="packagesDirectory" /> is passed as <c>--packages</c> so extraction lands in a directory the
    ///     caller controls, never the machine-wide global-packages folder: NuGet treats a given package id+version as
    ///     immutable once extracted there, so a stale extraction left by an earlier run (this fixture, a developer's own
    ///     restore, or another parallel build) would otherwise be reused silently even though this run's
    ///     <c>dotnet pack</c> produced different content under the same MinVer-derived version (see
    ///     <c>PackagedUmbrellaFixture</c>'s own remarks).
    /// </summary>
    public Task<ProcessResult> RestoreAsync(TimeSpan timeout, string packagesDirectory)
    {
        return ProcessRunner.RunAsync(
            "dotnet",
            $"restore \"{ProjectPath}\" --configfile \"{Path.Combine(Directory, "NuGet.Config")}\" --packages \"{packagesDirectory}\" --nologo",
            Directory,
            timeout);
    }

    /// <summary>A real Release build: what produces <see cref="AssemblyPath" /> and runs the packaged generators/analyzers.</summary>
    public Task<ProcessResult> BuildAsync(TimeSpan timeout)
    {
        return ProcessRunner.RunAsync("dotnet", $"build \"{ProjectPath}\" -c Release --no-restore --nologo", Directory,
            timeout);
    }

    /// <summary>Cleans the consumer output without restoring, so the following build validates normal SDK copy bookkeeping.</summary>
    public Task<ProcessResult> CleanAsync(TimeSpan timeout)
    {
        return ProcessRunner.RunAsync("dotnet", $"clean \"{ProjectPath}\" -c Release --nologo", Directory,
            timeout);
    }

    /// <summary>Publishes the consumer into <paramref name="outputDirectory" /> without restoring again.</summary>
    public Task<ProcessResult> PublishAsync(TimeSpan timeout, string outputDirectory)
    {
        return ProcessRunner.RunAsync("dotnet",
            $"publish \"{ProjectPath}\" -c Release --no-restore --nologo -o \"{outputDirectory}\"", Directory,
            timeout);
    }

    /// <summary>
    ///     Evaluates (does not build: no <c>-target</c>, per the MSBuild command-line reference) the named MSBuild
    ///     properties after restore, exactly as the packaged <c>build/CheatEngine.SDK.props</c> and this project's own
    ///     <c>PropertyGroup</c> leave them.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(TimeSpan timeout,
        params string[] propertyNames)
    {
        var switches = string.Join(' ', propertyNames.Select(static p => $"-getProperty:{p}"));
        var result = await ProcessRunner.RunAsync("dotnet",
                $"build \"{ProjectPath}\" -c Release --no-restore --nologo {switches}", Directory, timeout)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet build -getProperty' failed for '{ProjectPath}' (exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}):{Environment.NewLine}{result.CombinedOutput}");

        Dictionary<string, string> values = new(StringComparer.Ordinal);

        // MSBuild's own documented split: "-getProperty to request a single property" emits a bare string;
        // several properties (this project always requests at least one, so >= 2 here) emit one JSON object.
        if (propertyNames.Length == 1)
        {
            values[propertyNames[0]] = result.StandardOutput.Trim();
            return values;
        }

        var jsonStart = result.StandardOutput.AsSpan().IndexOf('{');
        if (jsonStart < 0)
            throw new InvalidOperationException(
                $"'dotnet build -getProperty' for '{ProjectPath}' produced no JSON on standard output:{Environment.NewLine}{result.CombinedOutput}");

        using var document = JsonDocument.Parse(result.StandardOutput[jsonStart..]);
        var properties = document.RootElement.GetProperty("Properties");
        foreach (var name in propertyNames)
            values[name] = properties.TryGetProperty(name, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;

        return values;
    }
}
