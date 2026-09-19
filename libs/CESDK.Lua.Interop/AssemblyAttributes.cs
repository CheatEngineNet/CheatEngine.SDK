using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Every signature in this assembly is blittable by construction (pointers, integers, doubles, function pointers).
// Switching the runtime marshaller off turns "no hidden marshalling, no hidden allocation" into a compile-time
// guarantee: a non-blittable type in a function-pointer signature or in the one P/Invoke no longer compiles.
[assembly: DisableRuntimeMarshalling]
[assembly: InternalsVisibleTo("CESDK.Lua")]

// SonarAnalyzer rule S6640 flags every unsafe context. Unsafe is how this assembly reaches Cheat Engine, through
// function pointers and blittable structures, so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Unsafe is the design of this interop assembly.")]
