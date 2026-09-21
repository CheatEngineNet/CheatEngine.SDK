using System.Runtime.CompilerServices;

// Every structure and every function-pointer signature in this assembly is blittable by construction (pointers,
// fixed-width integers, function pointers, single-field wrappers around them). Disabling runtime marshalling makes
// sure no call through these signatures can ever depend on a marshalling stub, which is also what Native AOT
// requires; the interop analyzers (CA1420/CA1421) then flag the APIs and P/Invoke features that would need the
// marshaller.
//
// What the attribute does NOT do is police the shapes themselves: with marshalling disabled, 'bool' and 'char' are
// simply passed as raw 1-byte and 2-byte values, and a function-pointer type with such a parameter, with a
// by-reference parameter or with another calling convention compiles without any diagnostic. Those rules (no
// bool/char at any depth, unmanaged Stdcall only, nothing by reference) are enforced by the reflection gate of
// tests/CheatEngine.SDK.Abi.Tests (Support/AbiShape.cs, run over every structure by AssemblyConformanceTests), and the
// exact signature of every typed slot by the host-simulation tests that store '&Method' in it.
[assembly: DisableRuntimeMarshalling]
