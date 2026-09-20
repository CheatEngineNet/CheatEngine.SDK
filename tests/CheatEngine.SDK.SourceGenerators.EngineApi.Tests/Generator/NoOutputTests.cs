using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Generator;

/// <summary>Valid ignored or empty-shell inputs stay silent; malformed specs instead report a located CESDK3001 error.</summary>
public sealed class NoOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    [Fact]
    public void An_additional_file_not_named_cheatengine_sdk_api_txt_is_ignored()
    {
        var run = roslyn.Run("notes.txt", SpecSources.SingleTry);

        run.AssertNoOutput();
    }

    [Fact]
    public void An_empty_spec_file_produces_no_output_and_reports_a_specification_error()
    {
        var run = roslyn.Run("empty.cheatengine-sdk-api.txt", string.Empty);

        run.AssertNoGeneratedSource();
        Assert.Contains(run.GeneratorDiagnostics,
            static diagnostic => string.Equals(diagnostic.Id, "CESDK3001", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spec_file_with_only_a_header_produces_no_output()
    {
        var run = roslyn.Run("shell.cheatengine-sdk-api.txt", "namespace: Demo\ntype: Shell\n");

        run.AssertNoOutput();
    }

    [Fact]
    public void A_spec_file_whose_only_entry_is_invalid_produces_no_output_and_reports_a_specification_error()
    {
        const string Text = """
                            namespace: Demo
                            type: T

                            global: not a name
                            method: Bad
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: bad.
                            """;

        var run = roslyn.Run("bad.cheatengine-sdk-api.txt", Text);

        run.AssertNoGeneratedSource();
        Assert.Contains(run.GeneratorDiagnostics,
            static diagnostic => string.Equals(diagnostic.Id, "CESDK3001", StringComparison.Ordinal));
    }

    [Fact]
    public void No_spec_files_at_all_produces_no_output()
    {
        var run = GeneratorRun.Execute(RoslynFixture.CreateDriver(), roslyn.CreateCompilation());

        run.AssertNoOutput();
    }
}
