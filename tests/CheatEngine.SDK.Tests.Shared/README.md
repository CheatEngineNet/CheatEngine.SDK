# CheatEngine.SDK.Tests.Shared

Test support that gives tests and benchmarks a real Lua 5.3 library, or a precise reason why none exists.

## Objective

`CheatEngine.SDK.Tests.Shared` is a project that runs no test. Its `NativeLua` fixture binds
`CheatEngine.SDK.Lua.Interop.Api.LuaApi` to a Lua 5.3 DLL once per process and hands out independent Lua states.

## Why it exists

Tests tagged `Category=NativeLua` and the benchmarks need real Lua behavior that no fake reproduces. The fixture exists
once, in one project with one namespace, `CheatEngine.SDK.Tests.Shared.NativeLua`. Linked source gave the same file
several owners and a namespace that followed whichever project a tool evaluated last. The cost is one more node in the
reference graph and an `InternalsVisibleTo` list in the project file.

## How it works

`NativeLuaLibrary` holds the process-wide result: `IsAvailable`, `Handle`, `LibraryPath`, `UnavailableReason` and
`ThrowIfUnavailable()`. `NativeLuaProbe.Run` performs one lookup and never throws. `NativeLuaState` owns one independent
`lua_State` (`L`, `Pointer`) and closes it on `Dispose()`, after which `L` and `Pointer` throw
`ObjectDisposedException`. It has no finalizer and is not thread-safe: dispose every state on the thread that created
it. The `openLibraries: false` argument skips `luaL_openlibs` and yields a bare state.

The first use of `NativeLuaLibrary` runs the lookup once. It reads `CHEATENGINE_SDK_LUA53_PATH` when the value is not
blank and trims it. No fallback follows, so an override is never replaced silently. Otherwise it uses `BundledPath`, the
copy of Cheat Engine 7.7's own Lua that the build places at `native/lua53-64.dll` beside the executable (the source
file lives in
[`native/cheat-engine`](../../native/cheat-engine/README.md)). An installed Cheat Engine is never consulted, so every
machine and CI run binds the same build. The chosen value becomes one absolute path, and the probe checks, loads
and reports that same path. The module stays loaded because `LuaApi` keeps raw addresses into it. The DLL must match the
architecture of the test process. When no library is usable, `UnavailableReason` names the cause, such as an invalid
path, a missing file, a load failure or missing Lua 5.3 exports. Tests then report Skipped.

The CI workflow provisions nothing: the checkout carries the DLL, and the file name `lua53-64.dll` lets the production
module lookup test run too. The Debug test step fails when any test reports Skipped.

## Use

Reference the project with
`<ProjectReference Include="../CheatEngine.SDK.Tests.Shared/CheatEngine.SDK.Tests.Shared.csproj"/>` and add the
referencing project to the `InternalsVisibleTo` list of `CheatEngine.SDK.Tests.Shared.csproj`, because the types are
internal. The referencing project needs `AllowUnsafeBlocks` and implicit usings (on for everything under `tests/`);
`CheatEngine.SDK.Lua.Interop` flows through this project. A new file in the `NativeLua` folder needs no project edit.

```csharp
using CheatEngine.SDK.Tests.Shared.NativeLua;

public sealed class StateTests
{
    [Fact, Trait("Category", "NativeLua")]
    public void Creates_a_state()
    {
        Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
        using NativeLuaState state = new();
        Assert.NotEqual(0, state.Pointer);
    }
}
```

The projects that use `NativeLua` are `CheatEngine.SDK.Lua.Interop.Tests`, `CheatEngine.SDK.Lua.Tests`,
`CheatEngine.SDK.Engine.Tests`, `CheatEngine.SDK.Hosting.Tests`, `CheatEngine.SDK.SourceGenerators.EngineApi.Tests`,
`CheatEngine.SDK.SourceGenerators.LuaBindings.Tests` and `CheatEngine.SDK.Benchmarks`. Benchmarks call
`NativeLuaLibrary.ThrowIfUnavailable()` in `[GlobalSetup]` instead of skipping.

## Promise

- The probe reports an invalid or missing path as a reason, never an exception, and a set variable never falls back to
  the bundled copy. `NativeLuaProbeTests` pins both.
- Without the variable, the fixture binds Cheat Engine's own Lua, byte for byte: `BundledLuaLibraryTests` pins the file
  by size and SHA-256 and the path that gets bound.
- A missing or unusable library skips the test instead of failing it: each test project that uses it guards its
  `NativeLua`
  tests with `Assert.SkipUnless`.
- Every type is `internal` and free of xUnit types. `CheatEngine.SDK.Benchmarks` uses it without an xUnit reference.
- One process binds one Lua library: `LuaApi` refuses a second module, pinned by
  `Second_copy_of_the_library_is_refused_and_leaves_the_table_alone`.
