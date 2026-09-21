# CheatEngine.SDK.SourceGenerators.LuaBindings

Incremental Roslyn source generator that writes the Lua side of a plugin's bindings into the plugin assembly. It ships
in the [`CheatEngine.SDK` package](../../src/CheatEngine.SDK/README.md) under `analyzers/dotnet/cs`.

## Objective

The generator turns each `[LuaFunction("name")]` static method into a function that Lua can call. It turns each
`[LuaGlobal("name")]` static partial method into a typed call of a Cheat Engine Lua global, and each
`[LuaClass("name")] readonly partial struct` into a borrowed `CEObject` handle with generated
`[LuaMethod]`/`[LuaProperty]` members. The emitted code uses only the public APIs of
[`CheatEngine.SDK.Lua`](../../libs/CheatEngine.SDK.Lua/README.md) and `CheatEngine.SDK.Engine.Objects`.

## Why it exists

A function that Lua calls must be an unmanaged `cdecl` function that checks its arguments, catches every exception and
reports failure through Lua's error channel. Calling a Lua global needs a protected call and exact stack discipline.
Written by hand, every binding repeats that code, and every exit path can go wrong.

## Use

Declare the bindings in a `partial` type:

```csharp
using CheatEngine.SDK.Annotations.Lua;

namespace MyTrainer;

internal static partial class Bindings
{
    [LuaFunction("add")] // Lua: add(2, 3) returns 5
    public static long Add(long a, long b) => a + b;

    [LuaGlobal("readInteger")]
    private static partial bool TryReadInt32Raw(nuint address, bool signed, out int value);

    // Cheat Engine's readInteger defaults to unsigned; keep the signed flag out of this public helper.
    internal static bool TryReadInt32(nuint address, out int value) => TryReadInt32Raw(address, true, out value);

    [LuaGlobal("readInteger")]
    private static partial int ReadInt32Raw(nuint address, bool signed);

    internal static int ReadInt32(nuint address) => ReadInt32Raw(address, true);
}
```

In `OnEnable`, acquire one operation lease, then call the ownership-aware generated registration method and retain its
lease for the matching disable path:

```csharp
using var operation = LuaRuntime.AcquireOperation();
var registration = Bindings.TryRegisterLuaFunctions(operation.State);
if (!registration.IsSuccess)
{
    _ = registration.Lease?.ReleaseWithOutcome(operation.State);
    throw new InvalidOperationException(registration.Kind.ToString());
}
_bindingsLease = registration.Lease;
```

The default collision policy rejects an already effective global before publication. `ReplaceExisting` is explicit; its
lease retains the prior value and restores it only while the installed closure still owns that global. A release after
detach or state reset is stale and does not touch a replacement registry. Protected lookup/set metamethod side effects
remain outside the transaction's rollback guarantee.

`RegisterLuaFunctions` and `UnregisterLuaFunctions` remain generated legacy APIs for source compatibility. They retain
their historical unconditional assignment behavior; new code should use `TryRegisterLuaFunctions` and dispose the
returned lease instead.

For this type the build adds two files. `MyTrainer.Bindings.LuaFunctions.g.cs` holds the registration pair and one thunk
per function. `MyTrainer.Bindings.LuaGlobals.g.cs` holds one cached `LuaRef` per global and the method bodies.

## Supported shapes

| Lua type | C# types               | Allowed as                             |
|----------|------------------------|----------------------------------------|
| integer  | `int`, `long`, `nuint` | arguments and results                  |
| number   | `float`, `double`      | arguments and results                  |
| boolean  | `bool`                 | arguments and results                  |
| string   | `string`, `string?`    | arguments and results                  |
| string   | `ReadOnlySpan<byte>`   | arguments, and `[LuaFunction]` results |

A `[LuaGlobal]` method has one of two forms. A Try form returns `bool` and ends with `out` results, or with
`Span<byte> destination, out int written`. It returns `false` and defaults the results when the global is missing,
raises or returns the wrong kind. A throwing form returns `void` or one value and throws `LuaException` in those cases.
A `bool` return without `out` results is a throwing form that reads a Lua boolean.

Cheat Engine-specific optional arguments remain ordinary binding parameters. For example, request a signed
`readInteger` result through a private generated raw binding and pass `true` from a public helper, as above. That keeps
the public `int` API from receiving an unsigned value that cannot represent negative 32-bit results.

- The containing type, and every type around it, is `partial`, non-generic, a class, struct or record, and not `file`
  -local.
- A Lua name starts with an ASCII letter or `_`, continues with letters, digits or `_`, and is not a Lua 5.3 reserved
  word.
- A `[LuaFunction]` method is `static`, non-generic and not `async`. Its parameters are by value, without `params` or
  default values. A leading `LuaState` parameter receives the callback state and is not a Lua argument. Its type must
  be the `CheatEngine.SDK.Lua.State.LuaState` symbol from the referenced SDK runtime; a consumer-defined same-name
  type does not qualify.
- A `[LuaGlobal]` method is the defining declaration of a `static partial` method without a body. Arguments come first
  and `out` results last. A leading `LuaState` is admitted through `LuaRuntime.AcquireOperation(state)` rather than
  bypassing the lifecycle gate.
- A `[LuaClass]` declaration is a non-generic `readonly partial struct`, including partial enclosing types. It is a
  borrowed handle only: the generated type implements `ICEObject<T>` and `ILuaMarshaller<T>` around `CEObject`.
  `Owned<T>` is the sole representation of plugin ownership.
- `[LuaMethod]` is an instance partial method on a valid Lua class handle. It uses the same scalar arguments/results as
  a global wrapper, except that a `LuaState` parameter is not accepted; the generated body snapshots the stack, pushes
  the bound CE method under protection, and restores the original top in `finally`.
- `[LuaProperty]` is a bodyless partial `get` and/or `set` property on a valid Lua class handle, with one scalar type.
  Its accessors use `CEObject.TryGetProperty`/`TrySetProperty`, whose operations are protected and stack-balanced.
- Two `[LuaFunction]` methods of one type with the same name are both skipped. The check covers one type: two types can
  request the same name. The new lease API rejects that collision by default; the legacy `RegisterLuaFunctions` method
  retains its historical later-write behavior.
- Several `[LuaGlobal]` methods can bind one global and share one cache.
- `[LuaGlobal]` targets methods only. Lua global variables require a separate future contract.
- A `string?` argument rejects `nil` like a `string` argument. A `null` string result becomes `nil`.
- A Try-form `out string` result needs `[MaybeNullWhen(false)]` or `string?`. Without it the compiler reports `CS8601`
  in the generated file.

## How it works

1. Four pipelines find SDK-identity `LuaFunction`, `LuaGlobal`, `LuaClass`, `LuaMethod` and `LuaProperty` annotations in
   the current compilation. A look-alike type with the same full name in a consumer source file or another assembly is
   not a binding contract.
2. Shape rules check each declaration, its containing types and its Lua name. The analyzers link the same source, so a
   member skipped for its type, name or shape gets the localized CESDK2xxx diagnostic.
3. Valid members are grouped by containing type. One type yields one file for each kind it declares.
4. The emitters write each file against the `CheatEngine.SDK.Lua` API, with every type name `global::`-qualified. The
   function
   file disables `CS0612`, `CS0618` and the IDs declared on its targets, so an `[Obsolete]` or `[Experimental]` target
   compiles clean (`LuaFunctionOutputTests`).

Registration takes the address of each thunk, so only `[LuaFunction]` needs `AllowUnsafeBlocks`; set
`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in a project that declares one. `[LuaGlobal]`, `[LuaClass]`,
`[LuaMethod]` and `[LuaProperty]` generate without unsafe code and remain valid with it set to `false`. Analyzer
[`CESDK2001`](../../analyzers/docs/CESDK2001.md) reports a function-export project that has not enabled it.

## Promise

- A call with the wrong number or kind of arguments raises a Lua error that names the function or the argument
  (`LuaFunctionEndToEndTests`).
- An exception thrown by a `[LuaFunction]` target becomes a Lua error, and none reaches native code
  (`LuaFunctionEndToEndTests`).
- The Lua stack returns to its previous height after every call (`LuaFunctionEndToEndTests`, `LuaGlobalEndToEndTests`).
- A warm Try-form call, a warm copy-out call and a thunk called from a Lua loop allocate nothing
  (`LuaGlobalEndToEndTests`, `LuaFunctionEndToEndTests`).
- The generated files compile without errors or warnings with C# 14, nullable on and documentation diagnostics on,
  except the `out string` case above (`LuaFunctionOutputTests`, `LuaGlobalOutputTests`).
- The generator emits no conflicting source for a member it cannot bind, while the linked analyzer reports the
  localized contract diagnostic and healthy siblings continue to generate (`LuaObjectOutputTests`, `NoOutputTests`).
- Editing one type re-emits only that type's file, and an unrelated edit re-emits nothing (`IncrementalityTests`).

## Run the tests

Run `dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests`. Tests tagged `Category=NativeLua`
load the
Lua DLL of Cheat Engine 7.7 kept in [`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be
installed. See the [test project](../../tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests/README.md).
