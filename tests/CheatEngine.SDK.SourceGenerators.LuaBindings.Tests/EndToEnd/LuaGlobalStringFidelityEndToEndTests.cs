using System.Diagnostics.CodeAnalysis;
using System.Text;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     Qualification Q20 at C2: strings with embedded NULs, multibyte UTF-8 and invalid sequences cross a bound global
///     with their exact byte length, and each text conversion is explicit (the UTF-16 forms decode with U+FFFD, the
///     byte forms keep the raw bytes) (audit A07-30 to A07-33, A12-11 to A12-15). The stand-ins are
///     <see cref="FidelityBindingSources.StringStandIns" />; every test asserts <c>L.Top == 0</c>.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalStringFidelityEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string StringsType = "Demo.Strings";

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Embedded_nul_survives_string_and_copy_out_results_with_exact_length()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.StringStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		Span<byte> buffer = stackalloc byte[16];

		Assert.True(assembly.Delegate<TryTextDelegate>(StringsType, "TryText")("nul", out string? text));
		Assert.Equal("a\0b\0", text);
		Assert.True(assembly.Delegate<TryTextCopyDelegate>(StringsType, "TryTextCopy")("nul", buffer, out int written));
		Assert.Equal(4, written);
		Assert.True(buffer[..written].SequenceEqual("a\0b\0"u8));
		Assert.Equal("a\0b\0", assembly.Delegate<TextDelegate>(StringsType, "Text")("nul"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Utf8_argument_with_embedded_nul_reaches_lua_with_its_exact_length()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.StringStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		Utf8LengthDelegate utf8Length = assembly.Delegate<Utf8LengthDelegate>(StringsType, "Utf8Length");
		TextLengthDelegate textLength = assembly.Delegate<TextLengthDelegate>(StringsType, "TextLength");

		Assert.Equal(3, utf8Length("a\0b"u8));
		Assert.Equal(2, utf8Length("\0\0"u8));
		Assert.Equal(0, utf8Length([]));
		Assert.Equal(3, textLength("a\0b"));
		Assert.Equal(0, textLength(string.Empty));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Multibyte_utf8_round_trips_through_string_and_utf8_forms()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.StringStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		const string Multibyte = "\u00E9\u6F22\U0001F600";
		byte[] utf8 = Encoding.UTF8.GetBytes(Multibyte);
		Span<byte> buffer = stackalloc byte[16];

		Assert.Equal(9, utf8.Length);
		Assert.Equal(Multibyte, assembly.Delegate<TextDelegate>(StringsType, "Text")("multibyte"));
		Assert.Equal(Multibyte, assembly.Delegate<EchoDelegate>(StringsType, "Echo")(Multibyte));
		Assert.Equal(9, assembly.Delegate<TextLengthDelegate>(StringsType, "TextLength")(Multibyte));
		Assert.True(assembly.Delegate<TryEchoCopyDelegate>(StringsType, "TryEchoCopy")(utf8, buffer, out int written));
		Assert.True(buffer[..written].SequenceEqual(utf8));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Invalid_utf8_from_lua_becomes_replacement_in_string_and_stays_raw_in_copy_out()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.StringStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		ReadOnlySpan<byte> invalid = [0xFF, 0xFE, (byte) 'A'];
		Span<byte> buffer = stackalloc byte[16];

		Assert.True(assembly.Delegate<TryTextDelegate>(StringsType, "TryText")("invalid", out string? text));
		Assert.Equal("\uFFFD\uFFFDA", text);
		Assert.True(assembly.Delegate<TryTextCopyDelegate>(StringsType, "TryTextCopy")("invalid", buffer,
			out int written));
		Assert.True(buffer[..written].SequenceEqual(invalid));
		Assert.True(assembly.Delegate<TryEchoCopyDelegate>(StringsType, "TryEchoCopy")(invalid, buffer, out written));
		Assert.True(buffer[..written].SequenceEqual(invalid));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Lone_surrogate_argument_is_pushed_as_the_utf8_replacement_character()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.StringStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TextLengthDelegate textLength = assembly.Delegate<TextLengthDelegate>(StringsType, "TextLength");
		EchoDelegate echo = assembly.Delegate<EchoDelegate>(StringsType, "Echo");

		// U+FFFD is three UTF-8 bytes; a well-formed surrogate pair is one four-byte scalar.
		Assert.Equal(3, textLength("\uD800"));
		Assert.Equal("a\uFFFDb", echo("a\uD800b"));
		Assert.Equal("\uFFFD", echo("\uDC00"));
		Assert.Equal(4, textLength("\uD83D\uDE00"));
		Assert.Equal("\uD83D\uDE00", echo("\uD83D\uDE00"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void Nullable_string_argument_null_is_pushed_as_nil_for_a_global_but_rejected_by_a_function()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.StringStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TypeOfDelegate typeOf = assembly.Delegate<TypeOfDelegate>(StringsType, "TypeOf");
		LuaStatus registered = (LuaStatus) assembly.Method(StringsType, "RegisterLuaFunctions").Invoke(null, [L])!;
		Assert.True(registered.IsOk);

		// The kept asymmetry: a global wrapper pushes C# null as nil; an exported function reads strings strictly, so
		// nil is "no string" there (LuaFunctionEndToEndTests.Nil_is_rejected_for_a_nullable_string_argument_...).
		Assert.Equal("nil", typeOf(null));
		Assert.Equal("string", typeOf(string.Empty));
		Assert.Equal("text:3", LuaTest.RunForString(L, "return f_describe('a\\0b')"u8));
		Assert.Equal("test:1: bad argument #1 (string expected, got nil)",
			LuaTest.RunForError(L, "return pcall(function() f_describe(nil) end)"u8));
		Assert.Equal(0, L.Top);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run(FidelityBindingSources.StringSuite));
	}

	private delegate bool TryTextDelegate(string key, [MaybeNullWhen(false)] out string value);

	private delegate bool TryTextCopyDelegate(string key, Span<byte> destination, out int written);

	private delegate string TextDelegate(string key);

	private delegate long Utf8LengthDelegate(ReadOnlySpan<byte> text);

	private delegate long TextLengthDelegate(string text);

	private delegate string EchoDelegate(string text);

	private delegate bool TryEchoCopyDelegate(ReadOnlySpan<byte> text, Span<byte> destination, out int written);

	private delegate string TypeOfDelegate(string? text);
}
