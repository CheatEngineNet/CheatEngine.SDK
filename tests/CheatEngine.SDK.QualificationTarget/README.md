# CheatEngine.SDK.QualificationTarget

A small, deterministic console program that the exact-host qualification runs attach Cheat Engine to. It is a lab
target, not a test project, not a plugin and never a package.

## Objective

Give the scan, memory and target scenarios of the [qualification matrix](../../docs/qualification/README.md) (Q25–Q32)
a target whose content is known in advance on x64 and on x86: a pattern inside the executable image, copies of it on the
heap, a region with many matches of a second pattern, and two value cells that change on command. Every count the
target reports is measured by the target itself, so a Cheat Engine scan can be compared with ground truth.

## Why it exists

The Phase-0 host spike used the tutorial programs shipped with Cheat Engine, whose global match counts changed with the
modules loaded (35 against 31 for the same pattern). A qualification result needs a target whose layout the repository
controls and whose hash each receipt records. The machine that runs the qualification has no x86 .NET 10 runtime (only
x86 .NET 6), so the target is published with Native AOT, which supports win-x86 since .NET 9
([Native AOT platform restrictions](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#platform-architecture-restrictions)).

## How it works

| File              | Content                                                                                                                                                         |
|-------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `TargetLayout.cs` | The 16-byte module-resident marker (constant data of the image), the 8-byte many-results pattern (computed at run time, so it is not in the image), the initial values. |
| `TargetMemory.cs` | Pinned heap arrays: N marker copies 64 bytes apart, M back-to-back repetitions of the second pattern, and the Int32/Int64 cells.                                 |
| `ImageScanner.cs` | Counts a pattern in every readable section of the executable image, or in a heap region.                                                                       |
| `ReadyRecord.cs`  | The one-line JSON records, written with `Utf8JsonWriter` (no reflection, trim-safe).                                                                           |
| `Program.cs`      | Arguments, the ready record, and the `step` / `exit` command loop on standard input.                                                                            |

On start the target prints one line and waits for commands:

```json
{"schema":"cheatengine-qualification-target/v0","pid":46920,"arch":"x64","pointerSize":8,"imageBase":"0x7FF7B65F0000","imageSize":1617920,
 "regions":[{"id":"module-marker","base":"0x7FF7B65F0000","length":1617920,"pattern":"C3 5D 4B 51 54 9A 7E 21 E8 0F B2 66 13 D7 4C A5","count":2},
            {"id":"heap-marker","base":"0x1E288001588","length":512,"pattern":"C3 5D 4B 51 54 9A 7E 21 E8 0F B2 66 13 D7 4C A5","count":8},
            {"id":"many-results","base":"0x1E2880017A0","length":160000,"pattern":"8F A2 F9 0C 23 76 8D A0","count":20000}],
 "values":[{"id":"int32","address":"0x1E2880288B8","value":1573167383},{"id":"int64","address":"0x1E2880288C0","value":81985529216486895}]}
```

(Shown wrapped; the real record is one line and the addresses differ per run.) The `module-marker` count is measured
over the image's readable sections; the win-x64 and win-x86 images of the first build held it twice, because the
compiler may emit a constant more than once, which is why the count is measured and never assumed. The heap counts are
measured over their regions. Patterns that must be absent are chosen by the qualification driver, never stored in the
target.

| Input                        | Effect                                                                                  |
|------------------------------|-----------------------------------------------------------------------------------------|
| `--heap-copies N`            | Number of heap marker copies, 1–1024 (default 8).                                       |
| `--repetitions M`            | Number of many-results repetitions, 1–1000000 (default 20000).                          |
| `--ready-file PATH`          | Also writes the ready record to PATH; the only file the target ever writes.             |
| `step` on standard input     | Adds one to both value cells and prints `{"event":"step","int32":…,"int64":…}`.        |
| `exit` or end of input       | Exits with code 0.                                                                      |

The target opens no network connection, requests no elevation, reads no file and loads no SDK assembly.

## Promise

- The target is in the solution, publishes with Native AOT for `win-x64` and `win-x86`, is not a test module and never
  packs (`QualificationProjectShapeTests.QualificationTarget_is_in_the_solution_and_publishes_native_aot_for_x64_and_x86`,
  `QualificationProjectShapeTests.Qualification_harnesses_are_not_test_modules_and_never_pack`).
- It references no SDK project, so a qualification observes the SDK only from the plugin side
  (`QualificationProjectShapeTests.QualificationTarget_references_no_SDK_project`).
- The ready record and the counts are produced by the published executable; the local runner
  ([`eng/qualification`](../../eng/qualification/README.md)) publishes it per run and records its SHA-256 in each receipt.

## Run the tests

The project has no test module of its own; its shape is checked by
`dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj`. To try it:

```powershell
dotnet publish tests/CheatEngine.SDK.QualificationTarget/CheatEngine.SDK.QualificationTarget.csproj -c Release -r win-x64 -o <folder>
```

then run `<folder>/CheatEngine.SDK.QualificationTarget.exe`, type `step`, then `exit`. Use `-r win-x86` for the x86
build; both need the MSVC linker that Native AOT uses on Windows.
