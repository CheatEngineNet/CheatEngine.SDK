using System.Text;
using System.Xml;
using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Architecture;

/// <summary>
///     Guards the project-file dependency direction that keeps the SDK independent from its public Client and MCP layers.
///     These tests intentionally inspect the checked-in MSBuild graph instead of a restored dependency graph, so an
///     invalid
///     edge is reported before it can be hidden by transitive assets.
/// </summary>
public sealed class ProjectDependencyDirectionTests
{
    private const string UmbrellaProject = "src/CheatEngine.SDK/CheatEngine.SDK.csproj";

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedLibraryRuntimeDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["libs/CheatEngine.SDK.Abi/CheatEngine.SDK.Abi.csproj"] = [],
            ["libs/CheatEngine.SDK.Annotations/CheatEngine.SDK.Annotations.csproj"] = [],
            ["libs/CheatEngine.SDK.Engine/CheatEngine.SDK.Engine.csproj"] =
            [
                "libs/CheatEngine.SDK.Annotations/CheatEngine.SDK.Annotations.csproj",
                "libs/CheatEngine.SDK.Lua.Interop/CheatEngine.SDK.Lua.Interop.csproj",
                "libs/CheatEngine.SDK.Lua/CheatEngine.SDK.Lua.csproj",
            ],
            ["libs/CheatEngine.SDK.Hosting/CheatEngine.SDK.Hosting.csproj"] =
            [
                "libs/CheatEngine.SDK.Abi/CheatEngine.SDK.Abi.csproj",
                "libs/CheatEngine.SDK.Annotations/CheatEngine.SDK.Annotations.csproj",
                "libs/CheatEngine.SDK.Lua.Interop/CheatEngine.SDK.Lua.Interop.csproj",
                "libs/CheatEngine.SDK.Lua/CheatEngine.SDK.Lua.csproj",
            ],
            ["libs/CheatEngine.SDK.Lua.Interop/CheatEngine.SDK.Lua.Interop.csproj"] = [],
            ["libs/CheatEngine.SDK.Lua/CheatEngine.SDK.Lua.csproj"] =
            [
                "libs/CheatEngine.SDK.Annotations/CheatEngine.SDK.Annotations.csproj",
                "libs/CheatEngine.SDK.Lua.Interop/CheatEngine.SDK.Lua.Interop.csproj",
            ],
        };

    [Fact]
    public void Every_SDK_project_has_a_sibling_README()
    {
        List<string> missingReadmes = [];

        foreach (var projectPath in EnumerateRepositoryFiles("*.csproj"))
        {
            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (projectDirectory is null)
            {
                missingReadmes.Add(GetRepositoryRelativePath(projectPath));
                continue;
            }

            if (!File.Exists(Path.Combine(projectDirectory, "README.md")))
                missingReadmes.Add(GetRepositoryRelativePath(projectPath));
        }

        AssertNoViolations("Every SDK project must have a sibling README.md.", missingReadmes);
    }

    [Fact]
    public void Shipping_libraries_keep_the_declared_lower_layer_runtime_graph()
    {
        List<string> violations = [];
        HashSet<string> discoveredLibraryProjects = new(StringComparer.Ordinal);

        foreach (var projectPath in EnumerateRepositoryFiles("*.csproj"))
        {
            var projectRelativePath = GetRepositoryRelativePath(projectPath);
            if (!projectRelativePath.StartsWith("libs/", StringComparison.Ordinal))
                continue;

            discoveredLibraryProjects.Add(projectRelativePath);
            if (!ExpectedLibraryRuntimeDependencies.TryGetValue(projectRelativePath, out var expectedDependencies))
            {
                violations.Add($"{projectRelativePath}: is not declared in the shipping library graph.");
                continue;
            }

            SortedSet<string> actualDependencies = new(StringComparer.Ordinal);
            foreach (var reference in ReadProjectReferences(projectPath))
            {
                if (IsRoslynComponent(reference.TargetRelativePath))
                    continue;

                actualDependencies.Add(reference.TargetRelativePath);
            }

            AddSetDifference(violations, projectRelativePath, "unexpected runtime dependency", actualDependencies,
                expectedDependencies);
            AddSetDifference(violations, projectRelativePath, "missing runtime dependency", expectedDependencies,
                actualDependencies);
        }

        foreach (var expectedProject in ExpectedLibraryRuntimeDependencies.Keys)
            if (!discoveredLibraryProjects.Contains(expectedProject))
                violations.Add($"{expectedProject}: expected shipping library project was not found.");

        AssertNoViolations("Shipping libraries must reference only their declared lower-layer runtime dependencies.",
            violations);
    }

    [Fact]
    public void Umbrella_runtime_references_are_limited_to_shipping_libraries()
    {
        var umbrellaPath = RepositoryLayout.PathOf(UmbrellaProject);
        List<string> violations = [];

        foreach (var reference in ReadProjectReferences(umbrellaPath))
        {
            if (IsRoslynComponent(reference.TargetRelativePath))
                continue;

            if (!reference.TargetRelativePath.StartsWith("libs/", StringComparison.Ordinal))
                violations.Add(
                    $"{UmbrellaProject}: runtime ProjectReference '{reference.Include}' targets '{reference.TargetRelativePath}', not libs/.");
        }

        AssertNoViolations("The umbrella package may compose lower shipping libraries but not a higher layer.",
            violations);
    }

    [Fact]
    public void Shipping_projects_consume_Roslyn_components_as_compile_time_only_inputs()
    {
        List<string> violations = [];

        foreach (var projectPath in EnumerateRepositoryFiles("*.csproj"))
        {
            var projectRelativePath = GetRepositoryRelativePath(projectPath);
            if (!IsShippingProject(projectRelativePath))
                continue;

            foreach (var reference in ReadProjectReferences(projectPath))
            {
                if (!IsRoslynComponent(reference.TargetRelativePath))
                    continue;

                if (!string.Equals(reference.OutputItemType, "Analyzer", StringComparison.Ordinal))
                    violations.Add(
                        $"{projectRelativePath}: Roslyn component '{reference.TargetRelativePath}' must set OutputItemType=\"Analyzer\".");

                if (!string.Equals(reference.ReferenceOutputAssembly, "false", StringComparison.OrdinalIgnoreCase))
                    violations.Add(
                        $"{projectRelativePath}: Roslyn component '{reference.TargetRelativePath}' must set ReferenceOutputAssembly=\"false\".");
            }
        }

        AssertNoViolations(
            "Shipping projects must consume analyzers and generators without runtime assembly references.",
            violations);
    }

    [Fact]
    public void Hosting_applies_the_SDK_analyzer_without_a_runtime_reference()
    {
        const string hostingProject = "libs/CheatEngine.SDK.Hosting/CheatEngine.SDK.Hosting.csproj";
        const string analyzerProject = "analyzers/CheatEngine.SDK.Analyzers/CheatEngine.SDK.Analyzers.csproj";
        List<string> violations = [];
        var foundAnalyzer = false;

        foreach (var reference in ReadProjectReferences(RepositoryLayout.PathOf(hostingProject)))
        {
            if (!string.Equals(reference.TargetRelativePath, analyzerProject, StringComparison.Ordinal))
                continue;

            foundAnalyzer = true;
            if (!string.Equals(reference.OutputItemType, "Analyzer", StringComparison.Ordinal))
                violations.Add($"{hostingProject}: SDK analyzer must set OutputItemType=\"Analyzer\".");

            if (!string.Equals(reference.ReferenceOutputAssembly, "false", StringComparison.OrdinalIgnoreCase))
                violations.Add($"{hostingProject}: SDK analyzer must set ReferenceOutputAssembly=\"false\".");
        }

        if (!foundAnalyzer)
            violations.Add($"{hostingProject}: expected compile-time analyzer '{analyzerProject}' was not found.");

        AssertNoViolations("Hosting must compile with the SDK analyzer and never reference it at runtime.", violations);
    }

    [Fact]
    public void SDK_build_metadata_has_no_Client_or_Mcp_dependency()
    {
        List<string> violations = [];

        foreach (var metadataPath in EnumerateBuildMetadataFiles())
        {
            var document = LoadProjectDocument(metadataPath);
            var nodes = document.SelectNodes("//*[@Include or @Update or @Remove]");
            if (nodes is null)
                continue;

            foreach (XmlNode node in nodes)
            {
                AddForbiddenDependencyViolations(violations, metadataPath, node, "Include");
                AddForbiddenDependencyViolations(violations, metadataPath, node, "Update");
                AddForbiddenDependencyViolations(violations, metadataPath, node, "Remove");
            }
        }

        foreach (var projectPath in EnumerateRepositoryFiles("*.csproj"))
        foreach (var reference in ReadProjectReferences(projectPath))
        {
            if (reference.Include.Contains("$(", StringComparison.Ordinal))
                violations.Add(
                    $"{GetRepositoryRelativePath(projectPath)}: ProjectReference '{reference.Include}' is dynamic and cannot be checked for a higher-layer dependency.");

            if (reference.TargetRelativePath.StartsWith("../", StringComparison.Ordinal))
                violations.Add(
                    $"{GetRepositoryRelativePath(projectPath)}: ProjectReference '{reference.Include}' escapes the SDK repository.");
        }

        AssertNoViolations("The SDK build graph must not take a Client or MCP dependency.", violations);
    }

    private static void AddForbiddenDependencyViolations(List<string> violations, string metadataPath, XmlNode node,
        string attributeName)
    {
        var value = GetAttribute(node, attributeName);
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Contains("CheatEngine.Client", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("CheatEngine.Mcp", StringComparison.OrdinalIgnoreCase))
            violations.Add(
                $"{GetRepositoryRelativePath(metadataPath)}: {node.LocalName} {attributeName}='{value}' references a higher layer.");
    }

    private static void AddSetDifference(List<string> violations, string projectRelativePath, string violationName,
        IEnumerable<string> source, IEnumerable<string> valuesToRemove)
    {
        HashSet<string> valuesToRemoveSet = new(valuesToRemove, StringComparer.Ordinal);
        foreach (var value in source)
            if (!valuesToRemoveSet.Contains(value))
                violations.Add($"{projectRelativePath}: {violationName} '{value}'.");
    }

    private static void AssertNoViolations(string expectation, List<string> violations)
    {
        if (violations.Count == 0)
            return;

        StringBuilder message = new(expectation);
        message.AppendLine();
        for (var index = 0; index < violations.Count; index++)
            message.Append(" - ").AppendLine(violations[index]);

        Assert.Fail(message.ToString());
    }

    private static IEnumerable<string> EnumerateBuildMetadataFiles()
    {
        foreach (var projectPath in EnumerateRepositoryFiles("*.csproj"))
            yield return projectPath;

        foreach (var propsPath in EnumerateRepositoryFiles("*.props"))
            yield return propsPath;

        foreach (var targetsPath in EnumerateRepositoryFiles("*.targets"))
            yield return targetsPath;

        foreach (var solutionPath in EnumerateRepositoryFiles("*.slnx"))
            yield return solutionPath;
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string searchPattern)
    {
        foreach (var path in
                 Directory.EnumerateFiles(RepositoryLayout.Root, searchPattern, SearchOption.AllDirectories))
        {
            var relativePath = GetRepositoryRelativePath(path);
            if (!IsGeneratedPath(relativePath))
                yield return path;
        }
    }

    private static string GetAttribute(XmlNode node, string attributeName)
    {
        var attribute = node.Attributes?[attributeName];
        return attribute?.Value ?? string.Empty;
    }

    private static string GetRepositoryRelativePath(string fullPath)
    {
        return Path.GetRelativePath(RepositoryLayout.Root, fullPath).Replace('\\', '/');
    }

    private static bool IsGeneratedPath(string relativePath)
    {
        return relativePath.StartsWith("artifacts/", StringComparison.Ordinal) ||
               relativePath.Contains("/bin/", StringComparison.Ordinal) ||
               relativePath.Contains("/obj/", StringComparison.Ordinal);
    }

    private static bool IsRoslynComponent(string projectRelativePath)
    {
        return projectRelativePath.StartsWith("analyzers/", StringComparison.Ordinal) ||
               projectRelativePath.StartsWith("source-generators/", StringComparison.Ordinal);
    }

    private static bool IsShippingProject(string projectRelativePath)
    {
        return projectRelativePath.StartsWith("libs/", StringComparison.Ordinal) ||
               string.Equals(projectRelativePath, UmbrellaProject, StringComparison.Ordinal);
    }

    private static XmlDocument LoadProjectDocument(string projectPath)
    {
        XmlDocument document = new();
        document.Load(projectPath);
        return document;
    }

    private static IEnumerable<ProjectReferenceInfo> ReadProjectReferences(string projectPath)
    {
        var document = LoadProjectDocument(projectPath);
        var nodes = document.SelectNodes("//*[local-name()='ProjectReference']");
        if (nodes is null)
            yield break;

        foreach (XmlNode node in nodes)
        {
            var include = GetAttribute(node, "Include");
            if (string.IsNullOrWhiteSpace(include))
                continue;

            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var targetPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
            yield return new ProjectReferenceInfo(
                include,
                GetRepositoryRelativePath(targetPath),
                GetAttribute(node, "OutputItemType"),
                GetAttribute(node, "ReferenceOutputAssembly"));
        }
    }

    private readonly record struct ProjectReferenceInfo(
        string Include,
        string TargetRelativePath,
        string OutputItemType,
        string ReferenceOutputAssembly);
}
