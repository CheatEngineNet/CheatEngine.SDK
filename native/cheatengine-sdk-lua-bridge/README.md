# CheatEngine.SDK Lua bridge

This deliberately tiny **C11**, Windows x64 micro-kernel puts host Lua operations that can allocate, invoke a finalizer,
or call `lua_error` beneath a native `lua_pcallk` boundary. Lua 5.3 uses `setjmp`/`longjmp` for those failures; [Microsoft documents that .NET does not support that interop and a `longjmp` must not cross or skip a managed frame](https://learn.microsoft.com/dotnet/standard/native-interop/exceptions-interoperability#setjmplongjmp-behaviors). The public SDK remains C# 14/.NET 10 and AOT-friendly; this source exists only for the native control-flow boundary.

The bridge receives function pointers from Cheat Engine's already-loaded Lua module. It includes neither `lua.h` nor a
Lua import library and has no `LoadLibrary`/`GetProcAddress` path, so it cannot create or load a second Lua runtime.

`runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll` is the checked-in source asset. CheatEngine.SDK packs it as
the direct-consumer build asset `build/native/cheatengine-sdk-lua-bridge.dll`, rather than as a transitive runtime
asset. Plugin authors and contributors making managed-only changes therefore need neither xmake nor a C compiler. CI
rebuilds the DLL before every build, test, and package job, so released packages use the bridge compiled from that
commit.

The legacy `cheatengine_sdk_lua_bridge_abi_version` export remains at ABI version `1` for already-published clients.
New managed code requires the versioned contract `1.1` from `cheatengine_sdk_lua_bridge_get_contract` before its first
protected operation. That contract has a magic value, major/minor version, contract and Lua-export-table sizes, pointer,
`lua_Integer` and `size_t` widths, plus the exact supported-operation bitmap. Managed code checks it with
`Unsafe.SizeOf` and also checks the fixed native export list. If the exported surface or operation contract changes,
update `cheatengine_sdk_lua_bridge.c`, the versioned
[`protected-operations.json`](../../eng/lua-bridge/protected-operations.json) catalogue, the generated managed
projection from `source-generators/CheatEngine.SDK.SourceGenerators.LuaBridgeContract` and its
`tests/CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests` contract tests, and the managed adapter as
applicable; then rebuild the DLL and commit the matching assets together. The C11 bridge remains the owner of its
native enum/cases and of the DLL; `Lua.Interop` consumes the catalogue as an `AdditionalFile`.

The DLL exports exactly four undecorated names: `cheatengine_sdk_lua_protected`,
`cheatengine_sdk_lua_bridge_abi_version`, `cheatengine_sdk_lua_bridge_get_contract`, and
`cheatengine_sdk_lua_bridge_source_fingerprint`. C11 `_Static_assert`s pin each of the 20 host-export-table slots and
every contract offset; the managed contract check independently verifies the same layout.

`OP_PUSH_HOST_OBJECT` invokes Cheat Engine's `LuaPushClassInstance`-shape callback under `lua_pcallk`; a Lua error from
the host pusher therefore becomes a Lua status rather than crossing a managed frame. The bridge has no Lua import
library, no `lua.h` include and no runtime-loading code: it receives the already-loaded host Lua exports as pointers.

Before pushing its zero-upvalue light C closure, the bridge validates all 20 host export pointers, validates the input
depth, and calls `lua_checkstack(L, 1)`. The pinned upstream CE Lua 5.3.0
[`lapi.c`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/lua53/lua53/src/lapi.c#L99-L116)
shows that `lua_checkstack` uses `luaD_rawrunprotected` and reports an allocation/overflow failure as `0`; it does not
unwind to the bridge caller. After that reservation `lua_pushcclosure(operation, 0)` is a non-allocating light C
function, while every bridge-owned operation that can raise starts only after `lua_pcallk`. This is `PinnedUpstream`
comparative source evidence, not evidence that the exact installed CE 7.7 host has executed this path and not a
substitute for the opt-in live probes.

The fixture child-process probe `CheatEngine.SDK.Lua.FailureProbe --checkstack-growth` forces allocation refusal during
a 4,096-slot reserve. It proves `TryEnsureStack` returns `0`, keeps the fixture stack at zero, and permits a subsequent
successful reserve. It is fixture evidence only; live CE 7.7 validation remains opt-in.

xmake also embeds SHA-256 hashes of the C source and build file. Those two inputs are pinned to LF checkout bytes in
the repository `.gitattributes`, so their raw hashes are identical on Windows and in CI. CI compares that fingerprint
with the checked-in DLL; when it is stale, the job fails after uploading the corrected `lua-protection-bridge` artifact
for a maintainer to commit.

Only rebuild it locally after changing `cheatengine_sdk_lua_bridge.c` or `xmake.lua`:

```powershell
$output = 'artifacts/native/cheatengine-sdk-lua-bridge'
$outputPath = Join-Path $PWD $output
xmake f -P native/cheatengine-sdk-lua-bridge -o $output -p windows -a x64 -m release -y
xmake -P native/cheatengine-sdk-lua-bridge -y
Copy-Item (Join-Path $outputPath 'cheatengine-sdk-lua-bridge.dll') native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll
```

The xmake target requires MSVC, C11, Windows x64, static CRT (`/MT`) and reproducible linking (`/Brepro`). `/MT` is
both declared through xmake and passed explicitly to prevent an MSVC/UCRT runtime DLL dependency in the CE host. The
dedicated PE contract test enforces PE32+ x64, exactly the four exports, an import allowlist of `KERNEL32.dll`, and no
delay-import directory or Lua import. Commit the rebuilt prebuilt DLL with its source, build script, and contract test.
