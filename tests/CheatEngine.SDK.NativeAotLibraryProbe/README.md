# CheatEngine.SDK.NativeAotLibraryProbe

This is an inert `win-x64` NativeAOT shared-library fixture for SDK-006. It has two deliberately non-Cheat-Engine
exports and no reference to the SDK shipping graph. It is not a plugin, does not contain any `CEPlugin_*` export, and
its exports must never be called by the loader harness.

Publish it only for the separate library-analysis gate:

```powershell
dotnet publish tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj -c Release -o artifacts/nativeaot-library-probe
```

The adjacent loader harness can inspect its export directory without loading it, then—only with an acknowledgement—map
the fixed-name output placed beside its own published executable and query the two names. It locks the file while it
checks and maps it, and intentionally does not call `NativeLibrary.Free`.
See [ADR-006](../../documentations/engineering/ADR-006-nativeaot-plugin-loader-profile.md) and the harness README for
the exact boundary.
