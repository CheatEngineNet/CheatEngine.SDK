using System.Text;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.State;

/// <summary>Strings across the boundary: bytes are preserved exactly, UTF-16 is transcoded, reads are strict.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaStateStringTests
{
    [Fact]
    public void Utf8_bytes_with_embedded_nul_round_trip_exactly()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        var bytes = "ab\0cd\0"u8;

        Utf8Marshaller.Push(L, bytes);

        Assert.True(Utf8Marshaller.TryRead(L, -1, out var read));
        Assert.True(read.SequenceEqual(bytes));
        Assert.Equal((nuint)6, L.RawLength(-1));
        Assert.True(L.TryReadString(-1, out var text));
        Assert.Equal("ab\0cd\0", text);
    }

    [Fact]
    public void Non_ascii_text_round_trips_through_utf8()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        const string Text = "Cheat Engine \u00E9\u00E8 \u20AC \u4F60\u597D \uD83D\uDE00";

        StringMarshaller.Push(L, Text);

        Assert.True(L.TryReadUtf8(-1, out var utf8));
        Assert.True(utf8.SequenceEqual(Encoding.UTF8.GetBytes(Text)));
        Assert.True(StringMarshaller.TryRead(L, -1, out var read));
        Assert.Equal(Text, read);
    }

    [Fact]
    public void Lua_sees_the_bytes_the_sdk_pushed()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        LuaTest.Run(L, "return function(s) return #s, s:byte(1), s:byte(-1) end"u8, 1);

        L.PushString("\u00E9x");
        var status = L.TryCall(1, 3);

        Assert.True(status.IsOk);
        Assert.True(L.TryReadInteger(-3, out var length));
        Assert.Equal(3, length);
        Assert.True(L.TryReadInteger(-2, out var first));
        Assert.Equal(0xC3, first);
        Assert.True(L.TryReadInteger(-1, out var last));
        Assert.Equal('x', last);
    }

    [Fact]
    public void A_string_longer_than_the_stack_buffer_is_pushed_through_the_pool()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        var text = string.Concat(Enumerable.Repeat("\u00E9\u20AC-", 1_000));

        L.PushString(text);

        Assert.Equal((nuint)Encoding.UTF8.GetByteCount(text), L.RawLength(-1));
        Assert.True(L.TryReadString(-1, out var read));
        Assert.Equal(text, read);
    }

    [Fact]
    public void Lone_surrogates_are_replaced_and_never_throw()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        L.PushString("a\uD800b");

        Assert.True(L.TryReadUtf8(-1, out var utf8));
        Assert.True(utf8.SequenceEqual(new byte[] { 0x61, 0xEF, 0xBF, 0xBD, 0x62 }));
    }

    [Fact]
    public void Empty_strings_round_trip()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        L.PushString(ReadOnlySpan<byte>.Empty);
        L.PushString(string.Empty);

        Assert.Equal(LuaType.String, L.TypeOf(1));
        Assert.Equal(LuaType.String, L.TypeOf(2));
        Assert.True(L.TryReadUtf8(1, out var a));
        Assert.True(a.IsEmpty);
        Assert.True(L.TryReadString(2, out var b));
        Assert.Equal(string.Empty, b);
    }

    [Fact]
    public void String_reads_are_strict_a_number_is_not_converted_in_place()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        L.PushInteger(42);

        Assert.False(L.TryReadUtf8(-1, out var utf8));
        Assert.True(utf8.IsEmpty);
        Assert.False(L.TryReadString(-1, out var text));
        Assert.Null(text);
        Assert.Equal(LuaType.Number, L.TypeOf(-1));
    }

    [Fact]
    public void Null_string_is_pushed_as_nil()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        StringMarshaller.Push(L, null);

        Assert.True(L.IsNil(-1));
    }

    [Fact]
    public void Invalid_utf8_from_lua_decodes_with_replacement_characters()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        L.PushString(new byte[] { 0x61, 0xFF, 0x62 });

        Assert.True(L.TryReadString(-1, out var text));
        Assert.Equal("a\uFFFDb", text);
    }
}
