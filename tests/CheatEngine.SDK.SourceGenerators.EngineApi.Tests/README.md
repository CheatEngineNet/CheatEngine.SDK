# CheatEngine.SDK.SourceGenerators.EngineApi.Tests

Tests for `EngineApiGenerator`, the repository-internal generator that turns a spec file into typed wrappers for
`CheatEngine.SDK.Engine`.

## Objective

Prove that a `*.cheatengine-sdk-api.txt` spec file becomes exactly the wrapper code `CheatEngine.SDK.Engine` needs. Invalid input emits
nothing, and the wrappers work against a real Lua 5.3 library.

## Why it exists

`CheatEngine.SDK.Engine.Generated.MemoryScalars` is generated from
`source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/memory-scalars.cheatengine-sdk-api.txt`. A wrong emitter would ship
wrong wrappers without a compile error. The generator never ships in the package, and this project guards its emitter
directly. See the [generator README](../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/README.md).

## How it works

| Suite          | What it proves                                                                                             |
|----------------|------------------------------------------------------------------------------------------------------------|
| Parsing        | `SpecFileParser.Parse` and `IsSpecFile` handle the grammar and report issues, with no Roslyn type involved |
| Emission       | Exact text of a wrapper body, one cache field per global, the global namespace, a clean compilation        |
| Silence        | A foreign file name, an empty file, a header-only file, an invalid entry or no spec at all emit nothing    |
| Incrementality | An unrelated edit recomputes nothing, and one spec change reruns only that file's output                   |
| End to end     | Emitted wrappers are compiled, loaded and called against Lua stand-ins for `readInteger` and its siblings  |

The generator reads only `AdditionalTextsProvider`. So the harness compiles an almost empty compilation and passes spec
text through `InMemoryAdditionalText`. The compilation references the real `CheatEngine.SDK.Annotations`, `CheatEngine.SDK.Lua.Interop` and
`CheatEngine.SDK.Lua`, but not `CheatEngine.SDK.Engine`, because the generated file becomes part of that assembly.
`Infrastructure/Address.cs` declares a stand-in `CheatEngine.SDK.Engine.Values.Address` with the members the wrappers call.
Generated code loads into its own load context that falls back to the test process, so it reaches the same `LuaRuntime`
the test attaches.

End-to-end tests carry `Category=NativeLua` and join the serial `LuaRuntimeSuite` collection. The library lookup and the
skip reason are described in [`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md). The other suites
need no Lua DLL, so a
default run never ends with zero executed tests.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests --filter-trait "Category=NativeLua"
```

## Promise

- Generated code compiles with no warning and no generator diagnostic, and its text is pinned (`EmissionTests`).
- An invalid or absent spec emits no file (`NoOutputTests`).
- Unchanged input recomputes nothing, and step values hold no Roslyn objects (`IncrementalityTests`).
- The 32-bit and 64-bit wrappers use independent storage, a detached runtime throws, and the warm success path allocates
  nothing (`MemoryScalarsEndToEndTests`).
