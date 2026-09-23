using System.Text;

using BenchmarkDotNet.Attributes;

using CheatEngine.SDK.SourceGenerators.EngineApi;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.Benchmarks;

/// <summary>
///     Measures the EngineApi incremental-generator pipeline for its representative two-spec workload. It separates a
///     fresh run, an identical re-run and an edit to one spec from compiler or MSBuild time, so it records the work the
///     generator itself performs rather than a whole build.
/// </summary>
/// <remarks>
///     Generated code is intentionally not compiled here. The generator has no compilation input, and its semantic
///     compilation and incremental-caching contracts remain test responsibilities in
///     <c>CheatEngine.SDK.SourceGenerators.EngineApi.Tests</c>. This benchmark is evidence for allocation and relative
///     pipeline cost only; it must not become a PR timing threshold.
/// </remarks>
[MemoryDiagnoser(false)]
[BenchmarkCategory("SourceGenerator", "Incremental", "EngineApi")]
public class EngineApiIncrementalBenchmarks
{
	private const string FirstSpec = """
	                                 namespace: Bench.Engine
	                                 type: MemoryScalars

	                                 global: readInteger
	                                 method: TryReadInt32
	                                 form: try
	                                 arg: address:address
	                                 fixed: boolean:true
	                                 result: value:int32
	                                 doc: Reads a 32-bit value.

	                                 global: writeInteger
	                                 method: WriteInt32
	                                 form: throwing
	                                 arg: address:address
	                                 arg: value:int32
	                                 return: boolean
	                                 doc: Writes a 32-bit value.

	                                 global: readQword
	                                 method: TryReadInt64
	                                 form: try
	                                 arg: address:address
	                                 result: value:int64
	                                 doc: Reads a 64-bit value.

	                                 global: writeQword
	                                 method: WriteInt64
	                                 form: throwing
	                                 arg: address:address
	                                 arg: value:int64
	                                 return: boolean
	                                 doc: Writes a 64-bit value.
	                                 """;

	private const string EditedFirstSpec = """
	                                       namespace: Bench.Engine
	                                       type: MemoryScalars

	                                       global: readInteger
	                                       method: TryReadInt32AfterEdit
	                                       form: try
	                                       arg: address:address
	                                       fixed: boolean:true
	                                       result: value:int32
	                                       doc: Reads a 32-bit value after a spec edit.

	                                       global: writeInteger
	                                       method: WriteInt32
	                                       form: throwing
	                                       arg: address:address
	                                       arg: value:int32
	                                       return: boolean
	                                       doc: Writes a 32-bit value.

	                                       global: readQword
	                                       method: TryReadInt64
	                                       form: try
	                                       arg: address:address
	                                       result: value:int64
	                                       doc: Reads a 64-bit value.

	                                       global: writeQword
	                                       method: WriteInt64
	                                       form: throwing
	                                       arg: address:address
	                                       arg: value:int64
	                                       return: boolean
	                                       doc: Writes a 64-bit value.
	                                       """;

	private const string SecondSpec = """
	                                  namespace: Bench.Runtime
	                                  type: RuntimeInfo

	                                  global: getCEVersion
	                                  method: GetVersion
	                                  form: throwing
	                                  return: int32
	                                  doc: Reads a runtime version value.
	                                  """;

	private CSharpCompilation? _compilation;
	private BenchmarkAdditionalText? _editedFirstSpec;

	private BenchmarkAdditionalText? _firstSpec;

	private BenchmarkAdditionalText? _secondSpec;

	private GeneratorDriver? _warmDriver;

	/// <summary>Constructs and warms the driver that the incremental cases reuse.</summary>
	[GlobalSetup]
	public void Setup()
	{
		_compilation = CSharpCompilation.Create("CheatEngine.SDK.Benchmarks.EngineApiWorkload");
		_firstSpec = new BenchmarkAdditionalText("memory-a.cheatengine-sdk-api.txt", FirstSpec);
		_secondSpec = new BenchmarkAdditionalText("memory-b.cheatengine-sdk-api.txt", SecondSpec);
		_editedFirstSpec = new BenchmarkAdditionalText("memory-a.cheatengine-sdk-api.txt", EditedFirstSpec);
		_warmDriver = CreateDriver(_firstSpec, _secondSpec).RunGenerators(_compilation);
	}

	/// <summary>Creates a fresh driver and generates both curated-spec shapes.</summary>
	[Benchmark(Baseline = true)]
	public int ColdTwoSpecs()
	{
		return GeneratedSourceCount(CreateDriver(First(), Second()).RunGenerators(Compilation()));
	}

	/// <summary>Re-runs an already warmed driver without changing either additional file.</summary>
	[Benchmark]
	public int CachedNoInputChange()
	{
		return GeneratedSourceCount(WarmDriver().RunGenerators(Compilation()));
	}

	/// <summary>Replaces one spec in the warmed driver and generates the two resulting files.</summary>
	[Benchmark]
	public int OneSpecModified()
	{
		GeneratorDriver updated = WarmDriver().ReplaceAdditionalText(First(), EditedFirst());
		return GeneratedSourceCount(updated.RunGenerators(Compilation()));
	}

	private static CSharpGeneratorDriver CreateDriver(params AdditionalText[] specs)
	{
		return CSharpGeneratorDriver.Create(
			[new EngineApiGenerator().AsSourceGenerator()],
			specs,
			new CSharpParseOptions(LanguageVersion.CSharp14),
			null,
			new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, false));
	}

	private static int GeneratedSourceCount(GeneratorDriver driver)
	{
		return driver.GetRunResult().Results[0].GeneratedSources.Length;
	}

	private CSharpCompilation Compilation()
	{
		return _compilation ?? throw new InvalidOperationException("Benchmark setup did not create a compilation.");
	}

	private BenchmarkAdditionalText First()
	{
		return _firstSpec ?? throw new InvalidOperationException("Benchmark setup did not create the first spec.");
	}

	private BenchmarkAdditionalText Second()
	{
		return _secondSpec ?? throw new InvalidOperationException("Benchmark setup did not create the second spec.");
	}

	private BenchmarkAdditionalText EditedFirst()
	{
		return _editedFirstSpec ??
		       throw new InvalidOperationException("Benchmark setup did not create the edited spec.");
	}

	private GeneratorDriver WarmDriver()
	{
		return _warmDriver ?? throw new InvalidOperationException("Benchmark setup did not warm the generator driver.");
	}

	private sealed class BenchmarkAdditionalText(string path, string text) : AdditionalText
	{
		private readonly SourceText _text = SourceText.From(text, Encoding.UTF8);

		/// <inheritdoc />
		public override string Path
		{
			get;
		} = path;

		/// <inheritdoc />
		public override SourceText GetText(CancellationToken cancellationToken = default)
		{
			return _text;
		}
	}
}
