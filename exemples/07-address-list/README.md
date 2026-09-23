<div align="center">

# 07 · The address list

**Read and create the typed, borrowed records in Cheat Engine's visible address list.**

**Level** `Intermediate` · **Time** `20 min` · **Needs** `Guide 06`

[Examples index](../README.md) · [Previous: Value scans](../06-value-scans/README.md) · [Next: Running Lua](../08-running-lua/README.md)

</div>

---

|                          |                                                                                                                                            |
|--------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**            | A small typed editor that creates a record, gives it a description and selects an existing record                                          |
| **You learn**            | `AddressList`, `MemoryRecord`, borrowed ownership, zero-based indexes, partial side effects, and the GUI-thread boundary                   |
| **You need**             | [06 · Value scans](../06-value-scans/README.md) for scan ownership and [09 · The main thread](../09-main-thread/README.md) for dispatching |
| **Cheat Engine surface** | `getAddressList`, `Addresslist.getCount`, `getMemoryRecord`, `createMemoryRecord`, and the documented `MemoryRecord` properties/methods    |

## Objective

Use the dedicated address-list surface instead of open-coding Lua object names. You obtain Cheat Engine's current list,
create a record attached to it, set the safe supported properties, and read or select records by their zero-based
position.

## What is owned

`AddressListAccess.TryGetCurrent` returns the GUI address list that Cheat Engine owns. A record returned from that
list —
including one that `TryCreateMemoryRecord` has just added — is also owned by Cheat Engine through the list. Both types
are `readonly` borrowed handles. They have no `Dispose`, are never wrapped in `Owned<T>`, and can become invalid if the
user or Cheat Engine changes the table.

| Value                         | How it is obtained                                    | Owner                           | What the SDK lets you do                            |
|-------------------------------|-------------------------------------------------------|---------------------------------|-----------------------------------------------------|
| `AddressList`                 | `AddressListAccess.TryGetCurrent`                     | Cheat Engine's GUI              | Read it and operate on it while it remains current  |
| `MemoryRecord` from list APIs | `TryGet…`, `TryCreateMemoryRecord`, child/parent APIs | Its address list / Cheat Engine | Read and set supported values; never dispose it     |
| `Owned<T>`                    | An API with an explicit ownership-transfer contract   | Your plugin                     | Dispose it deterministically on the required thread |

`[CEOwned]` makes this borrowed contract visible on APIs that return or accept a CE-owned value. The analyzer's
`CESDK1003` rule catches a direct `Dispose` or `DisposeAsync` on an explicitly marked borrowed value. It is deliberately
not an ownership inference engine: do not use the absence of a diagnostic as permission to destroy a CE object.

## Create and inspect a record

```csharp
using CheatEngine.SDK.Engine.AddressLists;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace TableTools;

internal readonly record struct CreateRequest(
    string Description,
    string AddressExpression,
    VariableType VariableType);

internal static class TableEditor
{
    public static bool TryAddRecord(
        string description,
        string addressExpression,
        VariableType variableType,
        out MemoryRecord record)
    {
        record = default;

        if (!AddressListAccess.TryGetCurrent(out var addressList)
            || !addressList.TryCreateMemoryRecord(out record))
        {
            return false;
        }

        // The record is already part of the CE list. These writes are not transactional:
        // a later false leaves the created record in the list with the earlier changes.
        return record.TrySetDescription(description)
               && record.TrySetAddressExpression(addressExpression)
               && record.TrySetVariableType(variableType);
    }

    public static bool TryDescribeAt(int zeroBasedIndex, out string? description, out Address address)
    {
        description = null;
        address = default;

        return AddressListAccess.TryGetCurrent(out var addressList)
               && addressList.TryGetMemoryRecord(zeroBasedIndex, out var record)
               && record.TryGetDescription(out description)
               && record.TryGetCurrentAddress(out address);
    }

    public static bool TrySelectAt(int zeroBasedIndex)
    {
        return AddressListAccess.TryGetCurrent(out var addressList)
               && addressList.TryGetMemoryRecord(zeroBasedIndex, out var record)
               && addressList.TrySetSelectedRecord(record);
    }
}
```

The typed façade preserves the CE vocabulary internally while making the public shape explicit:

| Type                | Supported operation                                                                                    | Failure shape                                                                 |
|---------------------|--------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------|
| `AddressListAccess` | `TryGetCurrent(out AddressList)`                                                                       | `false` for a missing/invalid object or protected Lua failure                 |
| `AddressList`       | `TryGetCount`, get a record by index or id, get/set selected record, create a record                   | `false`; a negative index throws `ArgumentOutOfRangeException`                |
| `MemoryRecord`      | read ID/index/description/address expression/value/variable type/current address; read parent or child | `false` for `nil`, a wrong Lua kind, or a protected Lua failure               |
| `MemoryRecord`      | set description, address expression, value, or variable type                                           | `false` for a protected Lua failure; null text throws `ArgumentNullException` |

The `Address` property is an expression such as a symbol or hexadecimal text. `TryGetCurrentAddress` is different: it
returns the resolved `Address` value for the target process. Keep those two meanings separate.

## Indexing and partial effects

The public index passed to `TryGetMemoryRecord`, `TryGetChild`, and `TryGetIndex` is zero based. Lua tables are normally
one based, but the SDK keeps the conversion at the binding boundary so plugins do not add one themselves.

Creating a record is an observable CE GUI operation. If setting a subsequent field fails, the SDK does not guess how to
delete or roll back that record: the destruction/rollback behavior of address-list records is not part of this safe
slice. Make the intended values valid before creation, report a partial result to the user, and reserve deletion,
grouping, freeze/activation, hierarchy mutation, and arbitrary `MemoryRecord` members for a later capability with an
explicit lifetime and rollback contract.

## Thread and lifetime boundary

The address list is a Cheat Engine GUI object. Its annotations require a plugin-enabled, main-thread call, but GUI
affinity for the exact CE 7.7 host is currently an inferred constraint, not a completed live `synchronize` probe. Call
this API directly only from an already main-thread context such as synchronous plugin lifecycle code. A worker can use
the bounded synchronous dispatcher:

```csharp
using CheatEngine.SDK.Hosting.Threading;

var created = MainThread.Invoke(
    static request => TableEditor.TryAddRecord(
        request.Description,
        request.AddressExpression,
        request.VariableType,
        out _),
    new CreateRequest("Health", "game.exe+1F4A0", VariableType.Dword));
```

That dispatcher verifies, in its callback thunk, that the host runs work on the captured enable thread. It refuses a
host that violates that check and it does not expose a fire-and-forget `queue(function, ...)` route. The CE 7.7 live
probe still has to establish the host's real `synchronize` scheduling, errors, returns, and re-entrance behavior; see
[09 · The main thread](../09-main-thread/README.md). Treat a disabled or stopping plugin as an expected failure
boundary, not as an opportunity to retain a borrowed record.

## Evidence and scope

This slice is sourced from the exact CE `7.7.0.10621` x64 `celua.txt` fixture: `getAddressList`, the `Addresslist`
class, and the
`MemoryRecord` members used above. Its protected-call tests exercise the managed boundary; they do not prove a live CE
GUI thread contract. The classic plugin callback type 0 record is a separate ABI concern and is not this object API.

## Before you move on

- [ ] Keep `AddressList` and `MemoryRecord` as borrowed values; never manufacture ownership with `Owned<T>`.
- [ ] Keep the record creation and its intended property writes together, and handle a partial result honestly.
- [ ] Use zero-based indexes.
- [ ] Keep GUI-object work on the enable thread or dispatch through `MainThread.Invoke`; do not rely on a worker-thread
  Lua call.

---

<div align="center">

[Examples index](../README.md) · **Next:** [08 · Running Lua](../08-running-lua/README.md)

</div>
