# CESDK0005: Source type collides with the generated Cheat Engine entry point

With generated entry-point support enabled, CheatEngine.SDK owns `CESDK.CESDK`. A source declaration with that exact
metadata identity duplicates the type that the generator must emit, even if it is not itself marked
`[CheatEnginePlugin]`.

Remove or rename the source type. If the assembly deliberately owns its bootstrap, set
`CheatEngineSdkGenerateEntryPoint=false` and provide the complete `CEPluginInitialize(System.IntPtr, int)` manual
contract; CESDK0003 validates that choice.
