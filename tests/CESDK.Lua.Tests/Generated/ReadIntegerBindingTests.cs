using CESDK.Lua.Calls;
using CESDK.Lua.Runtime;
using CESDK.Lua.Tests.Support;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Lua.Tests.Generated;

/// <summary>
///     The worked example of the generated call shape runs: <see cref="MemoryBindings.TryReadInt32" /> against a
///     stand-in <c>readInteger</c> defined by a Lua chunk, through the attached runtime, on every path.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class ReadIntegerBindingTests
{
    private static ReadOnlySpan<byte> StandIn => """
                                                 local memory = { [0x1000] = 42, [0x1004] = -7, [0x1008] = 0x100000000, [0x100C] = 2.5 }
                                                 function readInteger(address)
                                                   if address == 0xDEAD then error('access violation') end
                                                   return memory[address]
                                                 end
                                                 """u8;

    [Fact]
    public void Reads_a_value_the_stand_in_knows()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIn);

        Assert.True(MemoryBindings.TryReadInt32(0x1000, out var value));

        Assert.Equal(42, value);
        Assert.True(MemoryBindings.TryReadInt32(0x1004, out var negative));
        Assert.Equal(-7, negative);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Nil_result_is_false_without_an_exception()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIn);

        Assert.False(MemoryBindings.TryReadInt32(0x2000, out var value));

        Assert.Equal(0, value);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void A_value_that_does_not_fit_or_is_not_an_integer_is_false()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIn);

        Assert.False(MemoryBindings.TryReadInt32(0x1008, out _));
        Assert.False(MemoryBindings.TryReadInt32(0x100C, out _));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void A_raising_global_is_false_and_leaves_the_stack_balanced()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIn);

        Assert.False(MemoryBindings.TryReadInt32(0xDEAD, out var value));

        Assert.Equal(0, value);
        Assert.Equal(0, L.Top);
        Assert.True(MemoryBindings.TryReadInt32(0x1000, out var after));
        Assert.Equal(42, after);
    }

    [Fact]
    public void A_missing_global_is_false_and_is_looked_up_again_once_defined()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);

        Assert.False(MemoryBindings.TryReadInt32(0x1000, out _));
        Assert.Equal(0, L.Top);

        LuaTest.Run(L, StandIn);
        Assert.True(MemoryBindings.TryReadInt32(0x1000, out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void The_cached_reference_is_re_resolved_after_the_host_re_attaches()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState first = new();
        using NativeLuaState second = new();
        var L1 = LuaTest.View(first);
        var L2 = LuaTest.View(second);
        using (new RuntimeScope(first))
        {
            LuaTest.Run(L1, StandIn);
            Assert.True(MemoryBindings.TryReadInt32(0x1000, out var value));
            Assert.Equal(42, value);
        }

        // A different state, a new epoch: the slot cached for the first state must not be used against the second.
        using (new RuntimeScope(second))
        {
            LuaTest.Run(L2, "function readInteger(address) return address + 1 end"u8);
            Assert.True(MemoryBindings.TryReadInt32(0x1000, out var value));
            Assert.Equal(0x1001, value);
            Assert.Equal(0, L2.Top);
        }
    }

    [Fact]
    public void The_throwing_form_returns_the_value_and_leaves_the_stack_balanced()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIn);

        Assert.Equal(42, MemoryBindings.ReadInt32(0x1000));
        Assert.Equal(-7, MemoryBindings.ReadInt32(0x1004));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void The_throwing_form_has_one_exception_per_exit_and_names_the_type_of_an_unexpected_result()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);

        // Exit 1: the global is not defined yet.
        var unresolved = Assert.Throws<LuaException>(() => MemoryBindings.ReadInt32(0x1000));
        Assert.Contains("'readInteger'", unresolved.Message, StringComparison.Ordinal);
        Assert.Contains("undefined", unresolved.Message, StringComparison.Ordinal);
        Assert.Equal(0, L.Top);

        LuaTest.Run(L, StandIn);

        // Exit 2: the call raised; the Lua message travels with the status.
        var raised = Assert.Throws<LuaException>(() => MemoryBindings.ReadInt32(0xDEAD));
        Assert.Equal(LuaStatus.RuntimeError, raised.Status);
        Assert.Contains("access violation", raised.Message, StringComparison.Ordinal);
        Assert.Equal(0, L.Top);

        // Exit 3: the call succeeded but the result is nil (unknown address) or not an integer (2.5): the message
        // names the Lua type actually received.
        var nil = Assert.Throws<LuaException>(() => MemoryBindings.ReadInt32(0x2000));
        Assert.Equal("The Lua global 'readInteger' returned a nil value, not an integer.", nil.Message);
        var fraction = Assert.Throws<LuaException>(() => MemoryBindings.ReadInt32(0x100C));
        Assert.Equal("The Lua global 'readInteger' returned a number value, not an integer.", fraction.Message);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Throws_while_the_plugin_is_not_enabled()
    {
        LuaTest.RequireNativeLua();
        LuaRuntime.Detach();

        Assert.Throws<InvalidOperationException>(() => MemoryBindings.TryReadInt32(0x1000, out _));
    }

    [Fact]
    public void The_generated_shape_allocates_nothing()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        LuaTest.Run(L, StandIn);
        long sink = 0;

        AllocationGate.AssertZero(() =>
        {
            if (!MemoryBindings.TryReadInt32(0x1000, out var value) || value != 42)
                throw new InvalidOperationException("wrong value");

            if (MemoryBindings.TryReadInt32(0x2000, out _)) throw new InvalidOperationException("unexpected value");

            sink += value;
        });

        Assert.NotEqual(0, sink);
        Assert.Equal(0, L.Top);
    }
}
