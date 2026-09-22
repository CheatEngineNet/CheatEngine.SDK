using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     Facts of a <c>[LuaGlobal]</c> defining declaration that the implementing declaration must repeat exactly for the
///     pair to compile: the C# compiler enforces this for partial methods (CS8826 for parameter names/types/nullability,
///     CS8988 for an explicit <see langword="scoped" /> modifier on a by-value <c>ref struct</c> parameter).
/// </summary>
public sealed class PartialMethodSignatureTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string Usings = "using CheatEngine.SDK.Annotations.Lua;\n";

	[Fact]
	public void Generator_repeats_an_explicit_scoped_readonlyspan_argument()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(scoped System.ReadOnlySpan<byte> data, out int v); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("public static partial bool TryG(scoped global::System.ReadOnlySpan<byte> data, out int v)",
			run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_omits_scoped_when_the_defining_declaration_did()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(System.ReadOnlySpan<byte> data, out int v); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		string text = run.SingleGeneratedText;
		Assert.Contains("public static partial bool TryG(global::System.ReadOnlySpan<byte> data, out int v)", text,
			StringComparison.Ordinal);
		Assert.DoesNotContain("scoped global::System.ReadOnlySpan<byte>", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_repeats_an_explicit_scoped_copyout_destination()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, scoped System.Span<byte> destination, out int written); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("scoped global::System.Span<byte> destination, out int written", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_omits_scoped_on_the_copyout_destination_when_the_defining_declaration_did()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, System.Span<byte> destination, out int written); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		string text = run.SingleGeneratedText;
		Assert.Contains("global::System.Span<byte> destination, out int written", text, StringComparison.Ordinal);
		Assert.DoesNotContain("scoped global::System.Span<byte>", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_scoped_out_result_of_a_non_ref_struct_type_needs_no_special_handling()
	{
		// 'scoped' on an 'out' parameter of a non-ref-struct type does not affect partial-signature matching (the
		// compiler accepts a mismatch there), so the emitter never needs to repeat it; this pins that down.
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, scoped out int v); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("public static partial bool TryG(nuint a, out int v)", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_preserves_the_extension_receiver_in_the_implementing_declaration()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(this nuint address, out int value); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("public static partial bool TryG(this nuint address, out int value)", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_preserves_the_extension_receiver_on_a_copyout_destination()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(this System.Span<byte> destination, out int written); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("public static partial bool TryG(this global::System.Span<byte> destination, out int written)",
			run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_qualifies_the_cache_when_a_parameter_uses_its_name()
	{
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial void G(int s_luaGlobal_g); }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("TryPush(__L, global::Demo.Holder.s_luaGlobal_g, \"g\"u8)", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_parameter_named_like_a_generated_local_is_skipped_without_poisoning_compilation()
	{
		// A generated partial body shares its parameter scope with the defining declaration. Do not emit CS0136 and
		// leave the analyzer to report the precise CESDK2007 collision at the author declaration.
		const string Source = Usings +
							  "namespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(nuint __L, out int v); }";

		GeneratorRun run = roslyn.Run(Source);

		Assert.Empty(run.GeneratedSources);
		Assert.Empty(run.GeneratorDiagnostics);
	}
}
