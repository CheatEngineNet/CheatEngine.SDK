using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     The generated thunks run: <see cref="BindingSources.FunctionSuite" /> is compiled with its generated file, loaded,
///     registered on a real Lua 5.3 state through the generated <c>RegisterLuaFunctions</c>, and called from Lua chunks
///     on every path (values, wrong argument kinds and counts, a throwing target, the state parameter, unregistration).
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaFunctionEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    private const string SuiteType = "Demo.Suite";

    [Fact]
    public void Registered_functions_are_callable_from_lua_with_marshalled_values()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        var assembly = LoadSuite(roslyn);

        Register(assembly, L);

        Assert.Equal(3, LuaTest.RunForInteger(L, "return add(1, 2)"u8));
        Assert.Equal("hello, Lua", LuaTest.RunForString(L, "return greet('Lua')"u8));
        Assert.Equal("abc", LuaTest.RunForString(L, "return echo('abc')"u8));
        Assert.Equal("2.5", LuaTest.RunForString(L, "return tostring(half(5))"u8));
        Assert.Equal("true", LuaTest.RunForString(L, "return tostring(negate(false))"u8));
        Assert.Equal(0x1004, LuaTest.RunForInteger(L, "return step(0x1000)"u8));
        Assert.Equal(7, LuaTest.RunForInteger(L, "return small(7)"u8));
        Assert.Equal("3.0", LuaTest.RunForString(L, "return tostring(scale(1.5))"u8));
        Assert.Equal("yes", LuaTest.RunForString(L, "return maybe(true)"u8));
        Assert.Equal("nil", LuaTest.RunForString(L, "return tostring(maybe(false))"u8));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Void_function_returns_no_value_and_runs_the_target()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        var assembly = LoadSuite(roslyn);
        Register(assembly, L);

        Assert.Equal(0, LuaTest.RunForInteger(L, "return select('#', ping())"u8));
        LuaTest.Run(L, "ping() ping()"u8);

        var pings = assembly.Assembly.GetType(SuiteType, true)!.GetField("Pings")!;
        Assert.Equal(3, (int)pings.GetValue(null)!);
    }

    [Fact]
    public void State_parameter_receives_the_callback_state()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        Register(LoadSuite(roslyn), L);

        // IsInteger(LuaState L, double value) asks the state whether argument 1 is an integer subtype: only the
        // state Lua passed can answer that, and the state is not counted as a Lua argument.
        Assert.Equal("true", LuaTest.RunForString(L, "return tostring(isint(3))"u8));
        Assert.Equal("false", LuaTest.RunForString(L, "return tostring(isint(3.5))"u8));
    }

    [Fact]
    public void Wrong_argument_kind_is_a_catchable_lua_error_naming_the_argument_and_the_received_type()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        Register(LoadSuite(roslyn), L);

        // Not tail calls on purpose: 'error(message, 2)' blames the caller of the thunk, which a tail call would erase.
        Assert.Equal("test:1: bad argument #1 (integer expected, got string)",
            LuaTest.RunForError(L, "return pcall(function() add('x', 2) end)"u8));
        Assert.Equal("test:1: bad argument #2 (integer expected, got nil)",
            LuaTest.RunForError(L, "return pcall(function() add(1, nil) end)"u8));
        Assert.Equal("test:1: bad argument #1 (string expected, got number)",
            LuaTest.RunForError(L, "return pcall(function() greet(42) end)"u8));
        Assert.Equal("test:1: bad argument #1 (boolean expected, got nil)",
            LuaTest.RunForError(L, "return pcall(function() negate(nil) end)"u8));
        Assert.Equal("test:1: bad argument #1 (integer expected, got number)",
            LuaTest.RunForError(L, "return pcall(function() small(2.5) end)"u8));
        Assert.Equal("test:1: bad argument #1 (integer expected, got number)",
            LuaTest.RunForError(L, "return pcall(function() small(2^40) end)"u8));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Wrong_argument_count_is_a_catchable_lua_error_naming_the_function()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        Register(LoadSuite(roslyn), L);

        Assert.Equal("test:1: wrong number of arguments to 'add' (2 expected)",
            LuaTest.RunForError(L, "return pcall(function() add(1) end)"u8));
        Assert.Equal("test:1: wrong number of arguments to 'add' (2 expected)",
            LuaTest.RunForError(L, "return pcall(function() add(1, 2, 3) end)"u8));
        Assert.Equal("test:1: wrong number of arguments to 'ping' (0 expected)",
            LuaTest.RunForError(L, "return pcall(function() ping(1) end)"u8));
        Assert.Equal("test:1: wrong number of arguments to 'isint' (1 expected)",
            LuaTest.RunForError(L, "return pcall(function() isint() end)"u8));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Throwing_target_is_a_catchable_lua_error_carrying_the_exception()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        Register(LoadSuite(roslyn), L);

        Assert.Equal("test:1: System.InvalidOperationException: managed boom",
            LuaTest.RunForError(L, "return pcall(function() boom() end)"u8));

        // The state is intact afterwards: the next call works.
        Assert.Equal(3, LuaTest.RunForInteger(L, "return add(1, 2)"u8));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Unregister_removes_the_globals_and_registration_can_run_again()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        var assembly = LoadSuite(roslyn);
        Register(assembly, L);
        Assert.Equal("function", LuaTest.RunForString(L, "return type(add)"u8));

        var unregistered = Invoke(assembly, "UnregisterLuaFunctions", L);

        Assert.True(unregistered.IsOk);
        Assert.Equal(0, L.Top);
        Assert.Equal("nil", LuaTest.RunForString(L, "return type(add)"u8));
        Assert.Equal("nil", LuaTest.RunForString(L, "return type(greet)"u8));

        Register(assembly, L);
        Assert.Equal(3, LuaTest.RunForInteger(L, "return add(1, 2)"u8));
    }

    [Fact]
    public void Registration_failure_follows_the_status_protocol()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        var assembly = LoadSuite(roslyn);

        // A globals table whose __newindex raises: the first TrySetGlobal fails, the error value is on top.
        LuaTest.Run(L, "setmetatable(_G, { __newindex = function(t, k, v) error('sealed: ' .. k) end })"u8);
        var status = Invoke(assembly, "RegisterLuaFunctions", L);

        Assert.Equal(LuaStatus.RuntimeError, status);
        Assert.Equal(1, L.Top);
        Assert.Contains("sealed: add", LuaTest.ReadString(L, -1), StringComparison.Ordinal);
    }

    [Fact]
    public void Nil_is_rejected_for_a_nullable_string_argument_like_any_other_string_argument()
    {
        // Documented, deliberate behavior, not a gap: CheatEngine.SDK.Lua's string reads are strict (TryReadUtf8 checks
        // LUA_TSTRING; nil is "not a string" regardless of the C# parameter's nullable annotation), and a thunk uses
        // the same marshaller for 'string' and 'string?' arguments (StringMarshaller.TryRead does not consult
        // IsNullable - only the emitted local's declared type does, and that is always 'string?' for flow purposes).
        // A nullable string parameter therefore rejects nil exactly like a non-nullable one: 'nil' means "no value",
        // not "the null string", uniformly with how a [LuaGlobal] Try form treats a nil result as call failure.
        const string Source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              public static partial class NullableArgs
                              {
                                  [LuaFunction("describe")]
                                  public static string Describe(string? name) => name is null ? "nobody" : "hello, " + name;
                              }
                              """;

        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        var assembly = GeneratedAssembly.Load(roslyn.Run(Source));
        var registered = (LuaStatus)assembly.Method("Demo.NullableArgs", "RegisterLuaFunctions").Invoke(null, [L])!;
        Assert.True(registered.IsOk);

        Assert.Equal("hello, Lua", LuaTest.RunForString(L, "return describe('Lua')"u8));
        Assert.Equal("test:1: bad argument #1 (string expected, got nil)",
            LuaTest.RunForError(L, "return pcall(function() describe(nil) end)"u8));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Calling_a_thunk_from_lua_allocates_nothing_on_the_managed_side()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        Register(LoadSuite(roslyn), L);
        LuaTest.Run(L, "function loop() local s = 0 for i = 1, 100 do s = add(s, i) end return s end"u8);

        AllocationGate.AssertZero(() =>
        {
            using LuaFrame frame = new(L);
            var status = L.TryGetGlobal("loop"u8);
            if (!status.IsOk || !L.TryCall(0, 1).IsOk || !L.TryReadInteger(-1, out var sum) || sum != 5050)
                throw new InvalidOperationException("the loop did not run");
        });
    }

    private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
    {
        return GeneratedAssembly.Load(roslyn.Run(BindingSources.FunctionSuite));
    }

    private static void Register(GeneratedAssembly assembly, LuaState L)
    {
        var status = Invoke(assembly, "RegisterLuaFunctions", L);
        Assert.True(status.IsOk,
            "Registration failed: " + (status.IsOk ? string.Empty : LuaError.FromStack(L, status).ToString()));
        Assert.Equal(0, L.Top);
    }

    private static LuaStatus Invoke(GeneratedAssembly assembly, string methodName, LuaState L)
    {
        return (LuaStatus)assembly.Method(SuiteType, methodName).Invoke(null, [L])!;
    }
}
