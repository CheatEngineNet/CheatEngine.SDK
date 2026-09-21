using System.Runtime.CompilerServices;

// Same switch as CheatEngine.SDK.Abi and as every real consumer (CheatEngine.SDK.Hosting): the function-pointer calls
// made by these tests then go through exactly the code path a plugin uses, with no marshalling stub that could hide a
// non-blittable signature.
[assembly: DisableRuntimeMarshalling]
