using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>
///     The Lua name rule shared by the generator and the analyzer (
///     <c>CheatEngine.SDK.SourceGenerators.Shared.LuaEmit.LuaNames</c>).
/// </summary>
public sealed class LuaNamesTests
{
	[Theory]
	[InlineData("readInteger")]
	[InlineData("_G")]
	[InlineData("a")]
	[InlineData("x1")]
	[InlineData("__thunk")]
	[InlineData("END")]
	[InlineData("nilValue")]
	public void IsValidName_lua_identifier_true(string name)
	{
		Assert.True(LuaNames.IsValidName(name));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("1abc")]
	[InlineData("read-int")]
	[InlineData("ce.read")]
	[InlineData("with space")]
	[InlineData("caf\u00E9")]
	[InlineData("and")]
	[InlineData("end")]
	[InlineData("function")]
	[InlineData("goto")]
	[InlineData("nil")]
	[InlineData("true")]
	public void IsValidName_not_an_identifier_or_reserved_false(string? name)
	{
		Assert.False(LuaNames.IsValidName(name));
	}

	[Fact]
	public void IsReservedWord_covers_the_22_words_of_lua_5_3()
	{
		string[] reserved =
		[
			"and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
			"local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
		];

		Assert.Equal(22, reserved.Length);
		Assert.All(reserved, word => Assert.True(LuaNames.IsReservedWord(word), word));
		Assert.False(LuaNames.IsReservedWord("End"));
		Assert.False(LuaNames.IsReservedWord("self"));
	}
}
