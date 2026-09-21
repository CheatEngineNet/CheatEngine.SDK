using System.Runtime.CompilerServices;

// This assembly reaches the host only through CheatEngine.SDK.Lua (the state provider, the host-object pusher) and
// CheatEngine.SDK.Lua.Interop (the Lua C API forwarders): it declares no unmanaged signature of its own. Disabling
// runtime marshalling states that nothing here may ever need a marshalling stub, exactly as the two layers below do, so
// a future P/Invoke or delegate in this assembly is a compile-time error rather than a hidden allocation.
[assembly: DisableRuntimeMarshalling]
