# CheatEngine.SDK.SourceGenerators.LuaBindings.Tests

Tests for the LuaBindings generator, which emits thunks for `[LuaFunction]` methods, bodies for `[LuaGlobal]` methods,
and borrowed object handles plus protected members for `[LuaClass]`, `[LuaMethod]` and `[LuaProperty]`.

## Objective

Prove that the generator emits exact, warning-free code, isolates invalid inputs from healthy declarations and produces
no unmanaged boundary other than the explicit `[LuaFunction]` thunks. The generated code runs correctly on a real Lua
5.3 state.

## Why it exists

Generated code must be right for the assemblies it ships with: `CheatEngine.SDK.Annotations`,
`CheatEngine.SDK.Lua.Interop`, `CheatEngine.SDK.Lua` and `CheatEngine.SDK.Engine`.
So this project uses no contract stubs. Every test compiles against the real assemblies. See
the [generator README](../../source-generators/CheatEngine.SDK.SourceGenerators.LuaBindings/README.md).

## How it works

| Suite           | What it proves                                                                                                                   |
|-----------------|----------------------------------------------------------------------------------------------------------------------------------|
| Output          | Exact text for the nominal sources, and clean compilation of every supported shape, containing type and partial-method signature |
| Input isolation | Invalid shapes and look-alike attributes emit no conflicting source; a globals-only project works without `AllowUnsafeBlocks`    |
| Object handles  | Borrowed-handle identity, marshalling, protected methods/properties and valid-sibling isolation                                  |
| Incrementality  | Edits that cannot change the output recompute nothing, and an edit to one kind of binding leaves the other's output cached       |
| End to end      | Generated thunks and wrappers are compiled, loaded and run against a real Lua 5.3 state through the real `LuaRuntime`            |
| Shared code     | Unit tests of the linked `LuaEmit` emitters, `LuaNames`, `LuaValueKinds`, `HintNames` and the grouping models                    |

Expected text is written by hand, independent of the emitter, and normalized to LF. "Compiles clean" means no warning or
error at warning level 9999 on C# 14 with nullable on. Emitted assemblies load into their own context, which resolves
`CheatEngine.SDK.Lua` to the copy the test process runs. So the `LuaRuntime` a test attaches is the one the generated
code
reaches. The context is not collectible, because generated code holds static `LuaRef` caches and Lua closures hold thunk
addresses. Test sources stay ASCII, with non-ASCII data written as `\uXXXX` escapes. Hot-path assertions call custom
delegate types, because `MethodInfo.Invoke` allocates and `CreateDelegate` does not. Error-path chunks use
`return pcall(function() add('x', 2) end)`, because a tail call would erase the frame that `error(message, 2)` blames.

Only the end-to-end tests carry `Category=NativeLua` and join the serial `LuaRuntimeSuite` collection. The library
lookup and the skip reason are described in [
`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md).
Every other test runs
everywhere, so a default run never has zero tests.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests --filter-trait "Category=NativeLua"
```

## Promise

- The nominal inputs yield exactly the expected files, and an invalid member never blocks its valid neighbors
  (`LuaFunctionOutputTests`, `LuaGlobalOutputTests`, `LuaObjectOutputTests`, `NoOutputTests`).
- A wrong argument kind, a wrong argument count and a throwing target become catchable Lua errors
  (`LuaFunctionEndToEndTests`).
- Generated wrappers report nil, wrong kinds and raising globals as failure, and throw while the runtime is detached
  (`LuaGlobalEndToEndTests`).
- Warm thunk calls and the warm `Try` form allocate nothing on the managed side (`LuaFunctionEndToEndTests`,
  `LuaGlobalEndToEndTests`).
