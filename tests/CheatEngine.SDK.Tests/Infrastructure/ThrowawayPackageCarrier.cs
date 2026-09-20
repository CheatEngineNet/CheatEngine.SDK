namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     A temporary package which directly depends on the packed umbrella. It is intentionally an ordinary dependency,
///     not a project reference or a <c>PrivateAssets</c> trick, so a second consumer observes real NuGet asset flow.
/// </summary>
internal sealed class ThrowawayPackageCarrier
{
    /// <summary>The fixed identity is unique within one fixture-local feed.</summary>
    public const string PackageId = "CheatEngine.SDK.PackageAssetCarrier";

    /// <summary>The fixture controls the feed, so a stable test-only version is sufficient.</summary>
    public const string PackageVersion = "1.0.0";

    private ThrowawayPackageCarrier(string directory, string projectPath)
    {
        Directory = directory;
        ProjectPath = projectPath;
    }

    /// <summary>The carrier project's temporary directory.</summary>
    public string Directory { get; }

    /// <summary>The carrier project that is restored and packed into the fixture-local feed.</summary>
    public string ProjectPath { get; }

    /// <summary>Creates the carrier package project and its isolated NuGet configuration.</summary>
    public static ThrowawayPackageCarrier Create(string parentDirectory, string cheatEngineSdkVersion,
        string localFeedDirectory)
    {
        var directory = Path.Combine(parentDirectory, "PackageAssetCarrier");
        System.IO.Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "PackageAssetCarrier.csproj");
        File.WriteAllText(projectPath, $"""
                                        <Project Sdk="Microsoft.NET.Sdk">
                                          <PropertyGroup>
                                            <TargetFramework>net10.0</TargetFramework>
                                            <PackageId>{PackageId}</PackageId>
                                            <Version>{PackageVersion}</Version>
                                            <Nullable>enable</Nullable>
                                          </PropertyGroup>
                                          <ItemGroup>
                                            <PackageReference Include="{UmbrellaPackage.Id}" Version="{cheatEngineSdkVersion}" />
                                          </ItemGroup>
                                        </Project>
                                        """);
        File.WriteAllText(Path.Combine(directory, "PackageBoundary.cs"), """
                                                                    namespace PackageAssetCarrier;

                                                                    public sealed class PackageBoundary
                                                                    {
                                                                    }
                                                                    """);
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
        return new ThrowawayPackageCarrier(directory, projectPath);
    }

    /// <summary>Restores the carrier package from the fixture-local feed.</summary>
    public Task<ProcessResult> RestoreAsync(TimeSpan timeout, string packagesDirectory)
    {
        return ProcessRunner.RunAsync("dotnet",
            $"restore \"{ProjectPath}\" --configfile \"{Path.Combine(Directory, "NuGet.Config")}\" --packages \"{packagesDirectory}\" --nologo",
            Directory, timeout);
    }

    /// <summary>Packs the already-restored carrier into <paramref name="feedDirectory" />.</summary>
    public Task<ProcessResult> PackAsync(TimeSpan timeout, string feedDirectory)
    {
        return ProcessRunner.RunAsync("dotnet",
            $"pack \"{ProjectPath}\" -c Release --no-restore -o \"{feedDirectory}\" --nologo", Directory, timeout);
    }
}
