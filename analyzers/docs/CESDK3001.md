# CESDK3001: Engine API specification is invalid

|                    |                             |
|--------------------|-----------------------------|
| Category           | `CheatEngine.SDK.EngineApi` |
| Default severity   | Error                       |
| Enabled by default | Yes                         |
| Code fix           | No                          |
| Reported           | In build, by the generator  |

## Cause

A curated spec file (`*.cheatengine-sdk-api.txt`) has a malformed line, an unknown or duplicated key, an invalid
namespace, type, Lua global or C# method name, an unknown kind, a missing required key, or a value outside the
`contract: ce77` vocabulary (provenance status, minimum version, architecture, thread, ownership, `nil`).

## Why

A spec states the contract of every generated wrapper. A malformed entry cannot be generated, and silently skipping it
would remove an API from `CheatEngine.SDK.Engine` without anyone noticing. The generator drops only the invalid entry
(or the file, for a broken header) and reports why; a valid sibling entry still generates.

## What is checked

The grammar in [`source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/README.md`](../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/README.md#spec-file-format):
`key: value` lines, blocks separated by blank lines, the header keys, the entry keys and their counts, the value kinds,
reserved generated locals, and generated-member collisions inside one file.

## How to fix

Correct the key or value at the reported line and column. The message names the key, value or rule.

## When to suppress

Never: the diagnostic means an entry generates nothing.

## Who sees it

Only this repository: the EngineApi generator does not ship in the `CheatEngine.SDK` package. It runs while
`CheatEngine.SDK.Engine` builds and reads the spec files that project passes as `AdditionalFiles`. The diagnostic is
located on the spec file (line and column of the offending key or value), not on C# code.
