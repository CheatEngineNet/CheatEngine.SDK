# CheatEngine.SDK.SourceGenerators.LuaBindings

Incremental Roslyn source generator that writes the Lua side of a plugin's bindings into the plugin assembly. It ships
in the [`CheatEngine.SDK` package](../../src/CheatEngine.SDK/README.md) under `analyzers/dotnet/cs`.

## Objective

The generator turns each `[LuaFunction("name")]` static method into a function that Lua can call. It turns each
`[LuaGlobal("name")]` static partial method into a typed call of a Cheat Engine Lua global. The emitted code uses only
the public API of [`CheatEngine.SDK.Lua`](../../libs/CheatEngine.SDK.Lua/README.md).

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

In `OnEnable`, call `Bindings.RegisterLuaFunctions(state)` with the state from `LuaRuntime.AcquireState()` and check the
returned `LuaStatus`. `UnregisterLuaFunctions(state)` assigns `nil` to each name.

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
  default values. A leading `LuaState` parameter receives the callback state and is not a Lua argument.
- A `[LuaGlobal]` method is the defining declaration of a `static partial` method without a body. Arguments come first
  and `out` results last. A leading `LuaState` replaces `LuaRuntime.AcquireState()`.
- Two `[LuaFunction]` methods of one type with the same name are both skipped. The check covers one type: two types can
  register the same name, and the later `RegisterLuaFunctions` call wins.
- Several `[LuaGlobal]` methods can bind one global and share one cache.
- Only methods are bound. `[LuaGlobal]` on a property is ignored and raises no diagnostic.
- A `string?` argument rejects `nil` like a `string` argument. A `null` string result becomes `nil`.
- A Try-form `out string` result needs `[MaybeNullWhen(false)]` or `string?`. Without it the compiler reports `CS8601`
  in the generated file.

## How it works

1. Two pipelines find the methods that carry `CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute` or
   `CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute` in the current compilation.
2. Shape rules check each method, its containing types and its Lua name. The analyzers link the same source, so a member
   skipped for its type, name or shape gets [`CESDK2002`](../../analyzers/docs/CESDK2002.md), [
   `CESDK2003`](../../analyzers/docs/CESDK2003.md) or [`CESDK2004`](../../analyzers/docs/CESDK2004.md).
3. Valid members are grouped by containing type. One type yields one file for each kind it declares.
4. The emitters write each file against the `CheatEngine.SDK.Lua` API, with every type name `global::`-qualified. The function
   file disables `CS0612`, `CS0618` and the IDs declared on its targets, so an `[Obsolete]` or `[Experimental]` target
   compiles clean (`LuaFunctionOutputTests`).

Registration takes the address of each thunk, so the generator needs `AllowUnsafeBlocks`. The `CheatEngine.SDK` package sets it
while the property is empty. Without it the generator emits nothing, not even the `[LuaGlobal]` bodies, which need no
unsafe code themselves. Analyzer [`CESDK2001`](../../analyzers/docs/CESDK2001.md) reports the cause.

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
- The generator reports no diagnostics and emits nothing for a member it cannot bind (`LuaFunctionOutputTests`,
  `NoOutputTests`).
- Editing one type re-emits only that type's file, and an unrelated edit re-emits nothing (`IncrementalityTests`).

## Run the tests

Run `dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests`. Tests tagged `Category=NativeLua` load the
Lua DLL of Cheat Engine 7.7 kept in [`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be
installed. See the [test project](../../tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests/README.md).
