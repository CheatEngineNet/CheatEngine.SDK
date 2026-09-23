# CheatEngine.SDK.SourceGenerators.EngineApi

Repository-internal Roslyn generator that turns a curated text spec of Cheat Engine Lua globals into typed C# wrappers
inside `CheatEngine.SDK.Engine`. It never ships in the `CheatEngine.SDK` package.

## Objective

Read every additional file named `*.cheatengine-sdk-api.txt` and emit one complete wrapper method per spec entry. Each
wrapper
calls one Cheat Engine Lua global through the protected call shape that `CheatEngine.SDK.Lua` defines.

## Why it exists

`CheatEngine.SDK.Engine` wraps Cheat Engine's own Lua functions. Every wrapper needs the same body: acquire the state,
push, call,
read the results, restore the stack. A spec states each wrapper once in text. The generator writes the body, so all
wrappers share one shape and one set of failure rules.

The generator uses the emitter behind `[LuaGlobal]` (`LuaGlobalCallEmitter`, described in [
`../CheatEngine.SDK.SourceGenerators.Shared/README.md`](../CheatEngine.SDK.SourceGenerators.Shared/README.md)) through
the shared assembly,
never through the LuaBindings generator. It cannot reuse the output of
`CheatEngine.SDK.SourceGenerators.LuaBindings`, because source generators do not see each other's output. It therefore
writes
complete declarations, never the body half of a partial method.

## How it works

`libs/CheatEngine.SDK.Engine/CheatEngine.SDK.Engine.csproj` loads the generator as an analyzer and passes the spec as an
additional file:

```xml
<ProjectReference Include="../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/CheatEngine.SDK.SourceGenerators.EngineApi.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false"/>
<AdditionalFiles Include="../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/memory-scalars.cheatengine-sdk-api.txt"/>
```

Only files explicitly included as `AdditionalFiles` participate in one generator invocation. Every production spec
declares `contract: ce77` and parses without an issue, and they fall in three groups:

| Spec                                               | Status                                                                                                  |
|----------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| `memory-scalars.cheatengine-sdk-api.txt`           | Wired: generates `CheatEngine.SDK.Engine.Generated.MemoryScalars`.                                      |
| `runtime-capabilities.cheatengine-sdk-api.txt`     | Reviewed, not wired yet: runtime capability probes (the runtime lot wires it).                          |
| `allocation-protection.cheatengine-sdk-api.txt`    | Reviewed, not wired: the public `Address`/`TargetAllocationRequest` contract is hand-written in Engine. |
| `modules-symbols-regions.cheatengine-sdk-api.txt`  | Reviewed, not wired: the public inspection wrapper uses the same global directly.                       |
| `addresslist-memoryrecord.cheatengine-sdk-api.txt` | Header-only reservation: needs the object grammar.                                                      |
| `aob-stringlist.cheatengine-sdk-api.txt`           | Header-only reservation: needs the object-result grammar.                                               |
| `memscan-foundlist.cheatengine-sdk-api.txt`        | Header-only reservation: needs the object-method grammar.                                               |

A reviewed or reserved spec is not silently generated, and its presence does not imply that a wrapper exists. The
number of entries is not a coverage measure: the four `MemoryScalars` entries are four explicit contracts, not the Lua
surface of `CheatEngine.SDK.Engine`, most of which is hand-written (audit AX06-18; coverage is measured by the Lua
surface catalogue, not by this generator's spec count). `Parsing/ProductionSpecsTests.cs` reads the committed specs
and the Engine project file and fails when a spec with entries is neither wired nor in the reviewed list.

1. The generator keeps additional files whose name ends in `.cheatengine-sdk-api.txt`, ignoring case, and parses each
   one into a
   model of strings and enums.
2. It reports every malformed header or entry as a localized `CESDK3001` error on the originating additional file, then
   drops only that invalid entry. A valid sibling entry still generates normally. A file with entries and no
   `contract: ce77` is `CESDK3003` and generates nothing; an invalid optional argument is `CESDK3004`; an invalid
   optional or variadic result is `CESDK3005`.
3. It gives each generated type exactly one spec-file owner. A duplicate target type, wrapper member, or cache field
   reports `CESDK3002` on every participating file and emits none of the conflicting files.
4. Each remaining file becomes one generated source that compiles into `CheatEngine.SDK.Engine.dll`.

`EngineApiGenerator`, in namespace `CheatEngine.SDK.SourceGenerators.EngineApi`, is the only public type. The parser
uses no
Roslyn type, so tests call it with plain strings. It has no JSON dependency because NuGet does not resolve an analyzer's
dependencies and `System.Text.Json` is not part of `netstandard2.0`.

## Spec file format

A spec file is plain text made of `key: value` lines. Blank lines separate blocks. The first block is the header and
every later block is one entry. A line that starts with `#` is a comment and never separates blocks. Indentation and
CRLF line endings are tolerated.

| Key            | Block  | Count              | Meaning                                                                                                                                                                                                     |
|----------------|--------|--------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `namespace`    | header | 1                  | Namespace of the generated type. An empty value means the global namespace.                                                                                                                                 |
| `type`         | header | 1                  | Name of the one `public static partial class` the file emits.                                                                                                                                               |
| `contract`     | header | 1 with entries     | `ce77`: the mandatory, machine-validated CE 7.7 evidence contract below (`CESDK3003` when a file with entries omits it).                                                                                    |
| `provenance`   | header | with `contract`    | A proof status followed by `: ` and an immutable source reference.                                                                                                                                          |
| `minimum-ce`   | header | with `contract`    | Exact four-part minimum CE version, for example `7.7.0.10621`.                                                                                                                                              |
| `architecture` | header | with `contract`    | `x64`; this generator makes no public target-address claim on another architecture.                                                                                                                         |
| `thread`       | header | with `contract`    | `any`, `main`, or `unknown`; `unknown` records absent proof rather than granting thread safety.                                                                                                             |
| `ownership`    | header | with `contract`    | `none`, `borrowed`, or `owned`. Object contracts remain deliberately outside the scalar grammar.                                                                                                            |
| `global`       | entry  | 1                  | The Lua global to call: an ASCII identifier that is not a Lua 5.3 reserved word.                                                                                                                            |
| `method`       | entry  | 1                  | The C# method name. A C# reserved word gets an `@` prefix.                                                                                                                                                  |
| `form`         | entry  | 1                  | `try` returns `bool` and writes `out` results. `outcome` returns `LuaOperationStatus` and writes `out` results. `throwing` returns the value, or `void`, and raises `LuaException` when the Lua call fails. |
| `doc`          | entry  | 1                  | One line of original English, emitted as the XML `<summary>`. Never copy Cheat Engine documentation.                                                                                                        |
| `arg`          | entry  | 0 or more          | `name:kind`, one pushed argument. `arg`, `fixed` and `opt` keep their textual order, which is the push order.                                                                                               |
| `fixed`        | entry  | 0 or more          | `boolean:true` or `boolean:false`, one host-required Lua argument omitted from the C# signature.                                                                                                            |
| `opt`          | entry  | 0 or more, last    | `name:kind`, one `LuaOptional<T>` argument: omitted (not pushed), `Nil` (pushed as `nil`) or a value. Only `opt` may follow (`CESDK3004`).                                                                  |
| `result`       | entry  | `try`: 1 or more   | `name:kind`, one `out` result, in read order. Not allowed in a `throwing` entry.                                                                                                                            |
| `opt-result`   | entry  | 0 or more          | `name:kind`, one `out LuaOptional<T>` result after the `result` values: a position Lua did not return is omitted (`CESDK3005`).                                                                             |
| `rest`         | entry  | `outcome`: 0 or 1  | `name:kind`, the variadic tail `Span<T> name, out int nameCount`, last; kinds `int32`, `int64`, `single`, `double`, `boolean`.                                                                              |
| `return`       | entry  | `throwing`: 0 or 1 | The kind of the returned value. Omit it for `void`. Not allowed in a `try` entry.                                                                                                                           |
| `nil`          | entry  | with `contract`    | `none`, `absence`, `expected-failure`, or `lua-error`: CE result semantics after a protected call succeeds.                                                                                                 |

The kinds are `int32`, `int64`, `single`, `double`, `boolean`, `address`, `utf8`, `string` and `string?`. A `utf8` value
is valid only as an argument, because a span result would dangle once the wrapper restores the stack. `utf8` and
`string?` cannot be optional: `nil` is the `Nil` state of `LuaOptional<T>`, never a `null` string. An entry with
`opt-result` or `rest` calls Lua with `LUA_MULTRET` and reads the factual result count: fewer values than the `result`
entries is `LuaOperationStatusKind.MissingResult` (a `false` Try result), never `nil`. Two entries may
bind the same `global`, for example a `try` and a `throwing` form. They share one cache field. A `method` name used by
more than one entry, a parameter that collides with an emitted local, or an identity that collides with a generated
raw core/cache field is rejected with `CESDK3001` before code generation.

### CE 7.7 evidence contract

Every spec file with entries uses `contract: ce77` (`CESDK3003` otherwise); only a header-only reservation may omit
it. The header carries shared facts; each entry adds `nil`, while the existing `form` and `return` fields remain the
public return contract. The parser copies these facts to every `SpecCallModel`, validates them at the actual field
location, and emits them in XML `<remarks>`. This makes provenance, minimum version, architecture, thread-affinity,
ownership, return form and nil/absence/error semantics inspectable without treating a comment as an API contract.
`thread` stays `unknown` until a CE 7.7 host probe proves an affinity (audit SRC02-02).

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
`bool TryReadInt32(Address address, out int value)`. An optional address is `LuaOptional<Address>` on the public side
and `LuaOptional<nuint>` in the core; the facade keeps its state (omitted, nil or value) inline and allocation-free. The
`outcome` form returns the core's `LuaOperationStatus` after converting its address results. A spec with an `address`
contract compiles only where `CheatEngine.SDK.Engine.Values.Address` exists.

`Specs/memory-scalars.cheatengine-sdk-api.txt` is the one spec currently wired into the Engine build, and it also shows
the `throwing` form. Its entries produce `CheatEngine.SDK.Engine.Generated.MemoryScalars` with `TryReadInt32`,
`WriteInt32`, `TryReadInt64` and `WriteInt64`. `CheatEngine.SDK.Engine.csproj` names its path, so do not rename or move
it. Adding another manifest is an API change: first extend the grammar with a localized diagnostic for every unsupported
shape, then include that one file and add semantic, fixture and live-opt-in coverage appropriate to its contract.

### Contract, public projection and emitted code are checked separately

A formatting change is not a contract change, and a textually stable file can still change the public API (audit
A19-23). Three independent checks therefore exist:

- **Contract**: the parsed spec model (`Parsing/ProductionSpecsTests.cs`, and
  `Reformatting_a_spec_keeps_its_contract_and_call_model`, which reformats a spec and requires an equal contract and
  call model);
- **Public projection**: `libs/CheatEngine.SDK.Engine/PublicAPI.*.txt`, enforced by the build (RS0016/RS0017);
- **Emitted code**: the committed `MemoryScalars` text (`Memory_scalars_output_is_unchanged_by_the_new_grammar`),
  compiled clean and executed against stand-in globals (`EndToEnd/`).

## Promise

| You can rely on                                                                                                                                                                                                                                | Backed by                                                                                                                                          |
|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| The generator never ships.                                                                                                                                                                                                                     | `src/CheatEngine.SDK/CheatEngine.SDK.csproj` packs only the analyzer references it marks `PackAsAnalyzer`, and it does not reference this project. |
| Every invalid spec issue is a localized `CESDK3001` error; an unrelated valid entry still emits.                                                                                                                                               | `Parsing/SpecFileParserTests.cs`, `Generator/DiagnosticsTests.cs`                                                                                  |
| A spec with entries must declare `contract: ce77` (`CESDK3003`); every production spec parses without an issue, cites the pinned `celua.txt` SHA-256, claims no unproven thread affinity, and is wired or reviewed.                            | `Parsing/ProductionSpecsTests.cs`, `Spec_with_entries_without_contract_reports_the_missing_ce77_contract`                                          |
| `opt` arguments are omitted, `nil` or pushed exactly as declared; `opt-result` distinguishes zero results from `nil`; `rest` copies every value or reports the needed capacity; an absent global is `GlobalUnavailable` in the `outcome` form. | `EndToEnd/EngineApiOptionalEndToEndTests.cs`, `Parsing/SpecFileParserTests.cs` (`CESDK3004`/`CESDK3005` rows)                                      |
| The wired `MemoryScalars` output is byte-identical to its committed text; a reformatted spec (spaces, comments, CRLF) keeps the same contract and call model.                                                                                  | `Memory_scalars_output_is_unchanged_by_the_new_grammar`, `Reformatting_a_spec_keeps_its_contract_and_call_model`                                   |
| Every EngineApi diagnostic has a documentation page, a help link and a release-tracking row.                                                                                                                                                   | `Generator/EngineApiDiagnosticCatalogTests.cs`                                                                                                     |
| A ce77 spec carries validated provenance/version/architecture/thread/ownership/return/nil facts on every entry and projects them into XML documentation.                                                                                       | `Parsing/SpecFileParserTests.cs`, `Generator/EmissionTests.cs`                                                                                     |
| A spec file exclusively owns its generated type; duplicate type/member/cache identities are `CESDK3002` errors at each exact field and emit neither file.                                                                                      | `Generator/DiagnosticsTests.cs`                                                                                                                    |
| Emitted code compiles without errors or warnings against the real `CheatEngine.SDK.Annotations`, `CheatEngine.SDK.Lua.Interop` and `CheatEngine.SDK.Lua`.                                                                                      | `Generator/EmissionTests.cs`                                                                                                                       |
| Editing one spec file re-emits only that file. An unrelated compilation edit recomputes nothing.                                                                                                                                               | `Generator/IncrementalityTests.cs`                                                                                                                 |
| No parsed model in the pipeline holds a Roslyn object.                                                                                                                                                                                         | `Pipeline_step_values_hold_no_roslyn_objects` in `Generator/IncrementalityTests.cs`                                                                |
| Wrappers leave the Lua stack as they found it, allocate nothing on the warm success path, and throw `InvalidOperationException` while no runtime is attached.                                                                                  | `EndToEnd/MemoryScalarsEndToEndTests.cs`, run against stand-in Lua globals                                                                         |

## Run the tests

The end-to-end tests carry `Category=NativeLua` and run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md). See [
`tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests`](../../tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests/README.md).

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EngineApi.Tests --filter-trait "Category=NativeLua"
```
