# CESDK Lua bridge

The bridge keeps allocating Lua C API operations beneath a native `lua_pcallk` boundary, so Lua errors never unwind
through managed frames. It receives function pointers from Cheat Engine's already-loaded Lua module and does not link or
load another Lua runtime.

`runtimes/win-x64/native/cesdk-lua-bridge.dll` is checked in and packed with CESDK. Plugin authors and contributors
making managed-only changes therefore need neither xmake nor a C compiler. CI rebuilds the DLL before every build, test,
and package job, so released packages use the bridge compiled from that commit.

The DLL exports ABI version `1`. Managed code checks that version before its first protected operation, and packaging
tests check the prebuilt DLL directly. If the exported surface or operation contract changes, increment the version in
both `cesdk_lua_bridge.c` and `LuaProtectedApi.cs`, rebuild the DLL, and commit all three changes together.

xmake also embeds SHA-256 hashes of the C source and build file. CI compares that fingerprint with the checked-in DLL;
when it is stale, the job fails after uploading the corrected `lua-protection-bridge` artifact for a maintainer to commit.

Only rebuild it locally after changing `cesdk_lua_bridge.c` or `xmake.lua`:

```powershell
$output = Join-Path $PWD 'artifacts/native/cesdk-lua-bridge'
xmake f -P native/cesdk-lua-bridge -o $output -p windows -a x64 -m release -y
xmake -P native/cesdk-lua-bridge -y
Copy-Item "$output/cesdk-lua-bridge.dll" native/cesdk-lua-bridge/runtimes/win-x64/native/cesdk-lua-bridge.dll
```

The xmake target uses reproducible MSVC linking, so rebuilding twice with the same toolchain and source produces the
same bytes. Commit the updated prebuilt DLL together with its source.
