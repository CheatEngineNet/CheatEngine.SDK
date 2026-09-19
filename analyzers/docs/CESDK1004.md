# CESDK1004: Exception can escape an [UnmanagedCallersOnly] method

|                    |                                                                          |
|--------------------|--------------------------------------------------------------------------|
| Category           | `CESDK.Usage`                                                            |
| Default severity   | Warning                                                                  |
| Enabled by default | Yes                                                                      |
| Code fix           | Yes: wrap body in try/catch returning a failure value (supports Fix All) |
| Reported           | While typing and in build                                                |

In short: put the whole body of the method in one `try` statement, catch `Exception`, and return a failure value from
the `catch` block. The code fix does exactly that.

## Cause

A method or local function marked `[System.Runtime.InteropServices.UnmanagedCallersOnly]` has a body that is not
entirely guarded by a catch-all.

## Why

Such a method is called directly by native code: Cheat Engine for the plugin callbacks, the Lua VM for a
`lua_CFunction`. There is no managed frame above it that could handle an exception. An exception that leaves the method
is an unhandled exception on a native thread, and the runtime terminates the process, which is Cheat Engine with the
user's session in it. SDK rule: no managed exception ever crosses a native boundary. Every native-to-managed entry
converts failures into a return value.

The SDK's generators emit this guard themselves. The rule is for the entries you write by hand.

## The exact definition

The rule is structural, so that you can tell by looking whether code passes. A body is **guarded** when every top-level
statement is one of these:

1. A **guard try**, defined below.
2. A local variable declaration with no initializer or with trivially non-throwing initializers.
3. `return;` or `return <trivially non-throwing value>;`.
4. A local function declaration, because declaring it executes nothing.
5. An empty statement.
6. A nested block `{ ... }`, `unsafe { ... }`, `checked { ... }` or `unchecked { ... }` whose statements all satisfy
   this list.

A guard try is a `try` statement that meets three conditions:

| Condition                  | Detail                                                                                                                                                                                                                                                                                                                                                   |
|----------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| A catch-all clause         | `catch { }`, `catch (System.Exception)` or `catch (System.Exception e)`, without a `when` filter                                                                                                                                                                                                                                                         |
| No throw in a handler      | No `catch` block and no `finally` block contains a `throw` (statement or expression, `throw;` included), lambdas and local functions inside those blocks included                                                                                                                                                                                        |
| No call that always throws | No `catch` or `finally` block calls a method marked `[DoesNotReturn]`, the attribute with which a method says "I always throw" (`ExceptionDispatchInfo.Capture(e).Throw()`, `ExceptionDispatchInfo.Throw(e)`, throw helpers). The members of `System.Environment` are exempt: `FailFast` and `Exit` end the process, so nothing unwinds into native code |

An expression body `=> e` is read as `{ return e; }` (as `{ e; }` for `void`). So `=> 0` passes and `=> Work()` is
reported.

**Trivially non-throwing values** are a closed list:

| Value                                                                     | Accepted                                                                                                                                                                                                                                                                                                                                                     |
|---------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Compile-time constants, `default`, a local variable, a parameter          | Literals, `const`, `nameof`, enum members                                                                                                                                                                                                                                                                                                                    |
| A static field of a core-library type such as `nint`, `nuint` or `string` | `IntPtr.Zero`, `nint.Zero`, `UIntPtr.Zero`, `string.Empty`. Any other static field can run a type initializer, which can throw.                                                                                                                                                                                                                              |
| A conversion over such values                                             | Identity; the typing of a `default` or `null` literal; an implicit numeric, reference or pointer conversion; wrapping into a nullable type (`int` to `int?`, `int` to `long?`, `int?` to `long?`, `Guid` to `Guid?`); outside a `checked` context, an explicit conversion among primitives, enums and pointers (`(int)wide`, `(int)status`, `(nint)pointer`) |
| A built-in unary operator over such a value                               | `-`, `+`, `~`, `!` on a primitive or an enum, outside a `checked` context                                                                                                                                                                                                                                                                                    |
| A conditional expression                                                  | `c ? a : b` whose three operands are trivially non-throwing                                                                                                                                                                                                                                                                                                  |

Everything else is not on the list, because it runs code, allocates or can fail. That covers user-defined conversions
and operators, every conversion from `dynamic` (the runtime binder throws `RuntimeBinderException`), tuple conversions,
span conversions, boxing, unboxing, casts between reference types, `T?` to `T`, anything in a `checked` context, and
everything that involves `decimal` (its operators and conversions throw `OverflowException` whatever the context). A
project built with `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` is a `checked` context everywhere:
write `unchecked(...)` where the value is meant to wrap.

## Verdicts at a glance

| Code                                                                                              | Verdict             | Reason                                                                                                           |
|---------------------------------------------------------------------------------------------------|---------------------|------------------------------------------------------------------------------------------------------------------|
| `try { ... } catch (Exception) { return 0; }`                                                     | guarded             | the canonical shape                                                                                              |
| `try { ... } catch { }`                                                                           | guarded             | bare catch is a catch-all                                                                                        |
| `int r = -1; try { r = Work(); } catch (Exception) { } return r;`                                 | guarded             | declaration and return cannot throw                                                                              |
| `try { ... } catch (IOException) { return 1; } catch (Exception) { return 0; }`                   | guarded             | extra clauses are fine as long as none throws                                                                    |
| a lambda or local function that throws, used inside the `try` block                               | guarded             | whatever it throws is caught                                                                                     |
| `try { ... } finally { ... }`                                                                     | reported            | nothing is caught                                                                                                |
| `try { ... } catch (InvalidOperationException) { ... }`                                           | reported            | not a catch-all                                                                                                  |
| `catch (Exception) { Log(); throw; }`                                                             | reported            | rethrows                                                                                                         |
| `catch (Exception e) { ExceptionDispatchInfo.Capture(e).Throw(); return 0; }`                     | reported            | a rethrow by another name                                                                                        |
| `catch (Exception e) { Environment.FailFast(e.Message); return 0; }`                              | guarded             | the process ends there and no exception reaches native code                                                      |
| `try { ... } catch (Exception) { return 0; } finally { Cleanup(); }`                              | guarded, not proven | see Limits                                                                                                       |
| `catch (Exception e) when (Filter(e))`, even `when (true)`                                        | reported            | a filtered clause is not a catch-all, and the filter runs user code                                              |
| `Prepare(); try { ... } catch (Exception) { ... }`                                                | reported            | `Prepare()` runs outside the guard                                                                               |
| `... return (int)wide;`, `return -r;`, `return (int)status;` after the try                        | guarded             | unchecked built-in conversions and operators on primitives and enums                                             |
| `... return checked((int)wide);`, `return (int)money;` (`decimal`), `return (int)maybe;` (`int?`) | reported            | can throw `OverflowException` or `InvalidOperationException`                                                     |
| `object o = state;` in front of the try                                                           | reported            | boxing allocates: move it into the `try`                                                                         |
| `lock (gate) { try { ... } catch { ... } }`, and the same with `using` or `fixed`                 | reported            | acquiring the lock, the resource or the pinned pointer runs outside the guard: move the wrapper inside the `try` |
| `=> Work()`                                                                                       | reported            | unguarded call                                                                                                   |

## Limits

The rule does not prove that the calls inside a `catch` block or a `finally` block cannot throw. It finds the explicit
ways of throwing listed above and nothing else. A logger that throws inside the catch block still escapes, and so does a
cleanup call that throws inside `finally`, which runs after the catch-all and outside its protection. In the other
direction, the rule flags a `throw` in a catch block even if that `throw` sits in a lambda that is never invoked. Keep
the catch blocks of native entries trivial: record the failure and return the failure value. Keep `finally` blocks
trivial too, or move the cleanup into the `try` block, or into a nested `try`/`finally` inside it, where the catch-all
covers it.

Scope:

- Methods and local functions with the attribute, block or expression bodied. A local function is analyzed on its own.
- Generated code is not analyzed.
- The analyzer only runs in projects where a CESDK contract type (`CESDK.Annotations.Plugin.CheatEnginePluginAttribute`
  or `CESDK.Hosting.Plugin.CheatEnginePlugin`) can be resolved.

## Example

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class Callbacks
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnLuaCall(nint luaState)          // CESDK1004
    {
        return DoWork(luaState);
    }

    private static int DoWork(nint luaState) => 1;
}
```

## Code fix

"Wrap body in try/catch returning a failure value" moves the whole body into a `try` and adds a catch-all whose return
value depends on the return type:

| Return type                                                                           | Failure value | Meaning for the native caller                                                     |
|---------------------------------------------------------------------------------------|---------------|-----------------------------------------------------------------------------------|
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` | `0`           | `FALSE` for Cheat Engine's `BOOL` callbacks; "zero results" for a `lua_CFunction` |
| `bool`                                                                                | `false`       | Failure                                                                           |
| `void`                                                                                | nothing       | the catch block gets a comment instead of being left empty                        |
| anything else (`nint`, `nuint`, `char`, pointers, function pointers, enums, structs)  | `default`     | null pointer or handle, zero value                                                |

```csharp
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class Callbacks
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnLuaCall(nint luaState)
    {
        try
        {
            return DoWork(luaState);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int DoWork(nint luaState) => 1;
}
```

The exception type is written `Exception` where `using System;` is in scope and `System.Exception` elsewhere. An
existing `try` without a catch-all is wrapped as a whole rather than edited. Comments travel with the statements,
preprocessor directives inside a block body too.

An expression body becomes a block body: `=> e` turns into `return e;` (`e;` for `void`), and `=> throw ...` into a
throw statement. Comments between the signature and the expression (`=> // why`, or on lines of their own) move in front
of the statement, one per line. A comment after the semicolon stays on the statement line. The fix is not offered when a
preprocessor directive sits inside the expression body (`=>` followed by `#if` branches): the branches are halves of one
expression and the matching `#endif` lies outside the declaration, so there is no mechanical block form. Convert such a
body by hand.

The catch block is where your logging belongs. The fix does not invent a logging call. Check that the failure value is
what the native contract of that particular callback expects: for a callback where `0` means success, change it.

## When to suppress

When the method provably cannot throw but does not have the accepted shape, for example a body that only copies
blittable fields through pointers. Prefer reshaping over suppressing: the `try` region costs nothing on the non-throwing
path.
