# CESDK.SourceGenerators.EntryPoint.Tests

Tests for the entry-point generator, which emits `CESDK.CESDK.CEPluginInitialize`, and for the shared helpers in [
`CESDK.SourceGenerators.Shared`](../../source-generators/CESDK.SourceGenerators.Shared/README.md).

## Objective

Prove that the generator emits the exact bootstrap Cheat Engine looks up when a plugin assembly holds one valid plugin,
and nothing otherwise.

## Why it exists

Cheat Engine finds a plugin only through the type `CESDK.CESDK` and the method `CEPluginInitialize`. A wrong or missing
entry point fails inside the host. So the emitted text is compared, compiled, loaded and called here. See
the [generator README](../../source-generators/CESDK.SourceGenerators.EntryPoint/README.md).

## How it works

| Suite           | What it proves                                                                                                          |
|-----------------|-------------------------------------------------------------------------------------------------------------------------|
| Output          | The text equals a hand-written expectation, is UTF-8 with LF endings and holds no unsafe code                           |
| Plugin shapes   | Namespaces, nesting, partial types, keywords and non-ASCII names resolve, and zero, two or invalid plugins emit nothing |
| Escaping        | Display names become correct `u8` literals, checked as text, as a clean compile and as run-time bytes                   |
| Compile and run | The output compiles clean on C# 11 to 14, loads, and answers `(IntPtr, int) -> int`                                     |
| Incrementality  | Edits that cannot change the output recompute nothing, and edits that can reach the source output                       |
| Shared code     | `BuildProperty`, `CSharpLiteral`, `EquatableArray`, `GeneratedCodeText`, `SourceWriter` and `TrackingNames`             |

Test inputs compile against `ContractStubs`, an assembly that mirrors the `CESDK.Hosting` contract by hand and adds
instrumentation the real `PluginHost` does not have. Change it with the emitter and `ExpectedBootstrap` when the
contract changes. `RealAssemblyCompilationTests` alone uses the real `CESDK.Annotations` and `CESDK.Hosting`. It catches
only drift that breaks compilation. "Compiles clean" means no warning or error at warning level 9999 with nullable and
documentation diagnostics on.

Framework references come from the local .NET installation, never from NuGet. `DefaultVerifierTests` also runs the
standard Roslyn harness with a global analyzer config. `KnownLimitationTests` pins an extern-aliased SDK and a C# 10
consumer, which fail inside the generated file. No test needs Cheat Engine or a Lua DLL. `LocalFrameworkReferencesTests`
skips its targeting-pack case without a .NET targeting pack, and the shared-code tests need no compilation, so a run
never has zero tests.

## Run the tests

```powershell
dotnet test --project tests/CESDK.SourceGenerators.EntryPoint.Tests
dotnet test --project tests/CESDK.SourceGenerators.EntryPoint.Tests --filter-class "CESDK.SourceGenerators.EntryPoint.Tests.Generator.IncrementalityTests"
```

## Promise

- One valid plugin yields exactly the expected bootstrap, and its type and method names equal
  `CESDK.Abi.Managed.ManagedEntryPoint` (`NominalOutputTests`).
- Zero plugins, two plugins and every invalid shape yield no file and no diagnostic (`NoOutputTests`,
  `BootstrapModelTests`).
- The entry point returns 0 instead of throwing when the host or the plugin constructor throws
  (`BootstrapExecutionTests`).
- Unchanged input recomputes nothing (`IncrementalityTests`).
