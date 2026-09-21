using System.Runtime.CompilerServices;

// Runtime marshalling is disabled so these boundaries cannot gain an implicit marshalling stub. This does not validate
// unmanaged function-pointer shapes: the ABI tests separately check their calling conventions and reject bool, char and
// by-reference parameters, as documented by CheatEngine.SDK.Abi.
[assembly: DisableRuntimeMarshalling]
