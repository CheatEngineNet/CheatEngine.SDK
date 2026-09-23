# CESDK3002: Engine API specification has a generated-identity conflict

|                    |                             |
|--------------------|-----------------------------|
| Category           | `CheatEngine.SDK.EngineApi` |
| Default severity   | Error                       |
| Enabled by default | Yes                         |
| Code fix           | No                          |
| Reported           | In build, by the generator  |

## Cause

Two spec files declare the same generated type, or would generate the same wrapper member or `LuaRef` cache field.

## Why

A spec file exclusively owns its generated type. Two files contributing to one type would depend on partial-type
ordering and could collide silently. Every participating file is reported at the exact field, and none of the
conflicting files is generated.

## What is checked

The `namespace` + `type` identity across all spec files of one compilation, and the wrapper and cache-field identities
of their entries.

## How to fix

Move the entries into one spec file, or give one file another `type`.

## When to suppress

Never: the diagnostic means the conflicting files generate nothing.

## Who sees it

Only this repository: the EngineApi generator does not ship in the `CheatEngine.SDK` package. It runs while
`CheatEngine.SDK.Engine` builds and reads the spec files that project passes as `AdditionalFiles`. The diagnostic is
located on the spec file (line and column of the offending key or value), not on C# code.
