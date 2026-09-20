# CESDK0003: Manual Cheat Engine bootstrap is missing or malformed

This rule is reported at compilation end only when `CheatEngineSdkGenerateEntryPoint=false` and the assembly contains a
`[CheatEnginePlugin]` class. The generator is then intentionally silent, so source code must provide the host identity
it normally emits: a static `CESDK.CESDK` type with `public static int CEPluginInitialize(System.IntPtr, int)`.

The second parameter remains opaque. The rule validates only its required managed ABI shape; it does not assign it a
record-size or version meaning. Restore generated entry-point support, or implement that exact method and keep all
native exception handling inside it.

Suppress only for a deliberately non-loadable test assembly. Cheat Engine discovers this entry point by exact name and
signature, so an alternative bootstrap is not a compatible replacement.
