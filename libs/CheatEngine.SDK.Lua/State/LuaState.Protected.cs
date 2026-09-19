using System;
using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Protected;
using CheatEngine.SDK.Lua.Text;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Protected operations: metamethods and allocating argument pushes run behind native lua_pcallk boundaries.
// Each member documents its stack effect on success; on failure every member
// leaves exactly one error value on top of the stack in place of its results, and the returned LuaStatus says why.
//
// The helper functions (LuaHelpers) are plain Lua functions stored in the state's registry, so a raise inside them
// unwinds Lua frames only. Cost per operation: one lua_rawgetp for the helper, the argument pushes, one lua_pcallk,
// plus the Lua call itself. Key allocation uses the native bridge, including errors from pending finalizers.
public readonly unsafe partial struct LuaState
{
    /// <summary>
    ///     Calls the function below the arguments under protection (<c>lua_pcallk</c> with no message handler).
    ///     Stack before: <c>f, arg1 .. argN</c>; after success: the results (<paramref name="resultCount" /> of them, or
    ///     all with <see cref="MultipleResults" />); after failure: one error value.
    /// </summary>
    /// <param name="argumentCount">Number of arguments above the function.</param>
    /// <param name="resultCount">
    ///     Number of results to keep, adjusted with <c>nil</c>s or truncated, or
    ///     <see cref="MultipleResults" />.
    /// </param>
    /// <returns>The status; <see cref="LuaStatus.IsOk" /> when the results are on the stack.</returns>
    /// <remarks>
    ///     A managed callback reached by the call runs on this thread and this state; its exceptions come back as a
    ///     runtime error through the error channel, never as a managed exception.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaStatus TryCall(int argumentCount, int resultCount)
    {
        return new LuaStatus(lua_pcallk(Pointer, argumentCount, resultCount, 0, 0, null));
    }

    /// <summary>
    ///     Calls the function below the arguments under protection, with a message handler (<c>lua_pcallk</c>): on error
    ///     the handler at <paramref name="messageHandlerIndex" /> is called with the error value and its result becomes
    ///     the error value on the stack, which is how a traceback is attached.
    /// </summary>
    /// <param name="argumentCount">Number of arguments above the function.</param>
    /// <param name="resultCount">Number of results to keep, or <see cref="MultipleResults" />.</param>
    /// <param name="messageHandlerIndex">A valid index of the handler function, below the called function.</param>
    /// <returns>The status.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaStatus TryCall(int argumentCount, int resultCount, int messageHandlerIndex)
    {
        return new LuaStatus(lua_pcallk(Pointer, argumentCount, resultCount, messageHandlerIndex, 0, null));
    }

    /// <summary>
    ///     Compiles a text chunk without running it (<c>luaL_loadbufferx</c> in text mode, so a precompiled chunk is
    ///     rejected). Stack after success: the chunk as a function; after failure: the syntax error message.
    /// </summary>
    /// <param name="source">The Lua source, UTF-8.</param>
    /// <param name="chunkName">
    ///     Name used in error messages, UTF-8; prefix with <c>=</c> to use it verbatim. Empty means
    ///     <c>=?</c>; longer than 511 bytes is truncated.
    /// </param>
    /// <returns>The status: <see cref="LuaStatus.SyntaxError" /> or <see cref="LuaStatus.MemoryError" /> on failure.</returns>
    /// <remarks>Loading never runs Lua code. The chunk's <c>_ENV</c> is the globals table.</remarks>
    [LuaStackEffect(1)]
    [SkipLocalsInit] // The name buffer is written (copy + NUL) before Lua reads it; zeroing 512 bytes first would be dead stores.
    public LuaStatus TryLoad(ReadOnlySpan<byte> source, ReadOnlySpan<byte> chunkName = default)
    {
        byte empty = 0;
        fixed (byte* text = source)
        fixed (byte* mode = "t"u8)
        {
            if (chunkName.IsEmpty)
                fixed (byte* defaultName = "=?"u8)
                {
                    return new LuaStatus(luaL_loadbufferx(Pointer, text is null ? &empty : text, (nuint)source.Length,
                        defaultName, mode));
                }

            // The chunk name must be NUL-terminated; a u8 literal is, an arbitrary span is not. Copy it to be sure.
            Span<byte> name = stackalloc byte[Utf8Scratch.StackBufferSize];
            var length = Math.Min(chunkName.Length, name.Length - 1);
            chunkName[..length].CopyTo(name);
            name[length] = 0;
            fixed (byte* namePointer = name)
            {
                return new LuaStatus(luaL_loadbufferx(Pointer, text is null ? &empty : text, (nuint)source.Length,
                    namePointer, mode));
            }
        }
    }

    /// <summary>
    ///     Compiles and runs a text chunk under protection: <see cref="TryLoad" /> then <see cref="TryCall(int, int)" />
    ///     with no arguments. Stack after success: the results; after failure: one error value.
    /// </summary>
    /// <param name="source">The Lua source, UTF-8.</param>
    /// <param name="resultCount">Number of results to keep, or <see cref="MultipleResults" />.</param>
    /// <param name="chunkName">Name used in error messages; see <see cref="TryLoad" />.</param>
    /// <returns>The status of the load or of the run.</returns>
    public LuaStatus TryExecute(ReadOnlySpan<byte> source, int resultCount, ReadOnlySpan<byte> chunkName = default)
    {
        var status = TryLoad(source, chunkName);
        return status.IsOk ? TryCall(0, resultCount) : status;
    }

    /// <summary>
    ///     Pushes the global <paramref name="name" /> under protection (<c>_ENV[name]</c>, honouring an <c>__index</c>
    ///     metamethod on the globals table). Stack after success: the value (<c>nil</c> when undefined).
    /// </summary>
    /// <param name="name">The global name, UTF-8; a <c>"..."u8</c> literal for a fixed name.</param>
    /// <returns>The status.</returns>
    public LuaStatus TryGetGlobal(ReadOnlySpan<byte> name)
    {
        var top = Top;
        var status = LuaHelpers.Push(Pointer, LuaHelper.GetGlobal);
        if (!status.IsOk) return status;

        status = TryPushString(name);
        if (!status.IsOk) return KeepProtectedError(top, status);
        return TryCall(1, 1);
    }

    /// <summary>
    ///     Pops the value on top and assigns it to the global <paramref name="name" /> under protection (<c>_ENV[name] = v</c>
    ///     ,
    ///     honouring a <c>__newindex</c> metamethod on the globals table). Stack after success: the value is gone.
    /// </summary>
    /// <param name="name">The global name, UTF-8.</param>
    /// <returns>The status.</returns>
    public LuaStatus TrySetGlobal(ReadOnlySpan<byte> name)
    {
        var top = Top - 1;
        // [.. v] -> [.. v helper] -> [.. v helper k] -> [.. helper k v]
        var status = LuaHelpers.Push(Pointer, LuaHelper.SetGlobal);
        if (!status.IsOk)
        {
            lua_rotate(Pointer, -2, 1);
            lua_settop(Pointer, -2);
            return status;
        }

        status = TryPushString(name);
        if (!status.IsOk) return KeepProtectedError(top, status);
        lua_rotate(Pointer, -3, -1);
        return TryCall(2, 0);
    }

    /// <summary>
    ///     Pushes <c>o[key]</c> for the value at <paramref name="index" /> and a string key, under protection (this is how
    ///     a Cheat Engine object's property is read: its <c>__index</c> runs inside the call). Stack after success: the value.
    /// </summary>
    /// <param name="index">A valid index of the object; a relative index is taken before anything is pushed.</param>
    /// <param name="key">The key, UTF-8.</param>
    /// <returns>The status.</returns>
    public LuaStatus TryGetField(int index, ReadOnlySpan<byte> key)
    {
        var top = Top;
        var status = LuaHelpers.Push(Pointer, LuaHelper.Index);
        if (!status.IsOk) return status;

        lua_pushvalue(Pointer, Shift(index, 1));
        status = TryPushString(key);
        if (!status.IsOk) return KeepProtectedError(top, status);
        return TryCall(2, 1);
    }

    /// <summary>
    ///     Pops the value on top and does <c>o[key] = v</c> for the value at <paramref name="index" /> and a string key,
    ///     under protection (a Cheat Engine property write: its <c>__newindex</c> runs inside the call).
    /// </summary>
    /// <param name="index">
    ///     A valid index of the object, which must not be the top (the top is the value); a relative index is
    ///     taken before anything is pushed.
    /// </param>
    /// <param name="key">The key, UTF-8.</param>
    /// <returns>The status.</returns>
    public LuaStatus TrySetField(int index, ReadOnlySpan<byte> key)
    {
        var top = Top - 1;
        // [.. v] -> [.. v h] -> [.. v h o] -> [.. v h o k] -> [.. h o k v]
        var status = LuaHelpers.Push(Pointer, LuaHelper.NewIndex);
        if (!status.IsOk)
        {
            lua_rotate(Pointer, -2, 1);
            lua_settop(Pointer, -2);
            return status;
        }

        lua_pushvalue(Pointer, Shift(index, 1));
        status = TryPushString(key);
        if (!status.IsOk) return KeepProtectedError(top, status);
        lua_rotate(Pointer, -4, -1);
        return TryCall(3, 0);
    }

    internal LuaStatus KeepProtectedError(int top, LuaStatus status)
    {
        Copy(-1, top + 1);
        SetTop(top + 1);
        return status;
    }

    /// <summary>
    ///     Pops the key on top and pushes <c>o[key]</c> for the value at <paramref name="index" />, under protection, for
    ///     keys of any type. Stack after success: the value in place of the key.
    /// </summary>
    /// <param name="index">
    ///     A valid index of the object, which must not be the top (the top is the key); a relative index is
    ///     taken before anything is pushed.
    /// </param>
    /// <returns>The status.</returns>
    public LuaStatus TryGetTable(int index)
    {
        // [.. k] -> [.. k h] -> [.. k h o] -> [.. h o k]
        var status = LuaHelpers.Push(Pointer, LuaHelper.Index);
        if (!status.IsOk)
        {
            lua_rotate(Pointer, -2, 1);
            lua_settop(Pointer, -2);
            return status;
        }

        lua_pushvalue(Pointer, Shift(index, 1));
        lua_rotate(Pointer, -3, -1);
        return TryCall(2, 1);
    }

    /// <summary>
    ///     Pops the value on top and the key below it and does <c>o[key] = v</c> for the value at <paramref name="index" />,
    ///     under protection, for keys of any type.
    /// </summary>
    /// <param name="index">
    ///     A valid index of the object, below the key and the value; a relative index is taken before anything
    ///     is pushed.
    /// </param>
    /// <returns>The status.</returns>
    public LuaStatus TrySetTable(int index)
    {
        // [.. k v] -> [.. k v h] -> [.. k v h o] -> [.. h o k v]
        var status = LuaHelpers.Push(Pointer, LuaHelper.NewIndex);
        if (!status.IsOk)
        {
            lua_rotate(Pointer, -3, 1);
            lua_settop(Pointer, -3);
            return status;
        }

        lua_pushvalue(Pointer, Shift(index, 1));
        lua_rotate(Pointer, -4, 2);
        return TryCall(3, 0);
    }

    /// <summary>
    ///     Pushes <c>o[n]</c> for the value at <paramref name="index" /> and an integer key, under protection. Stack
    ///     after success: the value.
    /// </summary>
    /// <param name="index">A valid index of the object; a relative index is taken before anything is pushed.</param>
    /// <param name="key">The integer key (Cheat Engine collections index from 0, Lua tables from 1).</param>
    /// <returns>The status.</returns>
    public LuaStatus TryGetIndex(int index, long key)
    {
        var status = LuaHelpers.Push(Pointer, LuaHelper.Index);
        if (!status.IsOk) return status;

        lua_pushvalue(Pointer, Shift(index, 1));
        lua_pushinteger(Pointer, key);
        return TryCall(2, 1);
    }

    /// <summary>
    ///     Pops the value on top and does <c>o[n] = v</c> for the value at <paramref name="index" /> and an integer key,
    ///     under protection.
    /// </summary>
    /// <param name="index">
    ///     A valid index of the object, which must not be the top; a relative index is taken before anything
    ///     is pushed.
    /// </param>
    /// <param name="key">The integer key.</param>
    /// <returns>The status.</returns>
    public LuaStatus TrySetIndex(int index, long key)
    {
        // [.. v] -> [.. v h] -> [.. v h o] -> [.. v h o n] -> [.. h o n v]
        var status = LuaHelpers.Push(Pointer, LuaHelper.NewIndex);
        if (!status.IsOk)
        {
            lua_rotate(Pointer, -2, 1);
            lua_settop(Pointer, -2);
            return status;
        }

        lua_pushvalue(Pointer, Shift(index, 1));
        lua_pushinteger(Pointer, key);
        lua_rotate(Pointer, -4, -1);
        return TryCall(3, 0);
    }

    /// <summary>
    ///     Pushes <c>#o</c> for the value at <paramref name="index" />, honouring <c>__len</c>, under protection. Stack
    ///     after success: the length value (usually an integer).
    /// </summary>
    /// <param name="index">A valid index; a relative index is taken before anything is pushed.</param>
    /// <returns>The status.</returns>
    /// <remarks><see cref="RawLength" /> is the metamethod-free, never-raising form.</remarks>
    public LuaStatus TryLength(int index)
    {
        var status = LuaHelpers.Push(Pointer, LuaHelper.Length);
        if (!status.IsOk) return status;

        lua_pushvalue(Pointer, Shift(index, 1));
        return TryCall(1, 1);
    }

    /// <summary>
    ///     Pushes <c>tostring(v)</c> for the value at <paramref name="index" />, honouring <c>__tostring</c> and
    ///     <c>__name</c>, under protection. Stack after success: the string.
    /// </summary>
    /// <param name="index">A valid index; a relative index is taken before anything is pushed.</param>
    /// <returns>The status.</returns>
    /// <remarks>Uses the <c>tostring</c> that was global when the helpers were installed in this state.</remarks>
    public LuaStatus TryToString(int index)
    {
        var status = LuaHelpers.Push(Pointer, LuaHelper.ToString);
        if (!status.IsOk) return status;

        lua_pushvalue(Pointer, Shift(index, 1));
        return TryCall(1, 1);
    }

    /// <summary>
    ///     Compares two values with Lua semantics (<c>__eq</c>, <c>__lt</c>, <c>__le</c>; ordering values of different
    ///     types raises), under protection. Stack after success: unchanged; the result is returned through
    ///     <paramref name="result" />.
    /// </summary>
    /// <param name="index1">A valid index of the left operand; a relative index is taken before anything is pushed.</param>
    /// <param name="index2">A valid index of the right operand; likewise.</param>
    /// <param name="comparison">The operator.</param>
    /// <param name="result">The comparison result; <see langword="false" /> on failure.</param>
    /// <returns>The status.</returns>
    /// <remarks><see cref="RawEquals" /> is the metamethod-free, never-raising equality.</remarks>
    public LuaStatus TryCompare(int index1, int index2, LuaComparison comparison, out bool result)
    {
        result = false;
        var status = LuaHelpers.Push(Pointer, LuaHelper.Compare);
        if (!status.IsOk) return status;

        lua_pushinteger(Pointer, (int)comparison);
        lua_pushvalue(Pointer, Shift(index1, 2));
        lua_pushvalue(Pointer, Shift(index2, 3));
        status = TryCall(3, 1);
        if (!status.IsOk) return status;
        result = lua_toboolean(Pointer, -1) != 0;
        lua_settop(Pointer, -2);

        return status;
    }

    /// <summary>
    ///     One step of a table traversal (<c>next(t, k)</c> under protection): pops the key on top and, when the table
    ///     has a next entry, pushes that entry's key and value; at the end of the traversal pushes nothing. Start with
    ///     <c>nil</c> as the key. Stack after success: +1 (key and value in place of the key) when <paramref name="hasNext" />
    ///     ,
    ///     -1 (the key gone) otherwise.
    /// </summary>
    /// <param name="index">
    ///     A valid index of the table, which must not be the top (the top is the key); a relative index is
    ///     taken before anything is pushed.
    /// </param>
    /// <param name="hasNext">Whether a key and a value were pushed; <see langword="false" /> at the end and on failure.</param>
    /// <returns>
    ///     The status; a runtime error when the key is not a key of the table (an entry added during the traversal, or a
    ///     key that never was one).
    /// </returns>
    /// <remarks>
    ///     <c>lua_next</c> raises for an unknown key, so it is not exposed raw: the price is a protected call per step,
    ///     which is fine for the tables this SDK reads (bulk data never travels through tables). Uses the base library's
    ///     <c>next</c> as it was global when the helpers were installed, like <see cref="TryToString" /> uses
    ///     <c>tostring</c>: on a state without the base library the call fails with a runtime error. Never convert a key
    ///     in place with a string read: <see cref="TryReadUtf8" /> refuses numbers, or copy the key with
    ///     <see cref="PushValue" /> first. Assigning <c>nil</c> to existing fields during a traversal is allowed by Lua;
    ///     adding fields is not.
    /// </remarks>
    public LuaStatus TryNext(int index, out bool hasNext)
    {
        // [.. k] -> [.. k h] -> [.. k h t] -> [.. h t k]
        hasNext = false;
        var status = LuaHelpers.Push(Pointer, LuaHelper.Next);
        if (!status.IsOk)
        {
            lua_rotate(Pointer, -2, 1);
            lua_settop(Pointer, -2);
            return status;
        }

        lua_pushvalue(Pointer, Shift(index, 1));
        lua_rotate(Pointer, -3, -1);
        status = TryCall(2, 2);
        if (!status.IsOk) return status;

        // next returns a single nil at the end, adjusted to (nil, nil) by the result count.
        if (lua_type(Pointer, -2) == LUA_TNIL)
        {
            lua_settop(Pointer, -3);
            return status;
        }

        hasNext = true;
        return status;
    }
}
