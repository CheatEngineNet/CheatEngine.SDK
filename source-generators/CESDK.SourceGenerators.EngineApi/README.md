# CESDK.SourceGenerators.EngineApi

Repository-internal Roslyn generator that turns a curated text spec of Cheat Engine Lua globals into typed C# wrappers
inside `CESDK.Engine`. It never ships in the `CESDK` package.

## Objective

Read every additional file named `*.cesdk-api.txt` and emit one complete wrapper method per spec entry. Each wrapper
calls one Cheat Engine Lua global through the protected call shape that `CESDK.Lua` defines.

## Why it exists

`CESDK.Engine` wraps Cheat Engine's own Lua functions. Every wrapper needs the same body: acquire the state, push, call,
read the results, restore the stack. A spec states each wrapper once in text. The generator writes the body, so all
wrappers share one shape and one set of failure rules.

The generator uses the emitter behind `[LuaGlobal]` (`LuaGlobalCallEmitter`, described in [
`../CESDK.SourceGenerators.Shared/README.md`](../CESDK.SourceGenerators.Shared/README.md)) through the shared assembly,
never through the LuaBindings generator. It cannot reuse the output of
`CESDK.SourceGenerators.LuaBindings`, because source generators do not see each other's output. It therefore writes
complete declarations, never the body half of a partial method.

## How it works

`libs/CESDK.Engine/CESDK.Engine.csproj` loads the generator as an analyzer and passes the spec as an additional file:

```xml
<ProjectReference Include="../../source-generators/CESDK.SourceGenerators.EngineApi/CESDK.SourceGenerators.EngineApi.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false"/>
<AdditionalFiles Include="../../source-generators/CESDK.SourceGenerators.EngineApi/Specs/memory-scalars.cesdk-api.txt"/>
```

1. The generator keeps additional files whose name ends in `.cesdk-api.txt`, ignoring case, and parses each one into a
   model of strings and enums.
2. It drops every entry that breaks a rule. A file left with no valid entry emits nothing, and the generator never
   reports a diagnostic.
3. Each remaining file becomes one generated source that compiles into `CESDK.Engine.dll`.

`EngineApiGenerator`, in namespace `CESDK.SourceGenerators.EngineApi`, is the only public type. The parser uses no
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
| `global`    | entry  | 1                  | The Lua global to call: an ASCII identifier that is not a Lua 5.3 reserved word.                                                           |
| `method`    | entry  | 1                  | The C# method name. A C# reserved word gets an `@` prefix.                                                                                 |
| `form`      | entry  | 1                  | `try` returns `bool` and writes `out` results. `throwing` returns the value, or `void`, and raises `LuaException` when the Lua call fails. |
| `doc`       | entry  | 1                  | One line of original English, emitted as the XML `<summary>`. Never copy Cheat Engine documentation.                                       |
| `arg`       | entry  | 0 or more          | `name:kind`, one pushed argument, in order.                                                                                                |
| `fixed`     | entry  | 0 or more          | `boolean:true` or `boolean:false`, one host-required Lua argument omitted from the C# signature, after all `arg` values.                |
| `result`    | entry  | `try`: 1 or more   | `name:kind`, one `out` result, in read order. Not allowed in a `throwing` entry.                                                           |
| `return`    | entry  | `throwing`: 0 or 1 | The kind of the returned value. Omit it for `void`. Not allowed in a `try` entry.                                                          |

The kinds are `int32`, `int64`, `single`, `double`, `boolean`, `address`, `utf8`, `string` and `string?`. A `utf8` value
is valid only as an argument, because a span result would dangle once the wrapper restores the stack. Two entries may
bind the same `global`, for example a `try` and a `throwing` form. They share one cache field. A `method` name used by
more than one entry drops all of them.

```text
namespace: CESDK.Engine.Generated
type: MemoryScalars

global: readInteger
method: TryReadInt32
form: try
arg: address:address
fixed: boolean:true
result: value:int32
doc: Reads a 32-bit integer from the target process.
```

## What gets generated

One file per spec file: the generated-code header, the namespace block (omitted for the global namespace), one
`public static partial class`, one `private static readonly LuaRef` field per distinct global, then one method per
entry, sorted by method name.

An entry whose only `address` values are arguments yields two methods. A private `__<Method>Raw` core takes `nuint` and
carries the call shape. The public method takes `global::CESDK.Engine.Values.Address` and forwards with
`unchecked((nuint)address.ToUInt64())`. The example above yields the public
`bool TryReadInt32(Address address, out int value)`. Any other entry that uses `address` keeps `nuint`. A spec with an
`address` argument compiles only where `CESDK.Engine.Values.Address` exists.

`Specs/memory-scalars.cesdk-api.txt` is the one spec, and it also shows the `throwing` form. Its entries produce
`CESDK.Engine.Generated.MemoryScalars` with `TryReadInt32`, `WriteInt32`, `TryReadInt64` and `WriteInt64`.
`CESDK.Engine.csproj` names its path, so do not rename or move it.

## Promise

| You can rely on                                                                                                                                               | Backed by                                                                                                                      |
|---------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------|
| The generator never ships.                                                                                                                                    | `src/CESDK/CESDK.csproj` packs only the analyzer references it marks `PackAsAnalyzer`, and it does not reference this project. |
| Invalid input is silent: no diagnostic, no exception, and a bad entry never blocks the valid ones.                                                            | `Parsing/SpecFileParserTests.cs`, `Generator/NoOutputTests.cs`                                                                 |
| Emitted code compiles without errors or warnings against the real `CESDK.Annotations`, `CESDK.Lua.Interop` and `CESDK.Lua`.                                   | `Generator/EmissionTests.cs`                                                                                                   |
| Editing one spec file re-emits only that file. An unrelated compilation edit recomputes nothing.                                                              | `Generator/IncrementalityTests.cs`                                                                                             |
| No parsed model in the pipeline holds a Roslyn object.                                                                                                        | `Pipeline_step_values_hold_no_roslyn_objects` in `Generator/IncrementalityTests.cs`                                            |
| Wrappers leave the Lua stack as they found it, allocate nothing on the warm success path, and throw `InvalidOperationException` while no runtime is attached. | `EndToEnd/MemoryScalarsEndToEndTests.cs`, run against stand-in Lua globals                                                     |

## Run the tests

The end-to-end tests carry `Category=NativeLua` and run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md). See [
`tests/CESDK.SourceGenerators.EngineApi.Tests`](../../tests/CESDK.SourceGenerators.EngineApi.Tests/README.md).

```powershell
dotnet test --project tests/CESDK.SourceGenerators.EngineApi.Tests
dotnet test --project tests/CESDK.SourceGenerators.EngineApi.Tests --filter-trait "Category=NativeLua"
```
