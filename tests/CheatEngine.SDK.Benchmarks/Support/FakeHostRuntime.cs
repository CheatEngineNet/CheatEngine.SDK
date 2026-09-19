using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Benchmarks.Support;

/// <summary>
///     A minimal ambient-runtime binding over one <see cref="NativeLuaState" />, for benchmarks that call generated
///     wrappers (<c>LuaRuntime.AcquireState()</c>) or a <c>CheatEngine.SDK.Engine.Objects.CEObject</c> property (which
///     additionally needs the host-object pusher). Deliberately smaller than
///     <c>tests/CheatEngine.SDK.Engine.Tests/Support/FakeHost.cs</c> (no per-object state, no xUnit, one fixed "Count"
///     property instead of a full object model): this is a benchmark fixture, not a correctness double, and this project does
///     not reference xUnit (<c>tests/CheatEngine.SDK.Tests.Shared/README.md</c>).
/// </summary>
/// <remarks>
///     Only one <see cref="NativeLuaState" /> can be attached at a time: the state pointer lives in a single static
///     field, because the <see cref="LuaHostBinding" /> provider must be an <c>[UnmanagedCallersOnly]</c> static
///     method. Each benchmark class attaches its own state in <c>[GlobalSetup]</c> and detaches it in
///     <c>[GlobalCleanup]</c>; BenchmarkDotNet's default toolchain runs one benchmark class per generated process, so
///     classes never share the field in practice.
/// </remarks>
internal static unsafe class FakeHostRuntime
{
    // Registry key for the one-property metatable the pusher attaches; stable for the process (a light userdata whose
    // value is a type-stable address, the registry-key idiom of the Lua manual, section 4.5).
    private static readonly nint s_metatableKey =
        RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(FakeHostRuntime), 1);

    private static lua_State* s_state;

    /// <summary>
    ///     Attaches <see cref="LuaRuntime" /> to <paramref name="state" /> on the calling thread and returns a view of
    ///     it.
    /// </summary>
    /// <param name="state">The Lua state to serve as the ambient runtime's only thread.</param>
    /// <param name="withPusher">
    ///     Whether to install the host-object pusher and its one-property ("Count", always 42) metatable, needed only
    ///     by <c>CheatEngine.SDK.Engine.Objects.CEObject</c>'s property-get benchmark.
    /// </param>
    /// <returns>
    ///     A <see cref="LuaState" /> view of <paramref name="state" />, for the caller's own setup (defining globals,
    ///     registering functions).
    /// </returns>
    public static LuaState Attach(NativeLuaState state, bool withPusher)
    {
        s_state = state.L;
        LuaState view = new(state.Pointer);

        if (withPusher) InstallMetatable(view);

        delegate* unmanaged[Stdcall]<void*> provider = &Provide;
        delegate* unmanaged[Stdcall]<void*, void*, void> pusher = withPusher ? &PushObject : null;
        LuaRuntime.Attach(new LuaHostBinding(provider, pusher, Environment.CurrentManagedThreadId));
        return view;
    }

    private static void InstallMetatable(LuaState state)
    {
        using LuaFrame frame = new(state);
        var status = state.TryExecute("local mt = {} mt.__index = function(_, _) return 42 end return mt"u8, 1);
        if (!status.IsOk)
            throw new InvalidOperationException("Installing the fake host metatable failed: " +
                                                LuaError.FromStack(state, status));

        state.RawSetPointer(LuaState.RegistryIndex, s_metatableKey);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* Provide()
    {
        return s_state;
    }

    // What LuaPushClassInstance is assumed to do: a full userdata whose first field is the object pointer, with a
    // metatable attached.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PushObject(void* l, void* nativeObject)
    {
        var state = (lua_State*)l;
        var block = lua_newuserdata(state, (nuint)sizeof(nint));
        *(nint*)block = (nint)nativeObject;

        if (lua_rawgetp(state, LUA_REGISTRYINDEX, (void*)s_metatableKey) == LUA_TTABLE)
            _ = lua_setmetatable(state, -2);
        else
            lua_settop(state, -2);
    }
}
