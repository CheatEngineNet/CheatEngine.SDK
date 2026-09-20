using System.Globalization;
using System.Reflection;
using CheatEngine.SDK.Analyzers.CodeFixes.Plugin;
using CheatEngine.SDK.Analyzers.CodeFixes.Usage;
using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Generation;
using CheatEngine.SDK.Analyzers.Plugin;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.Analyzers.Usage;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Tests.Diagnostics;

/// <summary>
///     The catalog conventions checked over every descriptor: identifier range and category, help link,
///     documentation page, release-tracking row, compilation-end tag, and who reports or fixes what. No compilation is
///     created here: these tests need neither reference assemblies nor a package cache.
/// </summary>
public sealed class DiagnosticCatalogTests
{
    private const string HelpLinkBase = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/";

    public static TheoryData<string> DescriptorIds => [.. SortedIds(AllDescriptors())];

    [Fact]
    public void Catalog_contains_exactly_the_documented_identifiers()
    {
        string[] expected =
        [
            DiagnosticIds.InvalidPluginClass,
            DiagnosticIds.MultiplePluginClasses,
            DiagnosticIds.InvalidManualBootstrap,
            DiagnosticIds.ReservedNamespace,
            DiagnosticIds.GeneratedEntryPointCollision,
            DiagnosticIds.RequiresPluginEnabledTooEarly,
            DiagnosticIds.DisposeBorrowedValue,
            DiagnosticIds.UnguardedUnmanagedCallersOnly,
            DiagnosticIds.AsyncPluginLifecycle,
            DiagnosticIds.UnsafeBlocksRequired,
            DiagnosticIds.InvalidLuaBindingContainingType,
            DiagnosticIds.InvalidLuaFunction,
            DiagnosticIds.InvalidLuaGlobal,
            DiagnosticIds.DuplicateLuaName,
            DiagnosticIds.InvalidLuaAnnotationTarget,
            DiagnosticIds.GeneratedLuaIdentityCollision
        ];

        Assert.Equal(expected, SortedIds(AllDescriptors()), StringComparer.Ordinal);
        Assert.Equal(
            [
                "CESDK0001", "CESDK0002", "CESDK0003", "CESDK0004", "CESDK0005", "CESDK1001", "CESDK1003", "CESDK1004",
                "CESDK1005", "CESDK2001", "CESDK2002", "CESDK2003", "CESDK2004", "CESDK2005", "CESDK2006", "CESDK2007"
            ],
            expected,
            StringComparer.Ordinal);
    }

    [Fact]
    public void CESDK1002_remains_absent_until_the_CE_7_7_main_thread_probe_proves_its_contract()
    {
        Assert.DoesNotContain(AllDescriptors(),
            static descriptor => string.Equals(descriptor.Id, "CESDK1002", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(DescriptorIds))]
    public void Descriptor_follows_the_conventions(string id)
    {
        var descriptor = Descriptor(id);
        var title = descriptor.Title.ToString(CultureInfo.InvariantCulture);
        var description = descriptor.Description.ToString(CultureInfo.InvariantCulture);

        Assert.Matches("^CESDK[0-9]{4}$", descriptor.Id);
        Assert.Equal(CategoryFor(descriptor.Id), descriptor.Category);
        Assert.Equal(HelpLinkBase + descriptor.Id + ".md", descriptor.HelpLinkUri);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.DoesNotMatch(@"\.$", title);
        Assert.EndsWith(".", description, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DescriptorIds))]
    public void Descriptor_has_a_documentation_page_and_a_release_tracking_row(string id)
    {
        var descriptor = Descriptor(id);

        var page = RepositoryLayout.PathOf($"analyzers/docs/{id}.md");
        Assert.True(File.Exists(page), $"Missing documentation page '{page}'.");
        Assert.StartsWith($"# {id}", File.ReadAllText(page), StringComparison.Ordinal);

        var unshipped =
            File.ReadAllText(
                RepositoryLayout.PathOf("analyzers/CheatEngine.SDK.Analyzers/AnalyzerReleases.Unshipped.md"));
        var shipped =
            File.ReadAllText(
                RepositoryLayout.PathOf("analyzers/CheatEngine.SDK.Analyzers/AnalyzerReleases.Shipped.md"));
        Assert.True(
            HasRow(unshipped, descriptor) || HasRow(shipped, descriptor),
            $"No release-tracking row starts with '{id} | {descriptor.Category} | {descriptor.DefaultSeverity} |'.");
    }

    [Fact]
    public void Release_tracking_rows_are_unique_across_shipped_and_unshipped_files()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string[] trackingFiles =
        [
            RepositoryLayout.PathOf("analyzers/CheatEngine.SDK.Analyzers/AnalyzerReleases.Shipped.md"),
            RepositoryLayout.PathOf("analyzers/CheatEngine.SDK.Analyzers/AnalyzerReleases.Unshipped.md")
        ];

        foreach (var trackingFile in trackingFiles)
        foreach (var line in File.ReadLines(trackingFile))
        {
            var cells = line.Split('|');
            if (cells.Length < 3) continue;

            var id = cells[0].Trim();
            if (!id.StartsWith("CESDK", StringComparison.Ordinal)) continue;

            Assert.True(ids.Add(id), $"Release tracking contains duplicate diagnostic id '{id}'.");
        }

        Assert.Equal(SortedIds(AllDescriptors()), ids.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    // The tables are aligned with padding in places, so the cells are compared trimmed: the rule id, the category and
    // the severity still have to match exactly.
    private static bool HasRow(string tracking, DiagnosticDescriptor descriptor)
    {
        foreach (var line in tracking.Split('\n'))
        {
            var cells = line.Split('|');
            if (cells.Length >= 3
                && string.Equals(cells[0].Trim(), descriptor.Id, StringComparison.Ordinal)
                && string.Equals(cells[1].Trim(), descriptor.Category, StringComparison.Ordinal)
                && string.Equals(cells[2].Trim(), descriptor.DefaultSeverity.ToString(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    [Theory]
    [InlineData(DiagnosticIds.InvalidPluginClass, false)]
    [InlineData(DiagnosticIds.MultiplePluginClasses, true)]
    [InlineData(DiagnosticIds.InvalidManualBootstrap, true)]
    [InlineData(DiagnosticIds.ReservedNamespace, true)]
    [InlineData(DiagnosticIds.GeneratedEntryPointCollision, true)]
    [InlineData(DiagnosticIds.RequiresPluginEnabledTooEarly, false)]
    [InlineData(DiagnosticIds.DisposeBorrowedValue, false)]
    [InlineData(DiagnosticIds.UnguardedUnmanagedCallersOnly, false)]
    [InlineData(DiagnosticIds.AsyncPluginLifecycle, false)]
    [InlineData(DiagnosticIds.UnsafeBlocksRequired, false)]
    [InlineData(DiagnosticIds.InvalidLuaBindingContainingType, false)]
    [InlineData(DiagnosticIds.InvalidLuaFunction, false)]
    [InlineData(DiagnosticIds.InvalidLuaGlobal, false)]
    [InlineData(DiagnosticIds.DuplicateLuaName, true)]
    [InlineData(DiagnosticIds.InvalidLuaAnnotationTarget, false)]
    [InlineData(DiagnosticIds.GeneratedLuaIdentityCollision, false)]
    public void Compilation_end_tag_is_on_the_rules_reported_at_compilation_end(string id, bool compilationEnd)
    {
        Assert.Equal(compilationEnd,
            Descriptor(id).CustomTags.Contains(WellKnownDiagnosticTags.CompilationEnd, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_descriptor_is_reported_by_exactly_one_analyzer()
    {
        DiagnosticAnalyzer[] analyzers =
        [
            new CheatEnginePluginAnalyzer(),
            new UnmanagedCallersOnlyGuardAnalyzer(),
            new LuaBindingAnalyzer(),
            new PluginLifecycleAndOwnershipAnalyzer(),
            new LuaObjectBindingAnalyzer()
        ];

        var supported = SortedIds(analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics));

        Assert.Equal(SortedIds(AllDescriptors()), supported, StringComparer.Ordinal);
    }

    [Fact]
    public void Code_fix_providers_fix_their_rule_and_support_fix_all()
    {
        CodeFixProvider pluginFix = new PluginClassShapeCodeFixProvider();
        CodeFixProvider guardFix = new UnmanagedCallersOnlyGuardCodeFixProvider();

        Assert.Equal([DiagnosticIds.InvalidPluginClass], pluginFix.FixableDiagnosticIds, StringComparer.Ordinal);
        Assert.Equal([DiagnosticIds.UnguardedUnmanagedCallersOnly], guardFix.FixableDiagnosticIds,
            StringComparer.Ordinal);
        Assert.Same(WellKnownFixAllProviders.BatchFixer, pluginFix.GetFixAllProvider());
        Assert.Same(WellKnownFixAllProviders.BatchFixer, guardFix.GetFixAllProvider());
    }

    [Fact]
    public void Every_plugin_class_problem_has_its_own_message_and_a_place_in_the_report_order()
    {
        PluginShapeIssues[] problems =
            [.. Enum.GetValues<PluginShapeIssues>().Where(problem => problem != PluginShapeIssues.None)];
        string[] messages = [.. problems.Select(PluginClassProblemText.Describe)];
        var fallback = PluginClassProblemText.Describe(PluginShapeIssues.None);

        Assert.Equal(problems.Order(), PluginClassProblemText.ReportOrder.Order());
        Assert.DoesNotContain(fallback, messages, StringComparer.Ordinal);
        Assert.Equal(problems.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_containing_type_problem_has_its_own_message_and_a_place_in_the_report_order()
    {
        ContainingTypeIssues[] problems =
            [.. Enum.GetValues<ContainingTypeIssues>().Where(problem => problem != ContainingTypeIssues.None)];
        string[] messages = [.. problems.Select(ContainingTypeProblemText.Describe)];

        Assert.Equal(problems.Order(), ContainingTypeProblemText.ReportOrder.Order());
        Assert.Equal(problems.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_lua_function_problem_has_its_own_message_and_a_place_in_the_report_order()
    {
        LuaFunctionShapeIssues[] problems =
            [.. Enum.GetValues<LuaFunctionShapeIssues>().Where(problem => problem != LuaFunctionShapeIssues.None)];
        string[] messages = [.. problems.Select(LuaFunctionProblemText.Describe)];

        Assert.Equal(problems.Order(), LuaFunctionProblemText.ReportOrder.Order());
        Assert.Equal(problems.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_lua_global_problem_has_its_own_message_and_a_place_in_the_report_order()
    {
        LuaGlobalShapeIssues[] problems =
            [.. Enum.GetValues<LuaGlobalShapeIssues>().Where(problem => problem != LuaGlobalShapeIssues.None)];
        string[] messages = [.. problems.Select(LuaGlobalProblemText.Describe)];

        Assert.Equal(problems.Order(), LuaGlobalProblemText.ReportOrder.Order());
        Assert.Equal(problems.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    private static DiagnosticDescriptor Descriptor(string id)
    {
        return AllDescriptors().Single(descriptor => string.Equals(descriptor.Id, id, StringComparison.Ordinal));
    }

    private static IEnumerable<string> SortedIds(IEnumerable<DiagnosticDescriptor> descriptors)
    {
        return descriptors.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal);
    }

    private static IEnumerable<DiagnosticDescriptor> AllDescriptors()
    {
        return typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!);
    }

    // 'CESDK0xxx' -> CheatEngine.SDK.Plugin, 'CESDK1xxx' -> CheatEngine.SDK.Usage, 'CESDK2xxx' -> CheatEngine.SDK.Generation.
    private static string CategoryFor(string id)
    {
        return id[5] switch
        {
            '0' => "CheatEngine.SDK.Plugin",
            '1' => "CheatEngine.SDK.Usage",
            '2' => "CheatEngine.SDK.Generation",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown diagnostic range.")
        };
    }
}
