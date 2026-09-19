using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Every unmanaged signature this assembly calls through (the Lua C API forwarders of CESDK.Lua.Interop, the two host
// function pointers of LuaHostBinding, the lua_CFunction wrapped by LuaNativeFunction) is blittable by construction.
// With runtime marshalling disabled that is a compile-time guarantee: a bool, string, ref or out in one of them no
// longer compiles, so no hidden marshalling stub and no hidden allocation can appear on a call path.
[assembly: DisableRuntimeMarshalling]

// SonarAnalyzer rule S6640 flags every unsafe context. Unsafe is how this assembly reaches Cheat Engine, through
// function pointers and blittable structures, so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Unsafe is the design of this interop assembly.")]
