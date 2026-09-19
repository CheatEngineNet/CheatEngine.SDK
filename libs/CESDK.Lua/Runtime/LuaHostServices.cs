using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Runtime;

/// <summary>
///     The attached <see cref="LuaHostBinding" /> as an immutable object, so that <see cref="LuaRuntime" /> publishes and
///     reads the whole binding with one reference write and one volatile read, never a torn struct.
/// </summary>
internal sealed unsafe class LuaHostServices
{
    internal LuaHostServices(in LuaHostBinding binding)
    {
        Binding = binding;
        Provider = binding.Provider;
        Pusher = binding.Pusher;
        MainThreadId = binding.MainThreadId;
    }

    internal LuaHostBinding Binding { get; }

    internal delegate* unmanaged[Stdcall]<lua_State*> Provider { get; }

    internal delegate* unmanaged[Stdcall]<lua_State*, void*, void> Pusher { get; }

    internal int MainThreadId { get; }
}
