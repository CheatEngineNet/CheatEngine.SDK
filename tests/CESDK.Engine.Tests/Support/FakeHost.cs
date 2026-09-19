using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CESDK.Engine.Objects;
using CESDK.Lua.Calls;
using CESDK.Lua.Interop.Types;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;
using CESDK.Tests.Shared.NativeLua;
using static CESDK.Lua.Interop.Api.LuaApi;

namespace CESDK.Engine.Tests.Support;

/// <summary>
///     A stand-in for Cheat Engine's object model, in the shape <see cref="LuaHostBinding" /> expects: an
///     <c>[UnmanagedCallersOnly]</c> state provider returning the fixture state, and an <c>[UnmanagedCallersOnly]</c>
///     object pusher that does what <c>LuaPushClassInstance</c> is assumed to do: create a full userdata whose first
///     pointer-sized field is the native object pointer, and give it a metatable. The metatable is written in Lua
///     (<see cref="Model" />): <c>__index</c> returns property values or instance-bound closures for methods (called
///     without <c>self</c>, the convention this SDK relies on), <c>__newindex</c> stores properties, and both can
///     raise, so that every failure path of the Engine primitives can be exercised without Cheat Engine.
/// </summary>
/// <remarks>
///     Objects are synthetic: the "native pointer" is a unique number that is never dereferenced, and each object's
///     state lives in a Lua table stored as the userdata's user value (<c>lua_setuservalue</c>), keyed by pointer in a
///     registry table so that every push of the same pointer finds the same state.
/// </remarks>
internal static unsafe class FakeHost
{
    // Registry keys: light userdata whose values are the addresses of these bytes (stable for the process).
    private const int MetatableKey = 0;
    private const int ObjectsKey = 1;
    private const int HostKey = 2;
    private static readonly nint s_keys = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(FakeHost), 3);

    private static lua_State* s_state;
    private static int s_providerCalls;
    private static int s_pusherCalls;
    private static long s_nextPointer = 0x7FF0_0000_1000;

    /// <summary>
    ///     The Lua side of the fake host: returns the metatable, the pointer-to-object table and the host bookkeeping
    ///     table. Class methods receive the object table as their first argument and are handed to Lua callers as
    ///     closures bound to it.
    /// </summary>
    private static ReadOnlySpan<byte> Model => """
                                               local getuservalue = debug.getuservalue
                                               local host = { destroyed = 0, classes = {} }
                                               local objects = {}
                                               local mt = {}

                                               local function state(ud)
                                                 local o = getuservalue(ud)
                                                 if o == nil then error("not a host object") end
                                                 return o
                                               end

                                               mt.__index = function(ud, k)
                                                 local o = state(ud)
                                                 if type(k) == "number" then return o.items[k + 1] end
                                                 local getter = o.getters[k]
                                                 if getter ~= nil then return getter(o) end
                                                 local member = host.classes[o.class][k]
                                                 if type(member) == "function" then return function(...) return member(o, ...) end end
                                                 if member ~= nil then return member end
                                                 return o.props[k]
                                               end

                                               mt.__newindex = function(ud, k, v)
                                                 local o = state(ud)
                                                 if type(k) == "number" then o.items[k + 1] = v return end
                                                 local setter = o.setters[k]
                                                 if setter ~= nil then setter(o, v) return end
                                                 o.props[k] = v
                                               end

                                               host.classes.Object = {
                                                 destroy = function(o)
                                                   if o.destroyed then error("object already destroyed") end
                                                   o.destroyed = true
                                                   host.destroyed = host.destroyed + 1
                                                 end,
                                                 getClassName = function(o) return o.class end,
                                               }

                                               host.classes.Probe = setmetatable({
                                                 getCount = function(o) return o.props.Count end,
                                                 add = function(o, a, b) return a + b end,
                                                 echo = function(o, ...) return ... end,
                                                 raise = function(o) error("raised by the host object") end,
                                                 getAddress = function(o, i) return string.format("%08X", o.addresses[i + 1]) end,
                                                 getAddressNumber = function(o, i) return o.addresses[i + 1] end,
                                                 setOther = function(o, other) o.props.Other = other end,
                                                 notAMethod = 42,
                                               }, { __index = host.classes.Object })

                                               host.classes.Stubborn = setmetatable({
                                                 destroy = function(o) error("refuses to be destroyed") end,
                                               }, { __index = host.classes.Object })

                                               return mt, objects, host
                                               """u8;

    public static int ProviderCalls => Volatile.Read(ref s_providerCalls);

    public static int PusherCalls => Volatile.Read(ref s_pusherCalls);

    public static nint ProviderAddress => (nint)(delegate* unmanaged[Stdcall]<void*>)&Provide;

    public static nint PusherAddress => (nint)(delegate* unmanaged[Stdcall]<void*, void*, void>)&PushObject;

    /// <summary>
    ///     Points the provider at the fixture state, installs the Lua model in it and builds a binding for the calling
    ///     thread as main thread.
    /// </summary>
    public static LuaHostBinding CreateBinding(NativeLuaState state, bool withPusher = true)
    {
        s_state = state.L;
        s_providerCalls = 0;
        s_pusherCalls = 0;
        Install(new LuaState(state.Pointer));
        delegate* unmanaged[Stdcall]<void*> provider = &Provide;
        delegate* unmanaged[Stdcall]<void*, void*, void> pusher = withPusher ? &PushObject : null;
        return new LuaHostBinding(provider, pusher, Environment.CurrentManagedThreadId);
    }

    /// <summary>A pointer no object has had before. Never dereferenced.</summary>
    public static nint NewPointer()
    {
        return (nint)Interlocked.Add(ref s_nextPointer, 0x10);
    }

    /// <summary>
    ///     Registers a fake object of <paramref name="className" /> (<c>Probe</c>, <c>Stubborn</c> or <c>Object</c>)
    ///     and returns its handle. <paramref name="initializer" /> is Lua code run with the object table as <c>o</c>:
    ///     <c>o.props.Count = 3</c>, <c>o.getters.Bad = function() error("x") end</c>, <c>o.addresses = {...}</c>.
    /// </summary>
    public static CEObject CreateObject(LuaState L, string className, string initializer = "")
    {
        var pointer = NewPointer();
        var source =
            "local objects, key, ptr = ...\n" +
            "local o = { ptr = ptr, class = '" + className +
            "', props = {}, items = {}, getters = {}, setters = {}, addresses = {} }\n" +
            initializer + "\n" +
            "objects[key] = o";
        var utf8 = Encoding.UTF8.GetBytes(source);

        using LuaFrame frame = new(L);
        var status = L.TryLoad(utf8, "=fakehost"u8);
        FailIfNotOk(L, status, "loading the object initializer");
        Assert.Equal(LuaType.Table, L.RawGetPointer(LuaState.RegistryIndex, s_keys + ObjectsKey));
        L.PushLightUserdata(pointer);
        L.PushInteger(pointer);
        FailIfNotOk(L, L.TryCall(3, 0), "running the object initializer");
        return new CEObject(pointer);
    }

    /// <summary>How many objects have been destroyed through the model's <c>destroy</c> since the model was installed.</summary>
    public static long DestroyedCount(LuaState L)
    {
        using LuaFrame frame = new(L);
        Assert.Equal(LuaType.Table, L.RawGetPointer(LuaState.RegistryIndex, s_keys + HostKey));
        FailIfNotOk(L, L.TryGetField(-1, "destroyed"u8), "reading host.destroyed");
        return EngineTest.ReadInteger(L, -1);
    }

    /// <summary>Whether the object was destroyed through the model's <c>destroy</c>.</summary>
    public static bool IsDestroyed(LuaState L, CEObject obj)
    {
        using LuaFrame frame = new(L);
        Assert.Equal(LuaType.Table, L.RawGetPointer(LuaState.RegistryIndex, s_keys + ObjectsKey));
        Assert.Equal(LuaType.Table, L.RawGetPointer(-1, obj.Value));
        FailIfNotOk(L, L.TryGetField(-1, "destroyed"u8), "reading o.destroyed");
        return L.ToBoolean(-1);
    }

    private static void Install(LuaState L)
    {
        using LuaFrame frame = new(L);
        var status = L.TryExecute(Model, 3, "=fakehost"u8);
        FailIfNotOk(L, status, "installing the fake host model");

        // Results: mt, objects, host. rawsetp pops the top each time, so the keys are assigned in reverse.
        L.RawSetPointer(LuaState.RegistryIndex, s_keys + HostKey);
        L.RawSetPointer(LuaState.RegistryIndex, s_keys + ObjectsKey);
        L.RawSetPointer(LuaState.RegistryIndex, s_keys + MetatableKey);
    }

    private static void FailIfNotOk(LuaState L, LuaStatus status, string what)
    {
        if (!status.IsOk) Assert.Fail("The fake host failed while " + what + ": " + LuaError.FromStack(L, status));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* Provide()
    {
        Interlocked.Increment(ref s_providerCalls);
        return s_state;
    }

    // What LuaPushClassInstance is assumed to do: a full userdata holding the object pointer, with the class metatable.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PushObject(void* L, void* nativeObject)
    {
        Interlocked.Increment(ref s_pusherCalls);
        var state = (lua_State*)L;

        var block = lua_newuserdata(state, (nuint)sizeof(nint));
        *(nint*)block = (nint)nativeObject;

        // [ud] -> [ud objects] -> [ud objects o] -> setuservalue(ud) -> [ud objects] -> [ud]
        if (lua_rawgetp(state, LUA_REGISTRYINDEX, (void*)(s_keys + ObjectsKey)) == LUA_TTABLE)
        {
            _ = lua_rawgetp(state, -1, nativeObject);
            lua_setuservalue(state, -3);
        }

        lua_settop(state, -2);

        if (lua_rawgetp(state, LUA_REGISTRYINDEX, (void*)(s_keys + MetatableKey)) == LUA_TTABLE)
            _ = lua_setmetatable(state, -2);
        else
            lua_settop(state, -2);
    }
}
