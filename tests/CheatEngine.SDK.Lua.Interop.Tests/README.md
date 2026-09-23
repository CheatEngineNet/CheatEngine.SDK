# CheatEngine.SDK.Lua.Interop.Tests

Conformance tests for `CheatEngine.SDK.Lua.Interop`, the raw Lua 5.3 function-pointer table.

## Objective

Prove that every declaration in `CheatEngine.SDK.Lua.Interop` matches the Lua 5.3 C API, without Cheat Engine running.

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
`lua_error`. The library is the Lua of Cheat Engine 7.7 kept in `native/cheat-engine`, unless
`CHEATENGINE_SDK_LUA53_PATH` names another DLL (see
[`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md)). When the library cannot be
bound, the round-trip tests report Skipped. The shape tier
never touches `NativeLuaLibrary`, so it keeps the run from ending with zero executed tests.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Lua.Interop.Tests
dotnet test --project tests/CheatEngine.SDK.Lua.Interop.Tests --filter-trait "Category=NativeLua"
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
  `LuaApiBoundTableTests`). A copy of the fixture that lacks one export is refused with exactly that export named,
  and the bound table keeps working (`LuaApiPartialModuleTests`).
- A bridge contract whose bitmap lacks any single required operation is incompatible, whatever extra bits it sets
  (`LuaBridgeContractOperationBitTests`).
- A protected operation started from a host pusher while another one runs, including one that fails, returns to the
  outer operation intact, and the next operation succeeds (`NestedProtectedCallTests`).
- The C11 bridge accepts a forward-compatible minor contract and additive operation bits, while its three generated
  imports are explicitly cdecl and retain GC transitions (`LuaProtectedApiTests`). Native fixture checks additionally
  prove that an incomplete export table, negative input count, and input count above Lua's top return the bridge's
  no-error sentinel without changing the Lua stack (`LuaBridgeContractBoundaryTests`).
- The primitive matrix `tests/CheatEngine.SDK.Repository.Tests/LuaBridge/TestData/lua-interop-primitives.json` has
  exactly one row per public static `LuaApi`
  member, its error class and stack effect equal the member's `Raises:` and `Stack:` remarks, its native symbols are
  exactly the exports the table binds, and the catalogue's direct-call policy agrees with it
  (`LuaInteropPrimitiveMatrixTests`).
