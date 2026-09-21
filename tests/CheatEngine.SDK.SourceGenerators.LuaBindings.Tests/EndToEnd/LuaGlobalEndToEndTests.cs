using System.Text;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     The generated wrapper bodies run: <see cref="BindingSources.GlobalSuite" /> is compiled with its generated file,
///     loaded, and its methods are called through delegates against stand-in globals defined by a Lua chunk, through
///     the attached runtime, on every path (value, <c>nil</c>, wrong kind, raising global, missing global, the leading
///     state parameter, both string result shapes), with the zero-allocation gate on the warm Try form.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    private const string BindingsType = "Demo.Bindings";

    private static ReadOnlySpan<byte> StandIns => """
                                                  local memory = { [0x1000] = 42, [0x1004] = -7, [0x1008] = 0x100000000, [0x100C] = 2.5, [0x2000] = 'hello, world' }
                                                  function readInteger(address)
                                                    if address == 0xDEAD then error('access violation') end
                                                    return memory[address]
                                                  end
                                                  function readString(address, maxLength)
                                                    local s = memory[address]
                                                    if type(s) ~= 'string' then return nil end
                                                    return string.sub(s, 1, maxLength)
                                                  end
                                                  beeps = 0
                                                  function beep() beeps = beeps + 1 end
                                                  function isKeyPressed(key) return key == 13 end
                                                  function divide(a, b)
                                                    if b == 0 then return nil end
                                                    return a // b, a % b
                                                  end
                                                  function describe(value, flag) return 'v=' .. tostring(value) .. ' f=' .. tostring(flag), value * 2 end
                                                  function add(a, b) return a + b end
                                                  function upper(s) if s == nil then return nil end return string.upper(s) end
                                                  """u8;

    [Fact]
    public void Try_form_reads_values_and_reports_nil_wrong_kind_and_raising_global_as_false()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var tryRead = LoadSuite(roslyn).Delegate<TryReadInt32Delegate>(BindingsType, "TryReadInt32");

        Assert.True(tryRead(0x1000, out var value));
        Assert.Equal(42, value);
        Assert.True(tryRead(0x1004, out var negative));
        Assert.Equal(-7, negative);

        Assert.False(tryRead(0x2000, out var text)); // a string, not an integer
        Assert.Equal(0, text);
        Assert.False(tryRead(0x3000, out var missing)); // nil
        Assert.Equal(0, missing);
        Assert.False(tryRead(0x1008, out _)); // does not fit 32 bits
        Assert.False(tryRead(0x100C, out _)); // 2.5
        Assert.False(tryRead(0xDEAD, out var raised)); // error('access violation')
        Assert.Equal(0, raised);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Outcome_form_preserves_the_factual_lua_cause_without_reading_error_text()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        var detailed = LoadSuite(roslyn).Delegate<TryReadInt32DetailedDelegate>(BindingsType, "TryReadInt32Detailed");

        LuaOperationStatus missing;
        using (RuntimeScope missingScope = new(state))
        {
            missing = detailed(0x1000, out _);
        }

        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        LuaOperationStatus[] actual =
        [
            missing,
            detailed(0xDEAD, out _),
            detailed(0x3000, out _),
            detailed(0x100C, out _),
            detailed(0x1000, out var value),
        ];
        LuaOperationStatusKind[] expected =
        [
            LuaOperationStatusKind.GlobalUnavailable,
            LuaOperationStatusKind.LuaFailure,
            LuaOperationStatusKind.NilResult,
            LuaOperationStatusKind.InvalidResult,
            LuaOperationStatusKind.Success,
        ];

        for (var i = 0; i < actual.Length; i++) Assert.Equal(expected[i], actual[i].Kind);

        Assert.Equal(LuaStatus.RuntimeError, actual[1].LuaStatus);
        Assert.Equal(42, value);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Throwing_form_returns_the_value_and_throws_once_per_exit_with_the_lua_message()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        var read = LoadSuite(roslyn).Delegate<ReadInt32Delegate>(BindingsType, "ReadInt32");

        // Exit 1: the global is not defined yet.
        var unresolved = Assert.Throws<LuaException>(() => read(0x1000));
        Assert.Equal("The Lua global 'readInteger' is undefined or is not a function.", unresolved.Message);
        Assert.Equal(0, L.Top);

        LuaTest.Run(L, StandIns);
        Assert.Equal(42, read(0x1000));
        Assert.Equal(-7, read(0x1004));

        // Exit 2: the call raised.
        var raised = Assert.Throws<LuaException>(() => read(0xDEAD));
        Assert.Equal(LuaStatus.RuntimeError, raised.Status);
        Assert.Contains("access violation", raised.Message, StringComparison.Ordinal);

        // Exit 3: nil, or not an integer.
        Assert.Equal("The Lua global 'readInteger' returned a nil value, not an integer.",
            Assert.Throws<LuaException>(() => read(0x3000)).Message);
        Assert.Equal("The Lua global 'readInteger' returned a number value, not an integer.",
            Assert.Throws<LuaException>(() => read(0x100C)).Message);
        Assert.Equal("The Lua global 'readInteger' returned a string value, not an integer.",
            Assert.Throws<LuaException>(() => read(0x2000)).Message);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void String_results_come_back_copied_out_as_string_and_as_return_value()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var assembly = LoadSuite(roslyn);
        var copyOut = assembly.Delegate<TryReadStringCopyDelegate>(BindingsType, "TryReadString");
        var asString = assembly.Delegate<TryReadStringDelegate>(BindingsType, "TryReadString");
        var throwing = assembly.Delegate<ReadStringDelegate>(BindingsType, "ReadString");

        Span<byte> buffer = stackalloc byte[32];
        Assert.True(copyOut(0x2000, 5, buffer, out var written));
        Assert.Equal("hello", Encoding.UTF8.GetString(buffer[..written]));
        Assert.False(copyOut(0x2000, 100, buffer[..3], out var tooSmall)); // 12 bytes do not fit 3
        Assert.Equal(0, tooSmall);
        Assert.False(copyOut(0x1000, 5, buffer, out _)); // a number is not a string
        Assert.False(copyOut(0x3000, 5, buffer, out _)); // nil

        Assert.True(asString(0x2000, 12, out var text));
        Assert.Equal("hello, world", text);
        Assert.False(asString(0x3000, 12, out var missing));
        Assert.Null(missing);

        Assert.Equal("hello, w", throwing(0x2000, 8));
        Assert.Equal("The Lua global 'readString' returned a nil value, not a string.",
            Assert.Throws<LuaException>(() => throwing(0x3000, 8)).Message);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Void_and_boolean_throwing_forms_run_the_global()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var assembly = LoadSuite(roslyn);
        var beep = assembly.Delegate<BeepDelegate>(BindingsType, "Beep");
        var isKeyPressed = assembly.Delegate<IsKeyPressedDelegate>(BindingsType, "IsKeyPressed");

        beep();
        beep();
        Assert.Equal(2, LuaTest.RunForInteger(L, "return beeps"u8));
        Assert.True(isKeyPressed(13));
        Assert.False(isKeyPressed(27));
        Assert.Equal(0, L.Top);

        // A boolean wrapper is strict: a nil result is not false, it is an unexpected result.
        LuaTest.Run(L, "function isKeyPressed(key) return nil end"u8);
        using (new RuntimeScope(state))
        {
            // A fresh epoch, so the cached function is resolved again and sees the redefinition.
            Assert.Equal("The Lua global 'isKeyPressed' returned a nil value, not a boolean.",
                Assert.Throws<LuaException>(() => isKeyPressed(13)).Message);
        }
    }

    [Fact]
    public void Several_results_are_read_and_all_defaulted_together_on_failure()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var assembly = LoadSuite(roslyn);
        var divide = assembly.Delegate<TryDivideDelegate>(BindingsType, "TryDivide");
        var describe = assembly.Delegate<TryDescribeDelegate>(BindingsType, "TryDescribe");

        Assert.True(divide(17, 5, out var quotient, out var remainder));
        Assert.Equal(3, quotient);
        Assert.Equal(2, remainder);

        // divide(17, 0) returns a single nil: quotient fails to read, and the remainder is defaulted with it.
        Assert.False(divide(17, 0, out var noQuotient, out var noRemainder));
        Assert.Equal(0, noQuotient);
        Assert.Equal(0, noRemainder);

        Assert.True(describe(1.5, true, out var text, out var doubled));
        Assert.Equal("v=1.5 f=true", text);
        Assert.Equal(3.0, doubled);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Leading_state_parameter_is_used_instead_of_the_runtime()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        LuaTest.Run(L, StandIns);
        var assembly = LoadSuite(roslyn);
        var add = assembly.Delegate<AddOnDelegate>(BindingsType, "AddOn");
        var tryAdd = assembly.Delegate<TryAddOnDelegate>(BindingsType, "TryAddOn");

        // The runtime must be attached for the epoch-checked cache, but the state comes from the argument.
        using RuntimeScope scope = new(state);
        Assert.Equal(5, add(L, 2, 3));
        Assert.True(tryAdd(L, 40, 2, out var sum));
        Assert.Equal(42, sum);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Utf8_and_nullable_string_arguments_are_pushed()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var assembly = LoadSuite(roslyn);
        var upper = assembly.Delegate<UpperDelegate>(BindingsType, "Upper");
        var upperOrNull = assembly.Delegate<UpperOrNullDelegate>(BindingsType, "UpperOrNull");

        Assert.Equal("ABC", upper("abc"u8));
        Assert.Equal("XYZ", upperOrNull("xyz"));

        // null is pushed as nil; the stand-in returns nil, which the string? wrapper still reports as unexpected.
        Assert.Equal("The Lua global 'upper' returned a nil value, not a string.",
            Assert.Throws<LuaException>(() => upperOrNull(null)).Message);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Wrappers_throw_while_the_runtime_is_detached()
    {
        LuaTest.RequireNativeLua();
        LuaRuntime.Detach();
        var tryRead = LoadSuite(roslyn).Delegate<TryReadInt32Delegate>(BindingsType, "TryReadInt32");

        Assert.Throws<InvalidOperationException>(() => tryRead(0x1000, out _));
    }

    [Fact]
    public void Warm_try_form_allocates_nothing()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var tryRead = LoadSuite(roslyn).Delegate<TryReadInt32Delegate>(BindingsType, "TryReadInt32");
        long sink = 0;

        AllocationGate.AssertZero(() =>
        {
            if (!tryRead(0x1000, out var value) || value != 42) throw new InvalidOperationException("wrong value");

            if (tryRead(0x3000, out _)) throw new InvalidOperationException("unexpected value");

            sink += value;
        });

        Assert.NotEqual(0, sink);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Warm_copy_out_string_form_allocates_nothing()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIns);
        var copyOut = LoadSuite(roslyn).Delegate<TryReadStringCopyDelegate>(BindingsType, "TryReadString");
        var buffer = new byte[32];

        AllocationGate.AssertZero(() =>
        {
            if (!copyOut(0x2000, 5, buffer, out var written) || written != 5)
                throw new InvalidOperationException("wrong value");
        });
    }

    private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
    {
        return GeneratedAssembly.Load(roslyn.Run(BindingSources.GlobalSuite));
    }

    private delegate bool TryReadInt32Delegate(nuint address, out int value);

    private delegate LuaOperationStatus TryReadInt32DetailedDelegate(nuint address, out int value);

    private delegate int ReadInt32Delegate(nuint address);

    private delegate bool TryReadStringCopyDelegate(nuint address, int maxLength, Span<byte> destination,
        out int written);

    private delegate bool TryReadStringDelegate(nuint address, int maxLength, out string? value);

    private delegate string ReadStringDelegate(nuint address, int maxLength);

    private delegate void BeepDelegate();

    private delegate bool IsKeyPressedDelegate(int key);

    private delegate bool TryDivideDelegate(long dividend, long divisor, out long quotient, out long remainder);

    private delegate bool TryDescribeDelegate(double value, bool flag, out string? text, out double doubled);

    private delegate long AddOnDelegate(LuaState state, long a, long b);

    private delegate bool TryAddOnDelegate(LuaState state, long a, long b, out long sum);

    private delegate string UpperDelegate(ReadOnlySpan<byte> text);

    private delegate string? UpperOrNullDelegate(string? text);
}
