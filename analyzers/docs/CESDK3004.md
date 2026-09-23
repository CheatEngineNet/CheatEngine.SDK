# CESDK3004: Engine API optional argument is invalid

|                    |                             |
|--------------------|-----------------------------|
| Category           | `CheatEngine.SDK.EngineApi` |
| Default severity   | Error                       |
| Enabled by default | Yes                         |
| Code fix           | No                          |
| Reported           | In build, by the generator  |

## Cause

An entry declares an `opt:` argument followed by an `arg:` or `fixed:` argument, or an `opt:` argument of kind `utf8`
or `string?`.

## Why

An `opt:` argument becomes a `LuaOptional<T>` parameter: omitted (not pushed), `Nil` (pushed as `nil`) or a value.
Cheat Engine functions can behave differently for an omitted argument and an explicit `nil`, so the generated wrapper
never pushes `nil` in place of an omitted argument. Lua arguments are positional, so only a trailing run of arguments
can be omitted. `LuaOptional<T>` is an ordinary struct, so a `utf8` span cannot be optional, and `nil` is the `Nil`
state rather than a `null` string.

## What is checked

`arg:`, `fixed:` and `opt:` keep their textual order, which is the push order. After the first `opt:`, only `opt:`
may follow. An `opt:` kind is `int32`, `int64`, `single`, `double`, `boolean`, `address` or `string`; an optional
`address` is exposed as `LuaOptional<Address>` and converted by the facade without losing its state.

## How to fix

Move the required and fixed arguments before the optional ones, or use `string` for optional text.

## When to suppress

Never: the diagnostic means the entry generates nothing.

## Who sees it

Only this repository: the EngineApi generator does not ship in the `CheatEngine.SDK` package. It runs while
`CheatEngine.SDK.Engine` builds and reads the spec files that project passes as `AdditionalFiles`. The diagnostic is
located on the spec file (line and column of the offending key or value), not on C# code.
