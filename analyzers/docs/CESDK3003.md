# CESDK3003: Engine API specification does not declare the ce77 contract

|                    |                             |
|--------------------|-----------------------------|
| Category           | `CheatEngine.SDK.EngineApi` |
| Default severity   | Error                       |
| Enabled by default | Yes                         |
| Code fix           | No                          |
| Reported           | In build, by the generator  |

## Cause

A spec file declares at least one entry, but its header has no `contract: ce77` key.

## Why

Every generated wrapper carries machine-validated Cheat Engine 7.7 evidence: provenance (a proof status and the pinned
`celua.txt` SHA-256), minimum CE version, architecture, thread affinity (`unknown` unless proven), ownership, and a
`nil` contract per entry. A wrapper without that evidence would present an unqualified contract as a reviewed one
(audit F10, AX06-18). Header-only reservation files stay readable without the contract, because they generate nothing.

## What is checked

A file with one or more entries must have `contract: ce77` in its header; the other contract keys and the per-entry
`nil` key are then required and validated (CESDK3001). The file generates nothing until the contract is present.

## How to fix

```text
namespace: CheatEngine.SDK.Engine.Generated
type: Example
contract: ce77
provenance: ExactInstalledFile: CE 7.7.0.10621 celua.txt, SHA-256 <64 hex digits>
minimum-ce: 7.7.0.10621
architecture: x64
thread: unknown
ownership: none

global: getCEVersion
method: GetCheatEngineVersion
form: throwing
return: double
nil: none
doc: Gets the version value.
```

## When to suppress

Never.

## Who sees it

Only this repository: the EngineApi generator does not ship in the `CheatEngine.SDK` package. It runs while
`CheatEngine.SDK.Engine` builds and reads the spec files that project passes as `AdditionalFiles`. The diagnostic is
located on the spec file (line and column of the offending key or value), not on C# code.
