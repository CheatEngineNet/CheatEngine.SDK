using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Runtime marshalling is disabled so these boundaries cannot gain an implicit marshalling stub. This does not validate
// unmanaged function-pointer shapes: the ABI tests separately check their calling conventions and reject bool, char and
// by-reference parameters, as documented by CheatEngine.SDK.Abi.
[assembly: DisableRuntimeMarshalling]

// SonarAnalyzer rule S6640 flags every unsafe context. Unsafe is how this assembly reaches Cheat Engine, through
// function pointers and blittable structures, so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Unsafe is the design of this interop assembly.")]
