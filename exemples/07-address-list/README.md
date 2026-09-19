<div align="center">

# 07 · The address list

**Turn found addresses into cheat table records: add, group, find, freeze and read them from C#.**

**Level** `Intermediate` · **Time** `30 min` · **Needs** `Guide 06`

[Examples index](../README.md) · [Previous: Value scans](../06-value-scans/README.md) · [Next: Running Lua](../08-running-lua/README.md)

</div>

---

|                            |                                                                                                                                                                                                                                      |
|----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A `TableEditor` for Cheat Engine's address list, and a plugin that builds a "Player" group with Health, Mana and Gold                                                                                                                |
| **You learn**              | Borrowed handles, setting properties from C#, enum names as text, calling methods with arguments, and the main thread rule                                                                                                           |
| **You need**               | [06 · Value scans](../06-value-scans/README.md) for the object call pattern                                                                                                                                                          |
| **Cheat Engine functions** | `getAddressList`, and the members `createMemoryRecord`, `getMemoryRecordByDescription`, `Count` and its indexer on the list, `Description`, `Address`, `VarType`, `Value`, `Active`, `IsGroupHeader` and `appendToEntry` on a record |

## Objective

Put records into the cheat table that the user sees and saves. You create a record, describe it, nest it in a group,
freeze it and read its value, all from C#.

## Why it matters

The address list is where a scan turns into something reusable. A plugin that finds the gold counter (guide 06) can add
it to the table in the same call. The address list is also Cheat Engine's own user interface object, so the rules about
who owns an object and which thread may touch it decide whether the plugin is stable.

## How it works

### 1. Know who owns what

Guides 05 and 06 created objects, so they owned them and disposed them. Here Cheat Engine owns everything. The
`[CEOwned]` attribute names that intent: it marks a return value, a property or a parameter as an object that Cheat
Engine owns and you do not dispose. In your code it is a plain `CEObject` handle with no `Owned<T>` around it.

| Object                  | Created by                                   | Owner        | Do you dispose it                          |
|-------------------------|----------------------------------------------|--------------|--------------------------------------------|
| The address list        | `getAddressList()`                           | Cheat Engine | No                                         |
| A record                | `createMemoryRecord()` on the list           | The list     | No                                         |
| A scan or a result list | `createMemScan()` and `createFoundList(...)` | You          | Yes, see [06](../06-value-scans/README.md) |

A `CEObject` is only the native pointer. It has no `Dispose` and no destroy member, so a borrowed handle cannot free
what Cheat Engine still uses.

### 2. Write the editor

```csharp
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace TableTools;

internal static class TableEditor
{
    public static int Count
    {
        get
        {
            var L = LuaRuntime.AcquireState();
            using LuaFrame frame = new(L);
            return TryGetList(L, out var list) && list.TryGetProperty<Int32Marshaller, int>("Count"u8, out var count)
                ? count
                : 0;
        }
    }

    public static bool TryAddRecord(string description, string address, VariableType type, out CEObject record)
    {
        record = default;
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!TryGetList(L, out var list)) return false;
        if (!list.TryCallMethod(L, "createMemoryRecord"u8, 0, 1).IsOk) return false;
        if (!CEObject.TryRead(L, -1, out var created)) return false;

        if (!created.TrySetProperty<StringMarshaller, string>("Description"u8, description)
            || !created.TrySetProperty<StringMarshaller, string>("Address"u8, address)
            || !created.TrySetProperty<Utf8Marshaller, ReadOnlySpan<byte>>("VarType"u8, type.ToCEName()))
        {
            return false;
        }

        record = created;
        return true;
    }

    public static bool TryAddGroup(string description, out CEObject group)
    {
        group = default;
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!TryGetList(L, out var list)) return false;
        if (!list.TryCallMethod(L, "createMemoryRecord"u8, 0, 1).IsOk) return false;
        if (!CEObject.TryRead(L, -1, out var created)) return false;
        if (!created.TrySetProperty<StringMarshaller, string>("Description"u8, description)) return false;

        // A plain record still works as a parent when Cheat Engine refuses the group flag.
        _ = created.TrySetProperty<BooleanMarshaller, bool>("IsGroupHeader"u8, true);

        group = created;
        return true;
    }

    public static bool TryAppend(CEObject child, CEObject parent)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        parent.Push(L);
        return child.TryCallMethod(L, "appendToEntry"u8, 1, 0).IsOk;
    }

    public static bool TryFind(string description, out CEObject record)
    {
        record = default;
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!TryGetList(L, out var list)) return false;
        StringMarshaller.Push(L, description);
        return list.TryCallMethod(L, "getMemoryRecordByDescription"u8, 1, 1).IsOk
               && CEObject.TryRead(L, -1, out record);
    }

    public static bool TryFreeze(string description, bool frozen)
    {
        return TryFind(description, out var record)
               && record.TrySetProperty<BooleanMarshaller, bool>("Active"u8, frozen);
    }

    public static bool TryReadValue(string description, [MaybeNullWhen(false)] out string value)
    {
        value = null;
        return TryFind(description, out var record)
               && record.TryGetProperty<StringMarshaller, string>("Value"u8, out value);
    }

    public static List<string> Describe()
    {
        List<string> lines = [];
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!TryGetList(L, out var list)) return lines;
        if (!list.TryGetProperty<Int32Marshaller, int>("Count"u8, out var count)) return lines;

        for (var i = 0; i < count; i++)
        {
            using LuaFrame item = new(L);
            if (!list.TryGetIndex(L, i).IsOk || !CEObject.TryRead(L, -1, out var record)) continue;
            if (!record.TryGetProperty<StringMarshaller, string>("Description"u8, out var description)) continue;

            lines.Add(record.TryGetProperty<StringMarshaller, string>("Value"u8, out var value)
                ? $"{description}={value}"
                : description);
        }

        return lines;
    }

    private static bool TryGetList(LuaState L, out CEObject list)
    {
        list = default;
        return L.TryGetGlobal("getAddressList"u8).IsOk
               && L.TryCall(0, 1).IsOk
               && CEObject.TryRead(L, -1, out list);
    }
}
```

The table below lists the record members the editor uses and how each one travels.

| Member                  | Kind     | Type    | How the editor sets or reads it                                                          |
|-------------------------|----------|---------|------------------------------------------------------------------------------------------|
| `Description`           | Property | Text    | `TrySetProperty<StringMarshaller, string>`                                               |
| `Address`               | Property | Text    | An expression Cheat Engine interprets, such as `game.exe+2A0` or `game.exe+2A0+4`        |
| `VarType`               | Property | Text    | The name from `CEEnumNames.ToCEName`, such as `vtDword`, pushed through `Utf8Marshaller` |
| `Value`                 | Property | Text    | `TryGetProperty<StringMarshaller, string>`. The value in string form                     |
| `Active`                | Property | Boolean | `true` activates the record, which freezes a value record                                |
| `IsGroupHeader`         | Property | Boolean | Turns a record into a group header                                                       |
| `appendToEntry(parent)` | Method   | Record  | Pushes the parent, then `TryCallMethod(L, name, 1, 0)`                                   |
| `[index]` on the list   | Indexer  | Record  | `TryGetIndex(L, i)` with Cheat Engine's own zero based index                             |

### 3. Export it

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

namespace TableTools;

[CheatEnginePlugin("Table Tools")]
public sealed class TableToolsPlugin : CheatEnginePlugin
{
    protected override void OnEnable()
    {
        var status = TableFunctions.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");
    }

    protected override void OnDisable() => TableFunctions.UnregisterLuaFunctions(LuaRuntime.AcquireState());
}

internal static partial class TableFunctions
{
    [LuaFunction("my_plugin_add_record")]
    public static bool AddRecord(string description, string address, ReadOnlySpan<byte> typeName)
    {
        return CEEnumNames.TryParseCEName(typeName, out VariableType type)
               && TableEditor.TryAddRecord(description, address, type, out _);
    }

    [LuaFunction("my_plugin_build_player_group")]
    public static bool BuildPlayerGroup(string baseAddress)
    {
        if (!TableEditor.TryAddGroup("Player", out var group)) return false;

        (string Name, string Offset, VariableType Type)[] fields =
        [
            ("Health", "+0", VariableType.Dword),
            ("Mana", "+4", VariableType.Dword),
            ("Gold", "+8", VariableType.Dword),
        ];

        foreach (var (name, offset, type) in fields)
        {
            if (!TableEditor.TryAddRecord(name, baseAddress + offset, type, out var record)) return false;
            if (!TableEditor.TryAppend(record, group)) return false;
        }

        return true;
    }

    [LuaFunction("my_plugin_freeze")]
    public static bool Freeze(string description, bool frozen) => TableEditor.TryFreeze(description, frozen);

    [LuaFunction("my_plugin_record_value")]
    public static string? RecordValue(string description)
    {
        return TableEditor.TryReadValue(description, out var value) ? value : null;
    }

    [LuaFunction("my_plugin_record_count")]
    public static long RecordCount() => TableEditor.Count;

    [LuaFunction("my_plugin_list_records")]
    public static string ListRecords() => string.Join("; ", TableEditor.Describe());
}
```

`my_plugin_add_record` takes the type as text, so a Lua caller writes the name Cheat Engine already uses. Reading it
as a `ReadOnlySpan<byte>` means the name is parsed straight from the string Lua holds, and an unknown name returns
`false`.

### 4. Call it from Lua

```lua
print(my_plugin_build_player_group("game.exe+2A0"))
print(my_plugin_freeze("Health", true))
print(my_plugin_record_value("Health"))
print(my_plugin_add_record("Gold found", "00007FF6A1DC4F10", "vtDword"))
print(my_plugin_list_records())
```

| Lua call                                                            | Result                                                                          |
|---------------------------------------------------------------------|---------------------------------------------------------------------------------|
| `my_plugin_build_player_group("game.exe+2A0")`                      | `true` after a "Player" group with Health, Mana and Gold appears in the table   |
| `my_plugin_freeze("Health", true)`                                  | `true` when the record exists. Use `false` as the second argument to release it |
| `my_plugin_record_value("Health")`                                  | The value as text, or `nil` when no record has that description                 |
| `my_plugin_add_record("Gold found", "00007FF6A1DC4F10", "vtDword")` | `true`. An unknown type name such as `"vtBogus"` gives `false`                  |
| `my_plugin_record_count()`                                          | The number of records in the list                                               |
| `my_plugin_list_records()`                                          | Every record as `Description=Value`, joined with `; `                           |

The last step of the gold counter story from guide 06 is one call:
`my_plugin_add_record("Gold", my_plugin_scan_result(0), "vtDword")`.

## The main thread rule

The address list is a window control, and Cheat Engine's objects are not thread safe. `OnEnable`, `OnDisable` and a call
from the Lua Engine window already run on Cheat Engine's main thread, so the editor needs no extra code there. Code on
your own thread reaches the list through `MainThread.Invoke`, which runs the work on the main thread and returns its
result:

```csharp
using CheatEngine.SDK.Hosting.Threading;

namespace TableTools;

internal static class BackgroundTable
{
    public static Task<int> CountAsync() => Task.Run(() => MainThread.Invoke(static _ => TableEditor.Count, 0));
}
```

Keep a check and the operation it protects inside one `Invoke`, because another thread can change the table between
two separate calls. [09 · The main thread](../09-main-thread/README.md) covers the rules in full.

## Good to know

> [!TIP]
> Look a record up when you need it instead of storing its handle. The user can delete a record at any time, and
> `TryFind` returns `false` for a record that is gone.

- **Typed members never throw for a Lua failure.** `TrySetProperty`, `TryGetProperty` and the stack level `Try` members
  return `false` or a `LuaStatus`. Check the result, because a record created before a failed property stays in the
  table.
- **A group is a record.** `TryAddGroup` creates a record, flags it as a group header and returns it, and
  `appendToEntry` moves the children under it.
- **Saving and loading tables** with `saveTable` and `loadTable` is a separate topic. See
  [the cheat table recipe](../recipes/cheat-tables/README.md).

## Promise

- A typed property or method call returns `false` instead of throwing, and keeps no Lua error message.
- Members that push an object throw `InvalidOperationException` before the plugin is enabled.
- The Lua stack returns to its previous height after every editor call.
- Nothing here creates an object you must dispose, so nothing here can leak one.

## Before you move on

- [ ] `my_plugin_build_player_group` adds a group whose children appear under it in the Cheat Engine window.
- [ ] Freezing and releasing `Health` changes the record's checkbox and nothing else.
- [ ] Every call that runs on your own thread goes through `MainThread.Invoke`.

---

<div align="center">

[Examples index](../README.md) · **Next:** [08 · Running Lua](../08-running-lua/README.md)

</div>
