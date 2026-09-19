using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Same switch as CESDK.Abi and as every real consumer (CESDK.Hosting): the function-pointer calls made by these
// tests then go through exactly the code path a plugin uses, with no marshalling stub that could hide a
// non-blittable signature.
[assembly: DisableRuntimeMarshalling]

// SonarAnalyzer rule S6640 flags every unsafe context. These tests drive the unsafe surface of the SDK directly,
// so the rule has nothing to say here.
[assembly: SuppressMessage(
    "Major Vulnerability",
    "S6640:Unsafe code blocks should not be used",
    Justification = "Test code drives the unsafe interop surface.")]
