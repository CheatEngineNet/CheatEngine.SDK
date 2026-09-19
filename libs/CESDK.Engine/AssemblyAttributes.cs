using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// This assembly reaches the host only through CESDK.Lua (the state provider, the host-object pusher) and
// CESDK.Lua.Interop (the Lua C API forwarders): it declares no unmanaged signature of its own. Disabling runtime
// marshalling states that nothing here may ever need a marshalling stub, exactly as the two layers below do, so a
// future P/Invoke or delegate in this assembly is a compile-time error rather than a hidden allocation.
[assembly: DisableRuntimeMarshalling]

// SonarAnalyzer rule S6640 flags every unsafe context. Unsafe is how this assembly reaches Cheat Engine, through
// function pointers and blittable structures, so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Unsafe is the design of this interop assembly.")]
