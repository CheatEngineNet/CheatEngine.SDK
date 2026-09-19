using CESDK.SourceGenerators.EngineApi.Tests.Infrastructure;

namespace CESDK.SourceGenerators.EngineApi.Tests.Generator;

/// <summary>Every way the generator stays silent (no diagnostic, no output for that item).</summary>
public sealed class NoOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    [Fact]
    public void An_additional_file_not_named_cesdk_api_txt_is_ignored()
    {
        var run = roslyn.Run("notes.txt", SpecSources.SingleTry);

        run.AssertNoOutput();
    }

    [Fact]
    public void An_empty_spec_file_produces_no_output()
    {
        var run = roslyn.Run("empty.cesdk-api.txt", string.Empty);

        run.AssertNoOutput();
    }

    [Fact]
    public void A_spec_file_with_only_a_header_produces_no_output()
    {
        var run = roslyn.Run("shell.cesdk-api.txt", "namespace: Demo\ntype: Shell\n");

        run.AssertNoOutput();
    }

    [Fact]
    public void A_spec_file_whose_only_entry_is_invalid_produces_no_output()
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

        var run = roslyn.Run("bad.cesdk-api.txt", Text);

        run.AssertNoOutput();
    }

    [Fact]
    public void No_spec_files_at_all_produces_no_output()
    {
        var run = GeneratorRun.Execute(RoslynFixture.CreateDriver(), roslyn.CreateCompilation());

        run.AssertNoOutput();
    }
}
