using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.References;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>
///     The list of live callbacks: unlinking works from any position and takes the gate itself, so it does not depend
///     on what the caller holds. No Lua library involved: a callback here has no closure and no state.
/// </summary>
public sealed class LuaCallbackRegistryTests
{
    [Fact]
    public void Remove_unlinks_the_head_a_middle_callback_and_the_tail()
    {
        var tail = NewCallback();
        var middle = NewCallback();
        var head = NewCallback();
        LuaCallbackRegistry.Add(tail);
        LuaCallbackRegistry.Add(middle);
        LuaCallbackRegistry.Add(head);
        try
        {
            Assert.Equal(3, LuaCallbackRegistry.Count);

            LuaCallbackRegistry.Remove(middle);

            Assert.Equal(2, LuaCallbackRegistry.Count);
            Assert.False(middle.IsLinked);
            Assert.Null(middle.Next);
            Assert.Null(middle.Previous);
            Assert.Same(tail, head.Next);
            Assert.Same(head, tail.Previous);

            LuaCallbackRegistry.Remove(tail);

            Assert.Equal(1, LuaCallbackRegistry.Count);
            Assert.Null(head.Next);

            LuaCallbackRegistry.Remove(head);

            Assert.Equal(0, LuaCallbackRegistry.Count);
            Assert.False(head.IsLinked);

            // Not linked any more: nothing to do, and the list is not touched.
            LuaCallbackRegistry.Remove(head);
            Assert.Equal(0, LuaCallbackRegistry.Count);
        }
        finally
        {
            LuaCallbackRegistry.Remove(head);
            LuaCallbackRegistry.Remove(middle);
            LuaCallbackRegistry.Remove(tail);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Remove_takes_the_gate_itself_for_the_head_and_for_any_other_position(bool removeHead)
    {
        var first = NewCallback();
        var second = NewCallback();
        LuaCallbackRegistry.Add(first);
        LuaCallbackRegistry.Add(second);
        var target = removeHead ? second : first;
        Thread worker = new(() => LuaCallbackRegistry.Remove(target));
        try
        {
            lock (LuaCallbackRegistry.Gate)
            {
                worker.Start();

                // The worker cannot finish while this thread holds the gate. A branch that did not take it would be
                // done in microseconds.
                Assert.False(worker.Join(TimeSpan.FromMilliseconds(200)));
                Assert.True(target.IsLinked);
            }

            Assert.True(worker.Join(TimeSpan.FromSeconds(30)));
            Assert.False(target.IsLinked);
            Assert.Equal(1, LuaCallbackRegistry.Count);
        }
        finally
        {
            LuaCallbackRegistry.Remove(first);
            LuaCallbackRegistry.Remove(second);
        }
    }

    [Fact]
    public void Release_and_Remove_enter_the_gate_again_on_a_thread_that_holds_it()
    {
        var callback = NewCallback();
        var other = NewCallback();
        LuaCallbackRegistry.Add(callback);
        LuaCallbackRegistry.Add(other);
        try
        {
            lock (LuaCallbackRegistry.Gate)
            {
                callback.Release(default);
                Assert.True(callback.IsReleased);
                Assert.False(callback.IsLinked);

                LuaCallbackRegistry.Remove(other);
                Assert.False(other.IsLinked);
            }

            Assert.Equal(0, LuaCallbackRegistry.Count);
        }
        finally
        {
            LuaCallbackRegistry.Remove(callback);
            LuaCallbackRegistry.Remove(other);
        }
    }

    private static LuaCallback<object> NewCallback()
    {
        return new LuaCallback<object>(default, new LuaRef(), new LuaRef());
    }
}
