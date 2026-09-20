# CheatEngine.SDK.SourceGenerators.EngineApi

Repository-internal Roslyn generator that turns a curated text spec of Cheat Engine Lua globals into typed C# wrappers
inside `CheatEngine.SDK.Engine`. It never ships in the `CheatEngine.SDK` package.

## Objective

Read every additional file named `*.cheatengine-sdk-api.txt` and emit one complete wrapper method per spec entry. Each wrapper
calls one Cheat Engine Lua global through the protected call shape that `CheatEngine.SDK.Lua` defines.

## Why it exists

`CheatEngine.SDK.Engine` wraps Cheat Engine's own Lua functions. Every wrapper needs the same body: acquire the state, push, call,
read the results, restore the stack. A spec states each wrapper once in text. The generator writes the body, so all
wrappers share one shape and one set of failure rules.

The generator uses the emitter behind `[LuaGlobal]` (`LuaGlobalCallEmitter`, described in [
`../CheatEngine.SDK.SourceGenerators.Shared/README.md`](../CheatEngine.SDK.SourceGenerators.Shared/README.md)) through the shared assembly,
never through the LuaBindings generator. It cannot reuse the output of
`CheatEngine.SDK.SourceGenerators.LuaBindings`, because source generators do not see each other's output. It therefore writes
complete declarations, never the body half of a partial method.

## How it works

`libs/CheatEngine.SDK.Engine/CheatEngine.SDK.Engine.csproj` loads the generator as an analyzer and passes the spec as an additional file:

```xml
<ProjectReference Include="../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/CheatEngine.SDK.SourceGenerators.EngineApi.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false"/>
<AdditionalFiles Include="../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/memory-scalars.cheatengine-sdk-api.txt"/>
```

Only files explicitly included as `AdditionalFiles` participate in one generator invocation. At present the shipping
Engine project includes `memory-scalars.cheatengine-sdk-api.txt`. The adjacent CE 7.7 runtime, inspection, allocation,
AOB, scan and address-list manifests are provenance/reservation documents for contracts the scalar grammar cannot yet
represent, or candidates awaiting an explicit reviewed inclusion. They are not silently generated, and their presence
does not imply a wrapper exists. Their manual vertical-slice APIs document their own evidence and ownership/thread
boundaries.

1. The generator keeps additional files whose name ends in `.cheatengine-sdk-api.txt`, ignoring case, and parses each one into a
   model of strings and enums.
2. It reports every malformed header or entry as a localized `CESDK3001` error on the originating additional file, then
   drops only that invalid entry. A valid sibling entry still generates normally.
3. It gives each generated type exactly one spec-file owner. A duplicate target type, wrapper member, or cache field
   reports `CESDK3002` on every participating file and emits none of the conflicting files.
4. Each remaining file becomes one generated source that compiles into `CheatEngine.SDK.Engine.dll`.

`EngineApiGenerator`, in namespace `CheatEngine.SDK.SourceGenerators.EngineApi`, is the only public type. The parser uses no
Roslyn type, so tests call it with plain strings. It has no JSON dependency because NuGet does not resolve an analyzer's
dependencies and `System.Text.Json` is not part of `netstandard2.0`.

## Spec file format

A spec file is plain text made of `key: value` lines. Blank lines separate blocks. The first block is the header and
every later block is one entry. A line that starts with `#` is a comment and never separates blocks. Indentation and
CRLF line endings are tolerated.

| Key         | Block  | Count              | Meaning                                                                                                                                    |
|-------------|--------|--------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `namespace` | header | 1                  | Namespace of the generated type. An empty value means the global namespace.                                                                |
| `type`      | header | 1                  | Name of the one `public static partial class` the file emits.                                                                              |
| `contract`  | header | 0 or 1             | `ce77` opts into the mandatory, machine-validated CE 7.7 evidence contract below.                                                         |
| `provenance`| header | with `contract`    | A proof status followed by `: ` and an immutable source reference.                                                                         |
| `minimum-ce`| header | with `contract`    | Exact four-part minimum CE version, for example `7.7.0.10621`.                                                                             |
| `architecture`| header | with `contract`  | `x64`; this generator makes no public target-address claim on another architecture.                                                       |
| `thread`    | header | with `contract`    | `any`, `main`, or `unknown`; `unknown` records absent proof rather than granting thread safety.                                          |
| `ownership` | header | with `contract`    | `none`, `borrowed`, or `owned`. Object contracts remain deliberately outside the scalar grammar.                                          |
| `global`    | entry  | 1                  | The Lua global to call: an ASCII identifier that is not a Lua 5.3 reserved word.                                                           |
| `method`    | entry  | 1                  | The C# method name. A C# reserved word gets an `@` prefix.                                                                                 |
| `form`      | entry  | 1                  | `try` returns `bool` and writes `out` results. `throwing` returns the value, or `void`, and raises `LuaException` when the Lua call fails. |
| `doc`       | entry  | 1                  | One line of original English, emitted as the XML `<summary>`. Never copy Cheat Engine documentation.                                       |
| `arg`       | entry  | 0 or more          | `name:kind`, one pushed argument, in order.                                                                                                |
| `fixed`     | entry  | 0 or more          | `boolean:true` or `boolean:false`, one host-required Lua argument omitted from the C# signature, after all `arg` values.                |
| `result`    | entry  | `try`: 1 or more   | `name:kind`, one `out` result, in read order. Not allowed in a `throwing` entry.                                                           |
| `return`    | entry  | `throwing`: 0 or 1 | The kind of the returned value. Omit it for `void`. Not allowed in a `try` entry.                                                          |
| `nil`       | entry  | with `contract`    | `none`, `absence`, `expected-failure`, or `lua-error`: CE result semantics after a protected call succeeds.                              |

The kinds are `int32`, `int64`, `single`, `double`, `boolean`, `address`, `utf8`, `string` and `string?`. A `utf8` value
is valid only as an argument, because a span result would dangle once the wrapper restores the stack. Two entries may
bind the same `global`, for example a `try` and a `throwing` form. They share one cache field. A `method` name used by
more than one entry, a parameter that collides with an emitted local, or an identity that collides with a generated
raw core/cache field is rejected with `CESDK3001` before code generation.

### CE 7.7 evidence contract

All shipping and newly authored EngineApi specs use `contract: ce77`. The header carries shared facts; each entry adds
`nil`, while the existing `form` and `return` fields remain the public return contract. The parser copies these facts to
every `SpecCallModel`, validates them at the actual field location, and emits them in XML `<remarks>`. This makes
provenance, minimum version, architecture, thread-affinity, ownership, return form and nil/absence/error semantics
inspectable without treating a comment as an API contract.

Legacy fixtures without `contract` remain readable only to keep the repository migration incremental. They cannot use
contract fields or `nil`; new production specs must not use that compatibility path.

```text
namespace: CheatEngine.SDK.Engine.Generated
type: MemoryScalars
contract: ce77
provenance: ExactInstalledFile: CE 7.7 celua.txt scalar memory globals
minimum-ce: 7.7.0.10621
architecture: x64
thread: unknown
ownership: none

global: readInteger
method: TryReadInt32
form: try
arg: address:address
fixed: boolean:true
result: value:int32
nil: absence
doc: Reads a 32-bit integer from the target process.
```

## What gets generated

One file per spec file: the generated-code header, the namespace block (omitted for the global namespace), one
`public static partial class`, one `private static readonly LuaRef` field per distinct global, then one method per
entry, sorted by method name.

Every entry that uses `address` yields two methods. A private `__<Method>Raw` core uses the proven `nuint` call shape;
the public wrapper exposes `global::CheatEngine.SDK.Engine.Values.Address` for arguments, `try` outputs, and throwing
returns. It converts arguments with `unchecked((nuint)address.ToUInt64())` and raw results with
`new Address(unchecked((ulong)raw))`. The example above yields the public
`bool TryReadInt32(Address address, out int value)`. A spec with an `address` contract compiles only where
`CheatEngine.SDK.Engine.Values.Address` exists.

`Specs/memory-scalars.cheatengine-sdk-api.txt` is the one spec currently wired into the Engine build, and it also shows
the `throwing` form. Its entries produce `CheatEngine.SDK.Engine.Generated.MemoryScalars` with `TryReadInt32`,
`WriteInt32`, `TryReadInt64` and `WriteInt64`. `CheatEngine.SDK.Engine.csproj` names its path, so do not rename or move
it. Adding another manifest is an API change: first extend the grammar with a localized diagnostic for every unsupported
shape, then include that one file and add semantic, fixture and live-opt-in coverage appropriate to its contract.

## Promise

| You can rely on                                                                                                                                               | Backed by                                                                                                                                          |
|---------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| The generator never ships.                                                                                                                                    | `src/CheatEngine.SDK/CheatEngine.SDK.csproj` packs only the analyzer references it marks `PackAsAnalyzer`, and it does not reference this project. |
| Every invalid spec issue is a localized `CESDK3001` error; an unrelated valid entry still emits.                                                            | `Parsing/SpecFileParserTests.cs`, `Generator/DiagnosticsTests.cs`                                                                                   |
| A ce77 spec carries validated provenance/version/architecture/thread/ownership/return/nil facts on every entry and projects them into XML documentation.  | `Parsing/SpecFileParserTests.cs`, `Generator/EmissionTests.cs`                                                                                       |
| A spec file exclusively owns its generated type; duplicate type/member/cache identities are `CESDK3002` errors at each exact field and emit neither file. | `Generator/DiagnosticsTests.cs`                                                                                                                       |
| Emitted code compiles without errors or warnings against the real `CheatEngine.SDK.Annotations`, `CheatEngine.SDK.Lua.Interop` and `CheatEngine.SDK.Lua`.     | `Generator/EmissionTests.cs`                                                                                                                       |
| Editing one spec file re-emits only that file. An unrelated compilation edit recomputes nothing.                                                              | `Generator/IncrementalityTests.cs`                                                                                                                 |
| No parsed model in the pipeline holds a Roslyn object.                                                                                                        | `Pipeline_step_values_hold_no_roslyn_objects` in `Generator/IncrementalityTests.cs`                                                                |
| Wrappers leave the Lua stack as they found it, allocate nothing on the warm success path, and throw `InvalidOperationException` while no runtime is attached. | `EndToEnd/MemoryScalarsEndToEndTests.cs`, run against stand-in Lua globals                                                                         |

## Run the tests

The end-to-end tests carry `Category=NativeLua` and run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md). See [
`tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests`](../../tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests/README.md).

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests --filter-trait "Category=NativeLua"
```
