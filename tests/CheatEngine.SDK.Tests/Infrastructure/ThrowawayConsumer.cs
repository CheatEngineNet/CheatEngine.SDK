using System.Globalization;
using System.Text;

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

    // This is a standalone native-Lua proof, not a Cheat Engine host integration. It binds only the fixture-provided
    // Lua 5.3 module, creates one state it owns and supplies that state through a short-lived SDK runtime binding so
    // generated [LuaGlobal] bodies and ownership-aware generated [LuaFunction] registration both run for real.
    private const string RuntimeProgramSource = """"
                                                using System;
                                                using System.Runtime.CompilerServices;
                                                using System.Runtime.InteropServices;
                                                using CheatEngine.SDK.Annotations.Lua;
                                                using CheatEngine.SDK.Lua.Calls;
                                                using CheatEngine.SDK.Lua.Interop.Api;
                                                using CheatEngine.SDK.Lua.Interop.Types;
                                                using CheatEngine.SDK.Lua.Marshalling;
                                                using CheatEngine.SDK.Lua.Registration;
                                                using CheatEngine.SDK.Lua.Runtime;
                                                using CheatEngine.SDK.Lua.State;

                                                namespace ThrowawayRuntime;

                                                internal readonly struct FixtureToken<T>
                                                {
                                                    public FixtureToken(long value)
                                                    {
                                                        Value = value;
                                                    }

                                                    public long Value { get; }
                                                }

                                                internal readonly struct FixtureTokenMarshaller<T> : ILuaMarshaller<FixtureToken<T>>
                                                {
                                                    public static void Push(LuaState state, FixtureToken<T> value)
                                                    {
                                                        state.PushInteger(value.Value);
                                                    }

                                                    public static bool TryRead(LuaState state, int index, out FixtureToken<T> value)
                                                    {
                                                        if (state.TryReadInteger(index, out var number))
                                                        {
                                                            value = new FixtureToken<T>(number);
                                                            return true;
                                                        }

                                                        value = default;
                                                        return false;
                                                    }
                                                }

                                                internal static partial class GeneratedGlobals
                                                {
                                                    [LuaGlobal("sdk022_increment")]
                                                    public static partial LuaOperationStatus TryIncrement(
                                                        LuaState state,
                                                        [LuaMarshaller(typeof(FixtureTokenMarshaller<int>))] FixtureToken<int> value,
                                                        [LuaMarshaller(typeof(FixtureTokenMarshaller<int>))] out FixtureToken<int> result);

                                                    [LuaGlobal("sdk022_fail")]
                                                    public static partial LuaOperationStatus TryFail(LuaState state);
                                                }

                                                internal static partial class GeneratedFunctions
                                                {
                                                    [LuaFunction("sdk022_callback")]
                                                    [return: LuaMarshaller(typeof(FixtureTokenMarshaller<int>))]
                                                    public static FixtureToken<int> Increment(
                                                        [LuaMarshaller(typeof(FixtureTokenMarshaller<int>))] FixtureToken<int> value)
                                                    {
                                                        return new FixtureToken<int>(value.Value + 1);
                                                    }
                                                }

                                                internal static unsafe class Program
                                                {
                                                    private static nint s_state;

                                                    public static int Main(string[] args)
                                                    {
                                                        if (args.Length != 1)
                                                        {
                                                            Console.Error.WriteLine("Expected exactly one Lua 5.3 DLL path.");
                                                            return 64;
                                                        }

                                                        try
                                                        {
                                                            Run(args[0]);
                                                            return 0;
                                                        }
                                                        catch (Exception exception)
                                                        {
                                                            Console.Error.WriteLine(exception);
                                                            return 1;
                                                        }
                                                    }

                                                    private static void Run(string luaLibraryPath)
                                                    {
                                                        ArgumentException.ThrowIfNullOrWhiteSpace(luaLibraryPath);

                                                        // LuaApi retains raw function pointers for the process lifetime, so this loaded fixture module
                                                        // intentionally remains loaded until process exit.
                                                        LuaApi.Initialize(NativeLibrary.Load(luaLibraryPath));
                                                        lua_State* statePointer = LuaApi.luaL_newstate();
                                                        if (statePointer is null)
                                                            throw new InvalidOperationException("The Lua fixture could not create a state.");

                                                        try
                                                        {
                                                            LuaApi.luaL_openlibs(statePointer);
                                                            LuaState state = new((nint)statePointer);
                                                            s_state = state.Handle;
                                                            delegate* unmanaged[Stdcall]<void*> stateProvider = &ProvideState;
                                                            LuaHostBinding binding = new((nint)stateProvider, 0,
                                                                Environment.CurrentManagedThreadId);
                                                            LuaRuntime.Attach(in binding);
                                                            try
                                                            {
                                                                VerifyGeneratedBindings(state);
                                                            }
                                                            finally
                                                            {
                                                                LuaRuntime.Detach();
                                                                s_state = 0;
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            LuaApi.lua_close(statePointer);
                                                        }
                                                    }

                                                    private static void VerifyGeneratedBindings(LuaState state)
                                                    {
                                                        Execute(state, """
                                                                       function sdk022_increment(value)
                                                                         return value + 1
                                                                       end
                                                                       function sdk022_fail()
                                                                         error("sdk-022 fixture failure")
                                                                       end
                                                                       sdk022_callback = function(value)
                                                                         return -1
                                                                       end
                                                                       """u8);

                                                        LuaOperationStatus global = GeneratedGlobals.TryIncrement(state,
                                                            new FixtureToken<int>(41), out var globalResult);
                                                        if (!global.IsSuccess || globalResult.Value != 42)
                                                            throw new InvalidOperationException("The generated Lua global did not marshal its generic token.");
                                                        Console.WriteLine("SDK-022-RUNTIME-GLOBAL-MARSHALLER");

                                                        LuaRegistrationResult collision = GeneratedFunctions.TryRegisterLuaFunctions(state,
                                                            LuaRegistrationCollisionPolicy.RejectExisting);
                                                        if (collision.Kind != LuaRegistrationResultKind.Collision || collision.Lease is not null)
                                                            throw new InvalidOperationException("Generated registration did not reject the existing callback global.");

                                                        LuaRegistrationResult registration = GeneratedFunctions.TryRegisterLuaFunctions(state,
                                                            LuaRegistrationCollisionPolicy.ReplaceExisting);
                                                        LuaRegistrationLease lease = registration.Lease
                                                            ?? throw new InvalidOperationException("Generated registration did not return its ownership lease.");
                                                        if (!registration.IsSuccess)
                                                            throw new InvalidOperationException("Generated registration did not replace the callback global.");

                                                        if (ExecuteForInteger(state, "return sdk022_callback(41)"u8) != 42)
                                                            throw new InvalidOperationException("The generated Lua callback did not marshal its generic token.");
                                                        Console.WriteLine("SDK-022-RUNTIME-CALLBACK-MARSHALLER");

                                                        LuaRegistrationReleaseOutcome released = lease.ReleaseWithOutcome(state);
                                                        if (released.Kind != LuaRegistrationReleaseKind.Released || released.RestoredCount != 1 ||
                                                            ExecuteForInteger(state, "return sdk022_callback(41)"u8) != -1)
                                                            throw new InvalidOperationException("The generated registration lease did not restore the prior callback.");
                                                        Console.WriteLine("SDK-022-RUNTIME-COLLISION-LEASE");

                                                        LuaOperationStatus failure = GeneratedGlobals.TryFail(state);
                                                        if (failure.Kind != LuaOperationStatusKind.LuaFailure || failure.LuaStatus != LuaStatus.RuntimeError)
                                                            throw new InvalidOperationException("The generated Lua global did not preserve the runtime-error status.");
                                                        Console.WriteLine("SDK-022-RUNTIME-LUA-RUNTIME-ERROR");
                                                        Console.WriteLine("SDK-022-RUNTIME-PROOF");
                                                    }

                                                    private static void Execute(LuaState state, ReadOnlySpan<byte> source)
                                                    {
                                                        using LuaFrame frame = new(state);
                                                        LuaStatus status = state.TryExecute(source, 0, "=sdk022-runtime"u8);
                                                        if (!status.IsOk)
                                                            throw new InvalidOperationException("The Lua fixture setup failed with " + status + ".");
                                                    }

                                                    private static long ExecuteForInteger(LuaState state, ReadOnlySpan<byte> source)
                                                    {
                                                        using LuaFrame frame = new(state);
                                                        LuaStatus status = state.TryExecute(source, 1, "=sdk022-runtime"u8);
                                                        if (!status.IsOk || !state.TryReadInteger(-1, out var value))
                                                            throw new InvalidOperationException("The Lua fixture did not return the expected integer.");

                                                        return value;
                                                    }

                                                    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
                                                    private static void* ProvideState()
                                                    {
                                                        return (void*)s_state;
                                                    }
                                                }
                                                """";

    private const string AotProgramSource = """
                                            using System;
                                            using CheatEngine.SDK.Annotations.Lua;
                                            using CheatEngine.SDK.Lua.Calls;
                                            using CheatEngine.SDK.Lua.State;

                                            namespace ThrowawayAot;

                                            internal static partial class GeneratedAotBinding
                                            {
                                                [LuaGlobal("sdk022_aot_probe")]
                                                public static partial LuaOperationStatus TryProbe(LuaState state);
                                            }

                                            internal static class Program
                                            {
                                                public static int Main(string[] args)
                                                {
                                                    // Native AOT keeps this generated body because the non-default command-line
                                                    // path references it, while the normal no-host test run never invokes it.
                                                    if (args.Length != 0)
                                                        _ = GeneratedAotBinding.TryProbe(default);

                                                    Console.WriteLine("SDK-022-AOT-STANDALONE");
                                                    Console.WriteLine("SDK-022-AOT-NO-CE-HOST");
                                                    return 0;
                                                }
                                            }
                                            """;

    private const string DuplicateLuaFunctionProgramSource = """
                                                             using CheatEngine.SDK.Annotations.Lua;

                                                             namespace ThrowawayDuplicate;

                                                             internal static partial class DuplicateFunctions
                                                             {
                                                                 [LuaFunction("sdk022_duplicate")]
                                                                 public static int First() => 1;

                                                                 [LuaFunction("sdk022_duplicate")]
                                                                 public static int Second() => 2;
                                                             }

                                                             internal static class Program
                                                             {
                                                                 public static int Main() => 0;
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
    ///     net10.0 class library with one <c>PackageReference</c> to <c>CheatEngine.SDK</c>. Its generated
    ///     <c>NuGet.Config</c> maps that exact package identity to <paramref name="localFeedDirectory" /> while retaining
    ///     nuget.org for other package identities. The project has one minimal but valid plugin class and whatever
    ///     <paramref name="extraProperties" /> adds to its single <c>PropertyGroup</c>. When
    ///     <paramref name="includeLuaFunction" /> is <see langword="true" />,
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
        WriteNuGetConfig(directory, localFeedDirectory, UmbrellaPackage.Id);

        var assemblyPath = Path.Combine(directory, "bin", "Release", "net10.0", $"{name}.dll");
        return new ThrowawayConsumer(directory, projectPath, assemblyPath);
    }

    /// <summary>
    ///     Scaffolds a package-only executable that runs generated Lua bindings against exactly one fixture-supplied
    ///     Lua 5.3 DLL. <paramref name="extraProperties" /> is required so the fixture, rather than this scaffold,
    ///     explicitly opts into the unsafe compilation generated <c>[LuaFunction]</c> thunks require.
    /// </summary>
    public static ThrowawayConsumer CreateRuntimeExecutable(string parentDirectory, string name,
        string cheatEngineSdkVersion, string localFeedDirectory, string extraProperties)
    {
        return CreateExecutable(parentDirectory, name, cheatEngineSdkVersion, localFeedDirectory, extraProperties,
            "Program.cs", RuntimeProgramSource);
    }

    /// <summary>
    ///     Scaffolds the intentional CESDK2005 consumer: two otherwise valid generated Lua functions share one Lua
    ///     global name. <paramref name="extraProperties" /> must explicitly enable unsafe code so CESDK2001 does not
    ///     mask that duplicate-name diagnostic.
    /// </summary>
    public static ThrowawayConsumer CreateInvalidDuplicateLuaFunctionConsumer(string parentDirectory, string name,
        string cheatEngineSdkVersion, string localFeedDirectory, string extraProperties)
    {
        return CreateExecutable(parentDirectory, name, cheatEngineSdkVersion, localFeedDirectory, extraProperties,
            "Program.cs", DuplicateLuaFunctionProgramSource);
    }

    /// <summary>
    ///     Scaffolds a package-only executable whose source includes a generated Lua binding but whose program neither
    ///     loads a native Lua module nor activates a Cheat Engine host. The fixture supplies trim/AOT/RID properties.
    /// </summary>
    public static ThrowawayConsumer CreateAotExecutable(string parentDirectory, string name, string cheatEngineSdkVersion,
        string localFeedDirectory, string extraProperties)
    {
        return CreateExecutable(parentDirectory, name, cheatEngineSdkVersion, localFeedDirectory, extraProperties,
            "Program.cs", AotProgramSource);
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
        WriteNuGetConfig(directory, localFeedDirectory, carrierPackageId, UmbrellaPackage.Id);

        var assemblyPath = Path.Combine(directory, "bin", "Release", "net10.0", $"{consumerName}.dll");
        return new ThrowawayConsumer(directory, projectPath, assemblyPath);
    }

    private static ThrowawayConsumer CreateExecutable(string parentDirectory, string name, string cheatEngineSdkVersion,
        string localFeedDirectory, string extraProperties, string sourceFileName, string source)
    {
        var directory = Path.Combine(parentDirectory, name);
        System.IO.Directory.CreateDirectory(directory);

        var projectPath = Path.Combine(directory, $"{name}.csproj");
        File.WriteAllText(projectPath, $"""
                                        <Project Sdk="Microsoft.NET.Sdk">
                                          <PropertyGroup>
                                            <TargetFramework>net10.0</TargetFramework>
                                            <OutputType>Exe</OutputType>
                                            <Nullable>enable</Nullable>
                                        {extraProperties}  </PropertyGroup>
                                          <ItemGroup>
                                            <PackageReference Include="{UmbrellaPackage.Id}" Version="{cheatEngineSdkVersion}" />
                                          </ItemGroup>
                                        </Project>
                                        """);
        File.WriteAllText(Path.Combine(directory, sourceFileName), source);
        WriteNuGetConfig(directory, localFeedDirectory, UmbrellaPackage.Id);

        var assemblyPath = Path.Combine(directory, "bin", "Release", "net10.0", $"{name}.dll");
        return new ThrowawayConsumer(directory, projectPath, assemblyPath);
    }

    // <clear/> isolates restore from arbitrary user/machine configuration. The exact CheatEngine.SDK mapping keeps
    // package restore on this fixture's freshly packed feed; nuget.org remains only for external SDK dependencies.
    private static void WriteNuGetConfig(string directory, string localFeedDirectory, params string[] localPackageIds)
    {
        StringBuilder packageMappings = new();
        foreach (var packageId in localPackageIds)
        {
            packageMappings.Append("          <package pattern=\"");
            packageMappings.Append(packageId);
            packageMappings.Append("\" />\n");
        }

        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), $"""
                                                                    <?xml version="1.0" encoding="utf-8"?>
                                                                    <configuration>
                                                                      <packageSources>
                                                                        <clear />
                                                                        <add key="cheatengine-sdk-local" value="{localFeedDirectory}" />
                                                                        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                                                                      </packageSources>
                                                                      <packageSourceMapping>
                                                                        <clear />
                                                                        <packageSource key="cheatengine-sdk-local">
                                                                    {packageMappings}        </packageSource>
                                                                        <packageSource key="nuget.org">
                                                                          <package pattern="*" />
                                                                        </packageSource>
                                                                      </packageSourceMapping>
                                                                    </configuration>
                                                                    """);
    }

    /// <summary>
    ///     Restores the package identities mapped to the local feed (see <see cref="Create" />); no other consumer step
    ///     restores again. Nuget.org remains available only for package identities not mapped to that feed.
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
            $"restore \"{ProjectPath}\" --configfile \"{Path.Combine(Directory, "NuGet.Config")}\" --packages \"{packagesDirectory}\" --no-http-cache --force-evaluate --nologo",
            Directory,
            timeout);
    }

    /// <summary>A real Release build: what produces <see cref="AssemblyPath" /> and runs the packaged generators/analyzers.</summary>
    public Task<ProcessResult> BuildAsync(TimeSpan timeout)
    {
        return ProcessRunner.RunAsync("dotnet", $"build \"{ProjectPath}\" -c Release --no-restore --nologo", Directory,
            timeout);
    }

    /// <summary>Runs the built executable with its required one argument: the test fixture's Lua 5.3 DLL path.</summary>
    public Task<ProcessResult> RunAsync(string luaLibraryPath, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(luaLibraryPath);
        return ProcessRunner.RunAsync("dotnet", $"\"{AssemblyPath}\" \"{luaLibraryPath}\"", Directory, timeout);
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

    /// <summary>Runs the native executable emitted by a publish into <paramref name="outputDirectory" />.</summary>
    public Task<ProcessResult> RunPublishedAsync(string outputDirectory, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var executableName = Path.GetFileNameWithoutExtension(ProjectPath) + ".exe";
        var executablePath = Path.Combine(outputDirectory, executableName);
        return ProcessRunner.RunAsync(executablePath, "", outputDirectory, timeout);
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
