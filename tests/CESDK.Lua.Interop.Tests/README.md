# CESDK.Lua.Interop.Tests

Conformance tests for `CESDK.Lua.Interop`, the raw Lua 5.3 function-pointer table.

## Objective

Prove that every declaration in `CESDK.Lua.Interop` matches the Lua 5.3 C API, without Cheat Engine running.

## Why it exists

A wrong declaration in that assembly is memory-unsafe instead of an exception, and the compiler cannot see it. The
declarations are checked from two sides: their shape without any DLL, and their behavior against a real Lua 5.3 library.

## How it works

| Tier        | Needs a Lua DLL                  | What it checks                                                                                                                                                                                                    |
|-------------|----------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Shape       | No                               | Header constants, slot types by reflection, forwarder bodies by IL, `lua_Debug` and `luaL_Reg` layouts, pure macros, bind failures, `LuaModule` lookup, the shared fixture's lookup rules (`NativeLuaProbeTests`) |
| Round trips | Yes, tagged `Category=NativeLua` | Every bound export except `lua_error` (`lua_callk` runs through the `lua_call` macro), a second copy of the DLL refused, and Lua calling back into managed code                                                   |

Types alone cannot catch a forwarder wired to a sibling slot of the same type. So the IL test requires each forwarder to
load only its own slot and pass its parameters in order. Bind failures use the main program module as the "not Lua"
module. A failed bind changes nothing, so those tests run safely beside tests that use the bound table. A copy of the
DLL under another file name is a different module for the Windows loader. The second-copy test expects
`LuaApi.Initialize` to refuse it, and skips when the copy cannot load.

Every managed callback is a static cdecl `[UnmanagedCallersOnly]` method that cannot throw, and no test calls
`lua_error`. The library is the Lua of Cheat Engine 7.7 kept in `native/cheat-engine`, unless `CESDK_LUA53_PATH` names
another DLL (see [`tests/CESDK.Tests.Shared/README.md`](../CESDK.Tests.Shared/README.md)). When the library cannot be
bound, the round-trip tests report Skipped. The shape tier
never touches `NativeLuaLibrary`, so it keeps the run from ending with zero executed tests.

## Run the tests

```powershell
dotnet test --project tests/CESDK.Lua.Interop.Tests
dotnet test --project tests/CESDK.Lua.Interop.Tests --filter-trait "Category=NativeLua"
```

## Promise

- Every header constant equals its value in the default 64-bit Lua 5.3 build, including `LUA_REGISTRYINDEX`
  (`LuaConstantsTests`).
- Every table slot is an unmanaged cdecl pointer, and every public forwarder repeats its slot signature
  (`LuaApiSignatureTests`).
- Every forwarder loads its own slot, passes its parameters in order and calls nothing else. Forwarders and macros are
  `AggressiveInlining`, except `luaL_dofile` and `luaL_dostring` (`LuaApiSignatureTests`).
- `lua_Debug` and `luaL_Reg` have the C layout on x64, and the native `lua_Debug` record fits the managed struct
  (`NativeStructLayoutTests`, `CallbackTests`).
- A failed bind leaves the table untouched, and a second Lua module is refused (`LuaApiInitializationTests`,
  `LuaApiBoundTableTests`).
