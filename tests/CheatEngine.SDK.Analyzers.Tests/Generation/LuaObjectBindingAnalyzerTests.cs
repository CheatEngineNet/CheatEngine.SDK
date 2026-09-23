using System.Collections.Immutable;
using System.Globalization;

using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Generation;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Tests.Generation;

/// <summary>CESDK2006 and CESDK2007: unsupported Lua object declarations and source collisions with generated members.</summary>
public sealed class LuaObjectBindingAnalyzerTests
{
	private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

	// The real SDK assemblies: LuaOptional<T> is recognised only as the type CheatEngine.SDK.Lua defines.
	private static readonly ImmutableArray<MetadataReference> RealSdkReferences =
	[
		MetadataReference.CreateFromFile(typeof(LuaFunctionAttribute).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(LuaApi).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(LuaState).Assembly.Location)
	];

	[Fact]
	public async Task Invalid_lua_class_name_reserved_method_parameter_and_property_shape_report_CESDK2006()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			[LuaClass("end")]
			public readonly partial struct {|CESDK2006:InvalidName|}
			{
			}

			[LuaClass("Object")]
			public readonly partial struct ValidHandle
			{
			    [LuaMethod("call")]
			    partial void {|CESDK2006:Call|}(int __ceState);

			    [LuaMethod("operate")]
			    partial void {|CESDK2006:Operate|}(int __ceOperation);

			    [LuaProperty("value")]
			    public int {|CESDK2006:Value|} => 0;
			}
			""");
	}

	[Fact]
	public async Task Generated_handle_and_lua_thunk_identity_collisions_report_CESDK2007()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			[LuaClass("Object")]
			public readonly partial struct {|CESDK2007:Handle|}
			{
			}

			[LuaClass("Object")]
			public readonly partial struct HandleWithCollision
			{
			    private readonly int {|CESDK2007:_handle|};
			    private readonly int {|CESDK2007:Handle|};
			}

			public static partial class Functions
			{
			    [LuaFunction("load")]
			    public static void Load() { }

			    private static int {|CESDK2007:__LuaThunk_load|}() => 0;
			    private static void {|CESDK2007:TryRegisterLuaFunctions|}() { }
			    private static void {|CESDK2007:RegisterLuaFunctions|}() { }

			    [LuaGlobal("read")]
			    static partial void Read();

			    private static int {|CESDK2007:s_luaGlobal_read|};
			}
			""");
	}

	[Theory]
	[InlineData("_handle")]
	[InlineData("Handle")]
	[InlineData("FromHandle")]
	[InlineData("Equals")]
	[InlineData("GetHashCode")]
	[InlineData("Push")]
	[InlineData("TryRead")]
	public async Task Every_generated_handle_member_name_reports_CESDK2007(string memberName)
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
				using CheatEngine.SDK.Annotations.Lua;

				namespace Demo;

				[LuaClass("Object")]
				public readonly partial struct Collision
				{
				    private int {|CESDK2007:MEMBER|} => 0;
				}

				[LuaClass("Object")]
				public readonly partial struct Valid
				{
				}
				""".Replace("MEMBER", memberName, StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("_handle")]
	[InlineData("Handle")]
	[InlineData("FromHandle")]
	[InlineData("Equals")]
	[InlineData("GetHashCode")]
	[InlineData("Push")]
	[InlineData("TryRead")]
	public async Task Every_generated_handle_type_name_reports_CESDK2007(string typeName)
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
				using CheatEngine.SDK.Annotations.Lua;

				namespace Demo;

				[LuaClass("Object")]
				public readonly partial struct {|CESDK2007:TYPE|}
				{
				}
				""".Replace("TYPE", typeName, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Lua_global_generated_local_parameters_report_CESDK2007()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static partial class Globals
			{
			    [LuaGlobal("readL")]
			    static partial void ReadL(int {|CESDK2007:__L|});

			    [LuaGlobal("readOperation")]
			    static partial void ReadOperation(int {|CESDK2007:__operation|});

			    [LuaGlobal("readTop")]
			    static partial void ReadTop(int {|CESDK2007:__top|});

			    [LuaGlobal("readOk")]
			    static partial void ReadOk(int {|CESDK2007:__ok|});

			    [LuaGlobal("readStatus")]
			    static partial void ReadStatus(int {|CESDK2007:__status|});

			    [LuaGlobal("readResult")]
			    static partial void ReadResult(int {|CESDK2007:__result|});

			    [LuaGlobal("readResolution")]
			    static partial void ReadResolution(int {|CESDK2007:__resolution|});

			    [LuaGlobal("readException")]
			    static partial void ReadException(int {|CESDK2007:__exception|});

			    [LuaGlobal("readArgc")]
			    static partial void ReadArgc(int {|CESDK2007:__argc|});

			    [LuaGlobal("readRest")]
			    static partial void ReadRest(int {|CESDK2007:__rest|});
			}
			""");
	}

	[Fact]
	public async Task LuaOptional_on_a_lua_method_reports_CESDK2013_until_object_members_support_it()
	{
		const string Source = """
		                      using CheatEngine.SDK.Annotations.Lua;
		                      using CheatEngine.SDK.Lua.Marshalling;

		                      namespace Demo;

		                      [LuaClass("Object")]
		                      public readonly partial struct Handle
		                      {
		                          [LuaMethod("load")]
		                          public partial void Load(int path, LuaOptional<bool> merge);

		                          [LuaMethod("read")]
		                          public partial bool TryRead(out LuaOptional<int> value);

		                          [LuaProperty("Value")]
		                          public partial LuaOptional<int> Value { get; }

		                          [LuaMethod("count")]
		                          public partial int Count(int first);
		                      }
		                      """;
		CSharpCompilation compilation = CSharpCompilation.Create(
			"LuaObjectOptionalTestAssembly",
			[
				CSharpSyntaxTree.ParseText(TestText.Normalize(Source), ParseOptions, "Test.cs",
					cancellationToken: TestContext.Current.CancellationToken)
			],
			LocalFrameworkReferences.References.AddRange(RealSdkReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

		ImmutableArray<Diagnostic> diagnostics = await compilation
			.WithAnalyzers([new LuaObjectBindingAnalyzer(), new LuaBindingAnalyzer()], options: null)
			.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Load", "TryRead", "Value"],
			diagnostics.Where(static d =>
					string.Equals(d.Id, DiagnosticIds.UnsupportedLuaOptionalPosition, StringComparison.Ordinal))
				.Select(static d => d.GetMessage(CultureInfo.InvariantCulture).Split('\'')[1])
				.Order(StringComparer.Ordinal),
			StringComparer.Ordinal);
		Assert.All(
			diagnostics.Where(static d =>
				string.Equals(d.Id, DiagnosticIds.UnsupportedLuaOptionalPosition, StringComparison.Ordinal)),
			static d => Assert.Contains("[LuaMethod] and [LuaProperty] members do not support optional values yet",
				d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		// The optional position is explained once, by CESDK2013, not a second time as an unsupported type (CESDK2006).
		Assert.DoesNotContain(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaAnnotationTarget, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Generated_handle_constructor_collision_reports_CESDK2007()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace CheatEngine.SDK.Engine.Objects
			{
			    public readonly struct CEObject
			    {
			    }
			}

			namespace Demo
			{
			    [LuaClass("Object")]
			    public readonly partial struct ConstructorCollision
			    {
			        private {|CESDK2007:ConstructorCollision|}(
			            global::CheatEngine.SDK.Engine.Objects.CEObject handle)
			        {
			        }
			    }
			}
			""");
	}

	[Fact]
	public async Task Generated_handle_accessor_collision_reports_CESDK2007()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace CheatEngine.SDK.Engine.Objects
			{
			    public readonly struct CEObject
			    {
			    }
			}

			namespace Demo
			{
			    [LuaClass("Object")]
			    public readonly partial struct ObjectHandle
			    {
			        private global::CheatEngine.SDK.Engine.Objects.CEObject {|CESDK2007:get_Handle|}() => default;
			    }

			    [LuaClass("Sibling")]
			    public readonly partial struct Sibling
			    {
			    }
			}
			""");
	}

	[Fact]
	public async Task Generic_handle_accessor_collision_reports_CESDK2007()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace CheatEngine.SDK.Engine.Objects
			{
			    public readonly struct CEObject
			    {
			    }
			}

			namespace Demo
			{
			    [LuaClass("Object")]
			    public readonly partial struct ObjectHandle
			    {
			        private global::CheatEngine.SDK.Engine.Objects.CEObject {|CESDK2007:get_Handle|}<T>() => default;
			    }
			}
			""");
	}

	[Fact]
	public async Task Generated_handle_setter_collision_requires_the_exact_CEObject_signature()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace CheatEngine.SDK.Engine.Objects
			{
			    public readonly struct CEObject
			    {
			    }
			}

			namespace Demo
			{
			    [LuaClass("Object")]
			    public readonly partial struct ObjectHandle
			    {
			        private void {|CESDK2007:set_Handle|}(global::CheatEngine.SDK.Engine.Objects.CEObject value) { }
			        private void {|CESDK2007:set_Handle|}<T>(global::CheatEngine.SDK.Engine.Objects.CEObject value) { }
			        private void set_Handle(int value) { }
			    }
			}
			""");
	}

	[Fact]
	public async Task Generated_handle_accessor_named_non_methods_report_CESDK2007()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo
			{
			    [LuaClass("Field")]
			    public readonly partial struct Field
			    {
			        private readonly int {|CESDK2007:get_Handle|};
			    }

			    [LuaClass("Property")]
			    public readonly partial struct Property
			    {
			        private int {|CESDK2007:set_Handle|} => 0;
			    }

			    [LuaClass("Nested")]
			    public readonly partial struct Nested
			    {
			        private struct {|CESDK2007:get_Handle|} { }
			    }
			}
			""");
	}

	[Fact]
	public async Task Record_and_ref_like_borrowed_handles_report_CESDK2006()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			[LuaClass("Record")]
			public readonly partial record struct {|CESDK2006:RecordHandle|}
			{
			}

			[LuaClass("Ref")]
			public readonly ref partial struct {|CESDK2006:RefLikeHandle|}
			{
			}
			""");
	}

	[Fact]
	public async Task Ref_return_methods_and_properties_report_CESDK2006()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			[LuaClass("Object")]
			public readonly partial struct ObjectHandle
			{
			    [LuaMethod("refMethod")]
			    private partial ref int RefMethod();

			    [LuaMethod("readonlyRefMethod")]
			    private partial ref readonly int ReadonlyRefMethod();

			    [LuaProperty("refProperty")]
			    public partial ref int RefProperty { get; }

			    [LuaProperty("readonlyRefProperty")]
			    public partial ref readonly int ReadonlyRefProperty { get; }
			}
			""");

		int count = 0;
		foreach (Diagnostic diagnostic in diagnostics)
		{
			if (!string.Equals(diagnostic.Id, "CESDK2006", StringComparison.Ordinal))
			{
				continue;
			}

			count++;
			Assert.Contains("ref and ref readonly", diagnostic.GetMessage(CultureInfo.InvariantCulture),
				StringComparison.Ordinal);
		}

		Assert.Equal(4, count);
	}

	[Fact]
	public async Task Explicit_interface_lua_property_reports_CESDK2006()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public interface IValue
			{
			    int Value { get; }
			}

			[LuaClass("Object")]
			public readonly partial struct ObjectHandle : IValue
			{
			    [LuaProperty("value")]
			    partial int IValue.Value { get; }
			}
			""");

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static candidate => string.Equals(candidate.Id, "CESDK2006", StringComparison.Ordinal));
		Assert.Contains("partial property", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_valid_borrowed_handle_reports_nothing()
	{
		await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			[LuaClass("Object")]
			public readonly partial struct ObjectHandle
			{
			}
			""");
	}

	private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
	{
		CSharpCompilation compilation = CSharpCompilation.Create(
			"LuaObjectBindingAnalyzerTestAssembly",
			[
				CSharpSyntaxTree.ParseText(TestText.Normalize(source), ParseOptions, "Test.cs",
					cancellationToken: TestContext.Current.CancellationToken)
			],
			LocalFrameworkReferences.References.AddRange(ContractStubs.References),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable));
		CompilationWithAnalyzers withAnalyzers =
			compilation.WithAnalyzers([new LuaObjectBindingAnalyzer()], options: null);
		return withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
	}
}
