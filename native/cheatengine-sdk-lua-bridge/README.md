# CheatEngine.SDK Lua bridge

This deliberately tiny **C11**, Windows x64 micro-kernel puts host Lua operations that can allocate, invoke a finalizer,
or call `lua_error` beneath a native `lua_pcallk` boundary. Lua 5.3 uses `setjmp`/`longjmp` for those
failures; [Microsoft documents that .NET does not support that interop and a
`longjmp` must not cross or skip a managed frame](https://learn.microsoft.com/dotnet/standard/native-interop/exceptions-interoperability#setjmplongjmp-behaviors).
The public SDK remains C# 14/.NET 10 and AOT-friendly; this source exists only for the native control-flow boundary.

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
[`protected-operations.json`](../../libs/CheatEngine.SDK.Lua.Interop/Protected/protected-operations.json) catalogue, the generated managed
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
[
`lapi.c`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/lua53/lua53/src/lapi.c#L99-L116)
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

[`bridge-audit-manifest.json`](bridge-audit-manifest.json) records the committed bridge: the SHA-256 of
`cheatengine_sdk_lua_bridge.c` and `xmake.lua`, the fingerprint the DLL embeds, the DLL SHA-256, its PE facts (PE32+,
AMD64, DLL, exports, import modules, no delay-import directory) and the pinned xmake version. `BridgeAuditManifestTests`
in `tests/CheatEngine.SDK.Tests` compares it with the committed blob (`git cat-file`), never with the working-tree DLL,
because CI replaces that file with the bridge it builds before building the SDK. The CI-built and committed DLL bytes
may differ: the committed DLL was not necessarily built by the runner's pinned MSVC toolset. The `native` job reports
such a difference as a drift notice, `build-info.json` records both hashes, and the release run repeats the notice for
the bridge it packed; it is never a failure. When a manifest test fails, its message prints the complete expected
manifest, and replacing the file with it is the regeneration procedure.

Never commit a locally built DLL: a local Visual Studio toolset is not the runner's pinned one, so a local build has
different bytes than the bridge CI builds, tests and packs. After changing `cheatengine_sdk_lua_bridge.c` or
`xmake.lua`, use the CI round trip:

1. Push the change. The `native` job builds the bridge three times (a second output directory and a copy of the
   build inputs outside the repository), requires identical bytes, uploads it as the `lua-protection-bridge`
   artifact, and then fails its fingerprint check because the committed DLL is now stale.
2. Download that run's artifact over the checked-in asset:
   `gh run download <run-id> -n lua-protection-bridge -D native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native`.
3. Commit the DLL, run
   `dotnet test --project tests/CheatEngine.SDK.Tests --filter-class CheatEngine.SDK.Tests.Packaging.BridgeAuditManifestTests`,
   replace `bridge-audit-manifest.json` with the manifest its failure prints, and commit it in the same pull request, so
   the squash merge lands the DLL, its sources and its manifest together.

The xmake target requires MSVC, C11, Windows x64, static CRT (`/MT`) and reproducible linking (`/Brepro`). `/MT` is
both declared through xmake and passed explicitly to prevent an MSVC/UCRT runtime DLL dependency in the CE host. The
dedicated PE contract test enforces PE32+ x64, exactly the four exports, an import allowlist of `KERNEL32.dll`, and no
delay-import directory or Lua import. Commit the rebuilt prebuilt DLL with its source, build script, and contract test.
