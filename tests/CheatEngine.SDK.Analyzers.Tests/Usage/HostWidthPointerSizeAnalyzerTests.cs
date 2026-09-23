using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.Analyzers.Usage;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Usage;

/// <summary>
///     CESDK1020: a <c>PointerSize</c> built from the plugin process width. The Engine type is supplied by an in-memory
///     assembly named <c>CheatEngine.SDK.Engine</c> (this test project does not reference the Engine), so the tests also
///     prove that the rule matches the defining assembly and ignores a lookalike.
/// </summary>
public sealed class HostWidthPointerSizeAnalyzerTests
{
	private const string EngineStubSource = """
	                                        namespace CheatEngine.SDK.Engine.Runtime
	                                        {
	                                            public readonly struct PointerSize
	                                            {
	                                                public PointerSize(int bytes) { Bytes = bytes; }
	                                                public int Bytes { get; }
	                                                public static PointerSize Unknown => default;
	                                                public static PointerSize Bit32 => new PointerSize(4);
	                                                public static PointerSize Bit64 => new PointerSize(8);
	                                            }
	                                        }
	                                        """;

	private static readonly Lazy<MetadataReference> SEngineStub = new(() => Compile("CheatEngine.SDK.Engine"));
	private static readonly Lazy<MetadataReference> SForeignStub = new(() => Compile("Foreign.Engine"));

	[Fact]
	public async Task host_width_sources_passed_to_the_pointer_size_constructor_report_CESDK1020()
	{
		await VerifyAsync(
			"""
			using System;
			using System.Runtime.CompilerServices;
			using System.Runtime.InteropServices;
			using CheatEngine.SDK.Engine.Runtime;

			namespace Demo;

			internal static unsafe class Widths
			{
			    public static PointerSize[] All() =>
			    [
			        {|CESDK1020:new PointerSize(IntPtr.Size)|},
			        {|CESDK1020:new PointerSize(UIntPtr.Size)|},
			        {|CESDK1020:new PointerSize(nint.Size)|},
			        {|CESDK1020:new PointerSize(nuint.Size)|},
			        {|CESDK1020:new PointerSize((int) (long) IntPtr.Size)|},
			        {|CESDK1020:new PointerSize(sizeof(nint))|},
			        {|CESDK1020:new PointerSize(sizeof(nuint))|},
			        {|CESDK1020:new PointerSize(sizeof(void*))|},
			        {|CESDK1020:new PointerSize(sizeof(byte*))|},
			        {|CESDK1020:new PointerSize(Unsafe.SizeOf<nint>())|},
			        {|CESDK1020:new PointerSize(Unsafe.SizeOf<nuint>())|},
			        {|CESDK1020:new PointerSize(Marshal.SizeOf<IntPtr>())|},
			    ];
			}
			""");
	}

	[Fact]
	public async Task host_width_conditionals_selecting_bit64_or_bit32_report_CESDK1020()
	{
		await VerifyAsync(
			"""
			using System;
			using CheatEngine.SDK.Engine.Runtime;

			namespace Demo;

			internal static class Widths
			{
			    public static PointerSize FromProcess() =>
			        {|CESDK1020:Environment.Is64BitProcess ? PointerSize.Bit64 : PointerSize.Bit32|};

			    public static PointerSize FromIntPtr() =>
			        {|CESDK1020:IntPtr.Size == 8 ? PointerSize.Bit64 : PointerSize.Bit32|};

			    public static PointerSize Reversed() =>
			        {|CESDK1020:nint.Size != 8 ? PointerSize.Bit32 : PointerSize.Bit64|};
			}
			""");
	}

	[Fact]
	public async Task the_message_names_the_host_width_expression()
	{
		await VerifyAsync(
			"""
			using System;
			using CheatEngine.SDK.Engine.Runtime;

			namespace Demo;

			internal static class Widths
			{
			    public static PointerSize Width() => {|#0:new PointerSize(IntPtr.Size)|};
			}
			""",
			new DiagnosticResult(DiagnosticDescriptors.HostWidthPointerSize).WithLocation(0)
				.WithArguments("IntPtr.Size"));
	}

	[Fact]
	public async Task observed_widths_constants_host_address_uses_and_copied_values_stay_silent()
	{
		await VerifyAsync(
			"""
			using System;
			using CheatEngine.SDK.Engine.Runtime;

			namespace Demo;

			internal static class Widths
			{
			    public static PointerSize Constant() => new PointerSize(4);

			    public static PointerSize Named() => PointerSize.Bit64;

			    // No dataflow: a width copied into a local first is not followed.
			    public static PointerSize Copied()
			    {
			        int width = IntPtr.Size;
			        return new PointerSize(width);
			    }

			    // The plugin width used for a host value is not a PointerSize.
			    public static string FormatHostAddress(nint address) => address.ToString(IntPtr.Size == 8 ? "X16" : "X8");

			    public static byte[] HostBuffer() => new byte[IntPtr.Size];

			    public static PointerSize FromObservation(bool is64Bit) => is64Bit ? PointerSize.Bit64 : PointerSize.Bit32;

			    public static PointerSize NotAWidthChoice() =>
			        Environment.Is64BitProcess ? PointerSize.Unknown : PointerSize.Bit32;
			}
			""");
	}

	[Fact]
	public async Task a_lookalike_pointer_size_outside_the_engine_assembly_stays_silent()
	{
		CheatEngineSdkAnalyzerTest<HostWidthPointerSizeAnalyzer> source = new();
		source.TestState.Sources.Add(("Test0.cs", TestText.Normalize(
			"""
			using System;

			namespace CheatEngine.SDK.Engine.Runtime
			{
			    public readonly struct PointerSize
			    {
			        public PointerSize(int bytes) { }
			    }
			}

			namespace Demo
			{
			    internal static class Widths
			    {
			        public static CheatEngine.SDK.Engine.Runtime.PointerSize Width() =>
			            new CheatEngine.SDK.Engine.Runtime.PointerSize(IntPtr.Size);
			    }
			}
			""")));
		await source.RunAsync(TestContext.Current.CancellationToken);

		CheatEngineSdkAnalyzerTest<HostWidthPointerSizeAnalyzer> foreign = new();
		foreign.TestState.AdditionalReferences.Add(SForeignStub.Value);
		foreign.TestState.Sources.Add(("Test0.cs", TestText.Normalize(
			"""
			using System;
			using CheatEngine.SDK.Engine.Runtime;

			namespace Demo;

			internal static class Widths
			{
			    public static PointerSize Width() => new PointerSize(IntPtr.Size);
			}
			""")));
		await foreign.RunAsync(TestContext.Current.CancellationToken);
	}

	private static Task VerifyAsync(string source, params DiagnosticResult[] expected)
	{
		CheatEngineSdkAnalyzerTest<HostWidthPointerSizeAnalyzer> test = new();
		test.TestState.AdditionalReferences.Add(SEngineStub.Value);
		test.TestState.Sources.Add(("Test0.cs", TestText.Normalize(source)));
		test.SolutionTransforms.Add(static (solution, projectId) =>
		{
			Project project = solution.GetProject(projectId)!;
			CSharpCompilationOptions options = (CSharpCompilationOptions) project.CompilationOptions!;
			return solution.WithProjectCompilationOptions(projectId, options.WithAllowUnsafe(true));
		});
		test.ExpectedDiagnostics.AddRange(expected);
		return test.RunAsync(TestContext.Current.CancellationToken);
	}

	private static PortableExecutableReference Compile(string assemblyName)
	{
		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(EngineStubSource, new CSharpParseOptions(LanguageVersion.CSharp14))],
			LocalFrameworkReferences.References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using MemoryStream image = new();
		EmitResult result = compilation.Emit(image);
		Assert.True(result.Success, "The PointerSize stub did not compile:\n" + string.Join('\n', result.Diagnostics));
		return MetadataReference.CreateFromImage([.. image.ToArray()], filePath: assemblyName + ".dll");
	}
}
