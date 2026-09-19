# CheatEngine.SDK.Engine

The typed Cheat Engine object model for CheatEngine.SDK plugins: object handles, explicit ownership, addresses,
zero-based indices and Cheat Engine's numeric constants.

## Objective

Give plugin code a small, typed vocabulary for the objects and values that Cheat Engine's Lua API hands out. A plugin
reads and writes them without hand-written stack code.

## Why it exists

Cheat Engine exposes its objects as Lua userdata and its values by Lua conventions. An address is a Lua integer or
hexadecimal text. An object counts from zero, but a table returned by a function counts from one. The plugin destroys
the objects it created and never the ones Cheat Engine owns. Each rule is easy to break, and a mistake shows up inside
Cheat Engine. This library encodes each rule once, in a type.

## How it works

| Namespace                          | Types                                         | Role                                                                                                  |
|------------------------------------|-----------------------------------------------|-------------------------------------------------------------------------------------------------------|
| `CheatEngine.SDK.Engine.Objects`   | `CEObject`, `ICEObject<TSelf>`                | Borrowed handle: the native object pointer, equal by identity, with property, index and method access |
| `CheatEngine.SDK.Engine.Objects`   | `Owned<T>`                                    | Ownership of an object the plugin created; `Dispose` destroys it                                      |
| `CheatEngine.SDK.Engine.Values`    | `Address`                                     | An address read from a Lua integer or hexadecimal text; its own Lua marshaller                        |
| `CheatEngine.SDK.Engine.Values`    | `IndexBase`, `LuaSequence`                    | Zero-based indices over Cheat Engine objects and Lua sequences                                        |
| `CheatEngine.SDK.Engine.Enums`     | Enums, `CEEnumNames`, `EnumMarshaller<TEnum>` | Numeric constants, their Cheat Engine names, and Lua integer marshalling                              |
| `CheatEngine.SDK.Engine.Generated` | `MemoryScalars`                               | Generated wrappers for `readInteger`, `writeInteger`, `readQword` and `writeQword`                    |

A `CEObject` is the native object pointer and nothing else. `CEObject.TryRead` decodes it from a full userdata whose
first pointer-sized field holds the pointer, and `Push` hands it back through the host. A property is `obj.Name`. A
method is `obj.name(args)`, bound to the instance and called without `self`. An element is `obj[i]` with Cheat Engine's
own index. Every access runs under a protected Lua call. `ICEObject<TSelf>` is the shape `Owned<T>` needs from a handle
type: `Handle` and a static `FromHandle`.

The typed members (`TryGetProperty<TMarshaller, TValue>`, `TrySetProperty<TMarshaller, TValue>`, `TryCallMethod` and
`TryCallMethod<TMarshaller, TResult>`) acquire the calling thread's state, restore the stack and return `bool`. They
return `false` for a nil or wrong-kind value and keep no Lua error message. The stack-level members (`TryGetProperty`,
`TrySetProperty`, `TryGetIndex`, `TrySetIndex`, `TryPushMethod` and `TryCallMethod` with a `LuaState`) return a
`LuaStatus` and leave either their results or one error value.

`CEObject` is a `readonly struct` with no `Dispose` and no public destroy member, so destruction belongs to `Owned<T>`,
never to a flag. Construct one only from a handle that a create call just returned or that another owner released.
`Dispose` calls `destroy()` and must run on the main thread, and Debug builds assert the thread. There is no finalizer.
Dispose every `Owned<T>` before `OnDisable` returns: afterwards `Dispose` cannot reach Cheat Engine and the object
leaks. `Release()` gives up ownership without destroying.

`Address.TryRead` tries hexadecimal text first (optional `0x`, no sign, no decimal form, 64-bit overflow refused). It
then reads a Lua integer by bit reinterpretation, so an address above `long.MaxValue` round trips. `ToString()` gives
uppercase hexadecimal, eight digits when the value fits 32 bits and sixteen otherwise. Every public index is zero-based,
and the only `+ 1` lives in `IndexBase.ToLuaKey`, which the `LuaSequence` extensions on `LuaState` use.

The enums are `VariableType`, `ScanOption`, `RoundingType`, `FastScanMethod`, `BreakpointMethod`, `BreakpointTrigger`,
`ContinueMethod`, `MemoryProtection` (a `uint` flags enum) and `DuplicateHandling`. `CEEnumNames.ToCEName` returns a
member's Cheat Engine identifier such as `vtDword` as UTF-8, or an empty span for an undefined value or a flag
combination. `TryParseCEName` reverses it case-sensitively and also accepts the `vtUnicodeString` alias. Properties that
Cheat Engine publishes through RTTI, such as `MemoryRecord.VarType`, read and write these names as text.
`EnumMarshaller<TEnum>` does not check that a value is a defined member, because Cheat Engine can return combinations.

The repository-internal `CheatEngine.SDK.SourceGenerators.EngineApi` generates `MemoryScalars` at compile time from
`source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/memory-scalars.cheatengine-sdk-api.txt`. All four
methods take an `Address`. `TryReadInt32` and `TryReadInt64` return `false` when the read fails. `WriteInt32` and
`WriteInt64` return the flag Cheat Engine reports and throw `LuaException` when the Lua call fails.

```csharp
using CheatEngine.SDK.Engine.Generated;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

internal static class EngineDemo
{
    // Both methods need an enabled plugin. ListIsEmpty must also run on the main thread.
    internal static bool ListIsEmpty()
    {
        LuaState L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!L.TryExecute("return createStringlist()"u8, 1).IsOk) return false;
        if (!CEObject.TryRead(L, -1, out CEObject handle)) return false;

        using Owned<CEObject> list = new(handle);
        list.Value.TryCallMethod("clear"u8);
        return list.Value.TryGetProperty<Int32Marshaller, int>("Count"u8, out int count) && count == 0;
    }

    internal static bool TryBump(Address address)
    {
        return MemoryScalars.TryReadInt32(address, out int value) && MemoryScalars.WriteInt32(address, value + 1);
    }
}
```

The library references [`CheatEngine.SDK.Lua`](../CheatEngine.SDK.Lua/README.md), `CheatEngine.SDK.Lua.Interop` and
`CheatEngine.SDK.Annotations`, and not [`CheatEngine.SDK.Hosting`](../CheatEngine.SDK.Hosting/README.md). It reaches
Cheat Engine through `LuaRuntime`, which `CheatEngine.SDK.Hosting` attaches when the plugin is enabled. The assembly
ships in the `CheatEngine.SDK` package under `lib/net10.0`.

## Promise

The tests in `tests/CheatEngine.SDK.Engine.Tests` drive a simulated Cheat Engine object model on a real Lua 5.3 state.

1. `Owned<T>` destroys its object once, never retries a destroy that raised, and never throws from `Dispose`
   (`OwnedTests`).
2. A Lua error never becomes an exception: typed members return `false`, stack-level members return a `LuaStatus` with
   one error value (`CEObjectTests`).
3. Members that push the object throw `InvalidOperationException` before the plugin is enabled (`CEObjectValueTests`).
4. A typed operation acquires the state once and pushes the object once, on success and on failure
   (`HostCallCountTests`).
5. Warm property, method and address operations allocate nothing (`ZeroAllocationTests`, `AddressLuaTests`). `Owned<T>`
   creation, `Address.ToString`, `LuaError.FromStack` and a `StringMarshaller` read allocate.
6. A string that is not hexadecimal is never read as an address through Lua's number coercion (`AddressLuaTests`).
7. A negative index passed to `IndexBase.ToLuaKey` or a `LuaSequence` extension throws `ArgumentOutOfRangeException`
   before anything is pushed (`IndexBaseTests`, `LuaSequenceTests`).
8. Enum values equal the numbers in Cheat Engine 7.7's `defines.lua`, and every name parses back (`EnumValueTests`,
   `CEEnumNamesTests`).
9. `CheatEngine.SDK.Engine.dll` and its XML documentation ship in the package, and the EngineApi generator never does
   (`PackageContentsTests`). Warnings are errors, so every public member is documented.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Engine.Tests
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CheatEngine.SDK.Engine.Tests/README.md).
