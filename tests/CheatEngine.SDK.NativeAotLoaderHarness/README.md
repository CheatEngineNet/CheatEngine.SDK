# CheatEngine.SDK.NativeAotLoaderHarness

This managed executable is the bounded SDK-006 harness for a NativeAOT **test library**. It is not a test runner for
Cheat Engine plugins and does not start or attach to Cheat Engine.

It has exactly two modes:

- `--analyze <probe.dll>` reads PE bytes with `PEReader`. It verifies the PE32+ AMD64 image and the two inert fixture
  export names without mapping the DLL.
- `--load --acknowledge-process-resident-load` has no file-path argument. It derives the fixed fixture file name from
  its own published directory, locks it against replacement, then maps it in the short-lived harness process. It
  performs name queries only; it neither invokes an export nor frees the module.

The load mode refuses any file that has a `CEPlugin_` export. It cannot be pointed at an arbitrary path or production
plugin: Windows loader initialization can execute while a DLL is mapped even when no export is invoked. Publish both
profile components into the same new output directory, then invoke the published harness:

```powershell
dotnet publish tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj -c Release -o artifacts/nativeaot-loader-profile
dotnet publish tests/CheatEngine.SDK.NativeAotLoaderHarness/CheatEngine.SDK.NativeAotLoaderHarness.csproj -c Release -o artifacts/nativeaot-loader-profile

artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLoaderHarness.exe --analyze artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLibraryProbe.dll
artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLoaderHarness.exe --load --acknowledge-process-resident-load
```

`library.unload=not-attempted` is an intentional result. NativeAOT shared libraries do not support `FreeLibrary`/
`dlclose` unloading, so process termination is the boundary for this observation. See
[ADR-006](../../documentations/engineering/ADR-006-nativeaot-plugin-loader-profile.md).
