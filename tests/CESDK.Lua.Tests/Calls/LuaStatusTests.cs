using CESDK.Lua.Calls;
using CESDK.Lua.Interop.Api;

namespace CESDK.Lua.Tests.Calls;

/// <summary>The status value type against the C API constants. No Lua library involved.</summary>
public sealed class LuaStatusTests
{
    [Fact]
    public void Named_statuses_carry_the_c_api_codes()
    {
        Assert.Equal(LuaApi.LUA_OK, LuaStatus.Ok.Code);
        Assert.Equal(LuaApi.LUA_YIELD, LuaStatus.Yield.Code);
        Assert.Equal(LuaApi.LUA_ERRRUN, LuaStatus.RuntimeError.Code);
        Assert.Equal(LuaApi.LUA_ERRSYNTAX, LuaStatus.SyntaxError.Code);
        Assert.Equal(LuaApi.LUA_ERRMEM, LuaStatus.MemoryError.Code);
        Assert.Equal(LuaApi.LUA_ERRGCMM, LuaStatus.GcMetamethodError.Code);
        Assert.Equal(LuaApi.LUA_ERRERR, LuaStatus.MessageHandlerError.Code);
        Assert.Equal(LuaApi.LUA_ERRFILE, LuaStatus.FileError.Code);
    }

    [Fact]
    public void Only_ok_is_ok()
    {
        Assert.True(LuaStatus.Ok.IsOk);
        Assert.True(default(LuaStatus).IsOk);
        Assert.False(LuaStatus.RuntimeError.IsOk);
        Assert.False(new LuaStatus(99).IsOk);
    }

    [Fact]
    public void Equality_is_by_code()
    {
        Assert.Equal(LuaStatus.RuntimeError, new LuaStatus(2));
        Assert.True(LuaStatus.RuntimeError == new LuaStatus(2));
        Assert.True(LuaStatus.RuntimeError != LuaStatus.SyntaxError);
        Assert.Equal(LuaStatus.RuntimeError.GetHashCode(), new LuaStatus(2).GetHashCode());
    }

    [Fact]
    public void ToString_names_known_codes_and_prints_unknown_ones()
    {
        Assert.Equal("LUA_OK", LuaStatus.Ok.ToString());
        Assert.Equal("LUA_ERRRUN", LuaStatus.RuntimeError.ToString());
        Assert.Equal("LUA_ERRGCMM", LuaStatus.GcMetamethodError.ToString());
        Assert.Equal("42", new LuaStatus(42).ToString());
    }

    [Fact]
    public void ThrowIfFailed_does_nothing_for_ok_even_without_a_state()
    {
        LuaStatus.Ok.ThrowIfFailed(default);
    }

    [Fact]
    public void ThrowIfFailed_throws_a_lua_exception_for_a_failure_and_describes_a_missing_error_value()
    {
        var exception = Assert.Throws<LuaException>(() => LuaStatus.RuntimeError.ThrowIfFailed(default));

        Assert.Equal(LuaStatus.RuntimeError, exception.Status);
        Assert.Contains("no error value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaError_equality_and_text()
    {
        LuaError first = new(LuaStatus.RuntimeError, "boom");
        LuaError second = new(LuaStatus.RuntimeError, "boom");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.True(first != new LuaError(LuaStatus.SyntaxError, "boom"));
        Assert.Equal("LUA_ERRRUN: boom", first.ToString());
        Assert.Equal(string.Empty, new LuaError(LuaStatus.Ok, null!).Message);
    }

    [Fact]
    public void LuaException_from_error_carries_status_and_message()
    {
        var exception = Assert.Throws<LuaException>(() =>
            LuaException.Throw(new LuaError(LuaStatus.SyntaxError, "unexpected symbol")));

        Assert.Equal(LuaStatus.SyntaxError, exception.Status);
        Assert.Equal("unexpected symbol", exception.Message);
        Assert.True(new LuaException("plain").Status.IsOk);
    }
}
