# Native bridge PE audit

`bridge-audit-manifest.json` is the auditable, versioned contract for the checked-in Windows x64 bridge asset. It
records the xmake/MSVC selection, C11/CRT/warning/linker policy, source hashes, embedded source fingerprint, artifact
SHA-256, the normal-import module allowlist, the empty delay-load contract and the four exported symbols.

The source fingerprint covers raw bytes. The repository therefore pins the bridge C source and `xmake.lua` to LF in
`.gitattributes`; all other text files keep the workspace-wide CRLF policy. This narrow exception makes the recorded
hashes invariant across Windows worktrees and CI checkouts.

The managed test project reads PE/COFF tables directly; it never loads the bridge merely to inspect it. Its assertions
therefore prove that the binary is PE32+ AMD64, is a DLL, has exactly the declared export surface, has no delay imports,
and cannot import a Lua runtime through the ordinary import table. The static CRT legitimately imports several
`KERNEL32.dll` symbols (including generic loader helpers), so the contract allowlists the module rather than falsely
treating those CRT implementation details as bridge code. The C11 source itself has no Lua loader path. The audit also
verifies that the direct consumer's build and publish copies match the audited checked-in DLL byte for byte.

The reader follows Microsoft's [PE/COFF specification](https://learn.microsoft.com/windows/win32/debug/pe-format): it
validates the PE32+ optional-header magic, maps RVAs through section raw-data ranges, reads the export/import directory
tables, and treats a non-empty delay-load directory as a contract failure.

## Updating the contract

After a deliberate change to `cheatengine_sdk_lua_bridge.c` or `xmake.lua`, rebuild the bridge as documented in
`README.md`. Then update the source hashes, fingerprint, artifact SHA-256, exports or import allowlist in the manifest
only after reviewing the resulting PE diff. Run:

```powershell
pwsh eng/lua-bridge/Test-ProtectedOperationCatalog.ps1
dotnet test --project tests/CheatEngine.SDK.Tests
```

The CI native job validates the protected-operation catalogue against the C switch and managed wrappers before it
rebuilds into two separate output directories and compares their SHA-256 values. A mismatch is a reproducibility
failure, not an invitation to change the manifest.

## Scope

This is a structural, non-executing audit. It does not establish Cheat Engine live behavior or substitute for the Lua
longjmp failure probe. Its specific purpose is to enforce that the C11 protection microkernel remains minimal and keeps
using function pointers supplied by Cheat Engine instead of importing, loading or delay-loading a second Lua runtime.
