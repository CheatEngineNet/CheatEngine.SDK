# CESDK diagnostics

One page per rule. The help link of every diagnostic
(`https://github.com/ShadowNineX/CESDK/blob/main/analyzers/docs/<ID>.md`) lands on the page of that rule. Each page is
written for the plugin author who just saw the message: cause, reason, exact definition, fix, when to suppress.

Identifier ranges: `CESDK0xxx` plugin shape and bootstrap (category `CESDK.Plugin`), `CESDK1xxx` runtime-safety usage
(`CESDK.Usage`), `CESDK2xxx` generator input (`CESDK.Generation`). Identifiers are never renumbered or reused.

| Id                        | Title                                                           | Severity | Code fix                                                                                     |
|---------------------------|-----------------------------------------------------------------|----------|----------------------------------------------------------------------------------------------|
| [CESDK0001](CESDK0001.md) | Plugin class cannot be constructed by the generated entry point | Error    | Replace `abstract` or `static` with `sealed`, add a constructor, make the constructor public |
| [CESDK0002](CESDK0002.md) | More than one plugin class in the assembly                      | Error    | None                                                                                         |
| [CESDK0004](CESDK0004.md) | Plugin assembly declares a namespace under 'CESDK'              | Warning  | None                                                                                         |
| [CESDK1004](CESDK1004.md) | Exception can escape an [UnmanagedCallersOnly] method           | Warning  | Wrap the body in try/catch                                                                   |
| [CESDK2001](CESDK2001.md) | Lua binding needs AllowUnsafeBlocks                             | Error    | None                                                                                         |
| [CESDK2002](CESDK2002.md) | Type cannot receive a generated Lua binding part                | Error    | None                                                                                         |
| [CESDK2003](CESDK2003.md) | [LuaFunction] method cannot be exported by a generated thunk    | Error    | None                                                                                         |
| [CESDK2004](CESDK2004.md) | [LuaGlobal] method cannot receive a generated body              | Error    | None                                                                                         |

Configure a rule like any other analyzer diagnostic:

```ini
[*.cs]
dotnet_diagnostic.CESDK1004.severity = error
```

`CESDK0002` and `CESDK0004` are compilation-end diagnostics. They need the whole project, so they are reported in build
output and in full-solution analysis where an IDE offers it, not while typing.

`CESDK2003` carries the `CompilationEnd` tag on its whole descriptor because of one check, `DuplicateName`. That check
flags two `[LuaFunction]` members of one type that share a Lua name, so it needs every sibling member. The tag makes an
IDE defer the whole rule to build or full-solution analysis.

Adding a rule: constant in `CESDK.Analyzers/Diagnostics/DiagnosticIds.cs`, descriptor in `DiagnosticDescriptors.cs`, row
in `AnalyzerReleases.Unshipped.md`, page here, tests. `DiagnosticCatalogTests` fails when the page or the
release-tracking row of a descriptor is missing.
