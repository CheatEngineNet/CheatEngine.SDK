# CheatEngine.SDK diagnostics

One page per rule. The help link of every diagnostic
(`https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/<ID>.md`) lands on the page of that rule.
Each page is
written for the plugin author who just saw the message: cause, reason, exact definition, fix, when to suppress.

Identifier ranges: `CESDK0xxx` plugin shape and bootstrap (category `CheatEngine.SDK.Plugin`), `CESDK1xxx`
runtime-safety usage (`CheatEngine.SDK.Usage`), `CESDK2xxx` Lua generator input (`CheatEngine.SDK.Generation`). The
separate EngineApi generator owns
`CESDK3xxx` for curated-spec grammar and generated-identity diagnostics. `CESDK7xxx` marks members obsoleted after
1.0.0 (`[Obsolete(DiagnosticId = …)]`). Identifiers are never renumbered or reused.

| Id                        | Title                                                            | Severity | Code fix                                                                                     |
|---------------------------|------------------------------------------------------------------|----------|----------------------------------------------------------------------------------------------|
| [CESDK0001](CESDK0001.md) | Plugin class cannot be constructed by the generated entry point  | Error    | Replace `abstract` or `static` with `sealed`, add a constructor, make the constructor public |
| [CESDK0002](CESDK0002.md) | More than one plugin class in the assembly                       | Error    | None                                                                                         |
| [CESDK0003](CESDK0003.md) | Manual Cheat Engine bootstrap is missing or malformed            | Error    | Add the exact `CESDK.CESDK.CEPluginInitialize(IntPtr, int)` contract                         |
| [CESDK0004](CESDK0004.md) | Plugin assembly declares a namespace under 'CESDK'               | Warning  | None                                                                                         |
| [CESDK0005](CESDK0005.md) | Source type collides with the generated Cheat Engine entry point | Error    | Rename it, or explicitly own the complete manual bootstrap                                   |
| [CESDK1001](CESDK1001.md) | Plugin startup code calls an enabled-only API                    | Error    | Move the call to `OnEnable`                                                                  |
| [CESDK1003](CESDK1003.md) | A Cheat Engine-owned value is being destroyed                    | Error    | Keep it borrowed or use an explicit `Owned<T>` transfer                                      |
| [CESDK1004](CESDK1004.md) | Exception can escape an [UnmanagedCallersOnly] method            | Warning  | Wrap the body in try/catch                                                                   |
| [CESDK1005](CESDK1005.md) | Plugin lifecycle callback must not be `async void`               | Error    | Keep `OnEnable`/`OnDisable` synchronous                                                      |
| [CESDK2001](CESDK2001.md) | Lua binding needs AllowUnsafeBlocks                              | Error    | None                                                                                         |
| [CESDK2002](CESDK2002.md) | Type cannot receive a generated Lua binding part                 | Error    | None                                                                                         |
| [CESDK2003](CESDK2003.md) | [LuaFunction] method cannot be exported by a generated thunk     | Error    | None                                                                                         |
| [CESDK2004](CESDK2004.md) | [LuaGlobal] method cannot receive a generated body               | Error    | None                                                                                         |
| [CESDK2005](CESDK2005.md) | Lua function name is duplicated                                  | Error    | Give one valid export a distinct Lua name                                                    |
| [CESDK2006](CESDK2006.md) | Lua annotation target cannot receive generated code              | Error    | Declare the supported borrowed-handle/member shape                                           |
| [CESDK2007](CESDK2007.md) | User member collides with a generated Lua binding identity       | Error    | Rename the member or change the binding declaration                                          |
| [CESDK2010](CESDK2010.md) | Optional Lua argument is not in a trailing run                   | Error    | Declare optional arguments after the required ones                                           |
| [CESDK2011](CESDK2011.md) | Optional or variadic Lua result shape is invalid                 | Error    | Required, then optional results; one variadic pair last                                      |
| [CESDK2012](CESDK2012.md) | Type impersonates an SDK Lua contract type                       | Error    | Use the CheatEngine.SDK.Lua type                                                             |
| [CESDK2013](CESDK2013.md) | LuaOptional is not supported in this position                    | Error    | Use an argument or an `out` result of a supported kind                                       |
| [CESDK3001](CESDK3001.md) | Engine API specification is invalid                              | Error    | Correct the reported spec line                                                               |
| [CESDK3002](CESDK3002.md) | Engine API specification has a generated-identity conflict       | Error    | Keep one spec file per generated type                                                        |
| [CESDK3003](CESDK3003.md) | Engine API specification does not declare the ce77 contract      | Error    | Add the `contract: ce77` header                                                              |
| [CESDK3004](CESDK3004.md) | Engine API optional argument is invalid                          | Error    | Declare `opt:` arguments last                                                                |
| [CESDK3005](CESDK3005.md) | Engine API optional or variadic result is invalid                | Error    | `result:`, then `opt-result:`, then one `rest:`                                              |
| [CESDK7001](CESDK7001.md) | PointerSize.FromArchitecture is obsolete                         | Warning  | None; use `TargetArchitectureObservation.ConfiguredPointerSize` or `Bitness`                 |

Configure a rule like any other analyzer diagnostic:

```ini
[*.cs]
dotnet_diagnostic.CESDK1004.severity = error
```

`CESDK0002` through `CESDK0005`, and `CESDK2005`, are compilation-end diagnostics. They need the whole project, so
they are reported in build output and in full-solution analysis where an IDE offers it, not while typing. The
generation-specific plugin rules run only when the direct package build property explicitly supplies
`CheatEngineSdkGenerateEntryPoint=true`; explicit `false` activates the manual-bootstrap check `CESDK0003`, and an
absent property stays silent.

`CESDK2005` carries the `CompilationEnd` tag because it flags two valid `[LuaFunction]` members of one type that share
a Lua name. The tag lets the local shape diagnostics (`CESDK2001`–`CESDK2004`, `CESDK2006`, `CESDK2007`) remain visible
while typing.

`CESDK1002` is intentionally unassigned until the CE 7.7 main-thread dispatcher contract has opt-in live evidence. A
runtime guard exists today; that is not enough evidence for a static thread-affinity analyzer rule.

Adding a rule: constant in `CheatEngine.SDK.Analyzers/Diagnostics/DiagnosticIds.cs`, descriptor in
`DiagnosticDescriptors.cs`, row
in `AnalyzerReleases.Unshipped.md`, page here, tests. `DiagnosticCatalogTests` fails when the page, release-tracking row
or tracking uniqueness of a descriptor is missing.
