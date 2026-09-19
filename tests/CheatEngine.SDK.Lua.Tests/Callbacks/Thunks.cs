using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>
///     The managed <c>lua_CFunction</c>s the callback tests register. Each one follows the thunk rules: catch everything,
///     use the state Lua passed, report failures through <see cref="LuaThunk" />, never call a raising API.
/// </summary>
internal static unsafe class Thunks
{
    /// <summary>
    ///     Slots <see cref="DeepStackFirstProtected" /> fills before its first protected operation: most of
    ///     <c>LUA_MINSTACK</c>.
    /// </summary>
    public const int DeepStackSlots = LuaState.MinimumFreeSlots - 2;

    public static LuaNativeFunction Add => new(&AddThunk);

    public static LuaNativeFunction Count => new(&CountThunk);

    public static LuaNativeFunction Throw => new(&ThrowThunk);

    public static LuaNativeFunction Echo => new(&EchoThunk);

    public static LuaNativeFunction Greet => new(&GreetThunk);

    public static LuaNativeFunction DeepStackFirstProtected => new(&DeepStackFirstProtectedThunk);

    /// <summary><c>add(a, b)</c>: the sum, or the error "add expects two numbers".</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AddThunk(nint handle)
    {
        LuaState L = new(handle);
        try
        {
            if (!Int64Marshaller.TryRead(L, 1, out var a) || !Int64Marshaller.TryRead(L, 2, out var b))
                return LuaThunk.Fail(L, "add expects two numbers"u8);

            Int64Marshaller.Push(L, a + b);
            return 1;
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(L, exception);
        }
    }

    /// <summary><c>count()</c>: increments the <see cref="Counter" /> carried as state and returns the new value.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CountThunk(nint handle)
    {
        LuaState L = new(handle);
        try
        {
            if (!LuaThunk.TryGetState(L, out Counter? counter))
                return LuaThunk.Fail(L, "count: no state (callback released)"u8);

            counter.Value++;
            counter.LastState = handle;
            Int32Marshaller.Push(L, counter.Value);
            return 1;
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(L, exception);
        }
    }

    /// <summary><c>throwing()</c>: throws a managed exception, which the catch-all converts.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ThrowThunk(nint handle)
    {
        LuaState L = new(handle);
        try
        {
            return ThrowInvalidOperation();
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(L, exception);
        }
    }

    /// <summary><c>echo(...)</c>: returns its arguments unchanged.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EchoThunk(nint handle)
    {
        LuaState L = new(handle);
        return L.Top;
    }

    /// <summary>
    ///     <c>greet(name)</c>: "hello, " + name. The string argument goes straight into a <see cref="string" /> parameter
    ///     (no null-forgiving operator: <see cref="StringMarshaller.TryRead" /> is annotated), and a wrong argument is
    ///     reported with Lua's own wording through <see cref="LuaThunk.FailBadArgument" />.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GreetThunk(nint handle)
    {
        LuaState L = new(handle);
        try
        {
            if (!StringMarshaller.TryRead(L, 1, out var name)) return LuaThunk.FailBadArgument(L, 1, "string"u8);

            StringMarshaller.Push(L, Greeting(name));
            return 1;
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(L, exception);
        }
    }

    /// <summary>
    ///     <c>deep()</c>: fills most of the slots Lua guarantees a C function, then runs the first protected operation of
    ///     the state (the helper install) from that depth, and returns the global <c>x</c> read that way. Exercises the
    ///     stack budget of the install on a state that has no helpers yet.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DeepStackFirstProtectedThunk(nint handle)
    {
        LuaState L = new(handle);
        try
        {
            for (var i = 0; i < DeepStackSlots; i++) L.PushInteger(i);

            var status = L.TryGetGlobal("x"u8);
            if (!status.IsOk) return LuaThunk.Fail(L, "TryGetGlobal failed at depth"u8);

            // The pushed integers must be intact under the result.
            for (var i = 0; i < DeepStackSlots; i++)
                if (!L.TryReadInteger(i + 1, out var value) || value != i)
                    return LuaThunk.Fail(L, "the stack below the protected call was disturbed"u8);

            return 1;
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(L, exception);
        }
    }

    private static string Greeting(string name)
    {
        return "hello, " + name;
    }

    private static int ThrowInvalidOperation()
    {
        throw new InvalidOperationException("managed boom");
    }
}
