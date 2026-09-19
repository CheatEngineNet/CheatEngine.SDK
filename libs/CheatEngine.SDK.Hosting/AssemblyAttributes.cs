using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Every unmanaged signature in this assembly (the three lifecycle callbacks Cheat Engine calls, the slots of the
// copied exports record it calls through, the dispatch thunk Lua calls, the OutputDebugString import) is blittable by
// construction. With runtime marshalling disabled that becomes a compile-time guarantee: a bool, string, ref or out in
// one of them no longer compiles, so no hidden marshalling stub can appear at the native boundary.
[assembly: DisableRuntimeMarshalling]

// SonarAnalyzer rule S6640 flags every unsafe context. Unsafe is how this assembly reaches Cheat Engine, through
// function pointers and blittable structures, so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Unsafe is the design of this interop assembly.")]
