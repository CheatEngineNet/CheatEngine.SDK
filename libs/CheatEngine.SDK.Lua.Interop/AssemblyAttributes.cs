using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Every signature in this assembly is blittable by construction (pointers, integers, doubles, function pointers).
// Switching the runtime marshaller off turns "no hidden marshalling, no hidden allocation" into a compile-time
// guarantee: a non-blittable type in a function-pointer signature or in the one P/Invoke no longer compiles.
[assembly: DisableRuntimeMarshalling]
// The packaged C11 bridge is private to this assembly. Restrict every P/Invoke to this assembly's directory so a
// same-named DLL found through the process search path cannot satisfy a bridge import.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
[assembly: InternalsVisibleTo("CheatEngine.SDK.Lua")]
[assembly: InternalsVisibleTo("CheatEngine.SDK.Lua.Interop.Tests")]
