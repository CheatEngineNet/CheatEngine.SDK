# CESDK3005: Engine API optional or variadic result is invalid

|                    |                             |
|--------------------|-----------------------------|
| Category           | `CheatEngine.SDK.EngineApi` |
| Default severity   | Error                       |
| Enabled by default | Yes                         |
| Code fix           | No                          |
| Reported           | In build, by the generator  |

## Cause

An entry declares a `result:` after an `opt-result:`, anything after a `rest:`, a `rest:` on a form other than
`outcome`, an `opt-result:` of kind `string?`, or a `rest:` of a kind other than `int32`, `int64`, `single`, `double`
or `boolean`.

## Why

An entry with `opt-result:` or `rest:` calls Lua with `LUA_MULTRET` and reads the factual number of values. Required
results come first (fewer values than them is `MissingResult`, never `nil`), then optional results (a position Lua did
not return is omitted), then at most one variadic tail. Only the `outcome` form, which returns `LuaOperationStatus`,
can report `ResultCapacityExceeded`, `NilResult` or `InvalidResult` for the tail.

## What is checked

The order `result:`, `opt-result:`, `rest:`; `rest:` only with `form: outcome` and only once; the kinds above. A
`rest: values:int64` entry generates `Span<long> values, out int valuesCount`.

## How to fix

Reorder the results, or use `form: outcome` for a `rest:` result.

## When to suppress

Never: the diagnostic means the entry generates nothing.

## Who sees it

Only this repository: the EngineApi generator does not ship in the `CheatEngine.SDK` package. It runs while
`CheatEngine.SDK.Engine` builds and reads the spec files that project passes as `AdditionalFiles`. The diagnostic is
located on the spec file (line and column of the offending key or value), not on C# code.
