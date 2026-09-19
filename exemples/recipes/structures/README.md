<div align="center">

# Recipe · Structure definitions

**Describe a game's memory layout from C#, add it to Cheat Engine's structure list, and list what is there.**

**Level** `Advanced` · **Time** `30 min` · **Needs** `Guide 06`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                                                     |
|----------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A `Player` structure defined in code, a structure lister and an auto guess helper                                                                                   |
| **You learn**              | Creating an object you own, adding elements, handing ownership over with `Release()`, calling a method that takes arguments                                         |
| **You need**               | The object toolkit from [06 · Value scans](../../06-value-scans/README.md): `CEObject` and `Owned<T>`                                                               |
| **Cheat Engine functions** | `createStructure`, `getStructureCount`, `getStructure`, and the structure methods `addElement`, `addToGlobalStructureList`, `autoGuess`, `beginUpdate`, `endUpdate` |

## Objective

Define a `Player` structure with a health field, a mana field and a three float position, add it to the global structure
list so that Structure Dissect can pick it, and print every global structure with its size.

## Why it matters

A structure definition is knowledge about the game. Writing it as code means the layout is versioned with your plugin
and reappears in every session, instead of living in a table someone forgets to save. The catch is ownership: a
structure you create is yours until you give it away, and one you did not create is never yours to destroy.

## How it works

### 1. Know who owns what

| Object                         | Comes from                    | Owner                                       | What you do                                           |
|--------------------------------|-------------------------------|---------------------------------------------|-------------------------------------------------------|
| A new structure                | `createStructure(name)`       | You, until it joins the global list         | Wrap it in `Owned<CEObject>`. Disposing destroys it   |
| A structure in the global list | `addToGlobalStructureList()`  | Cheat Engine, which saves it with the table | Call `Release()` on your wrapper and never dispose it |
| A listed structure             | `getStructure(index)`         | Cheat Engine                                | Use the plain `CEObject` and never dispose it         |
| An element                     | `addElement()` on a structure | The structure                               | Use the plain `CEObject` and never dispose it         |

`Owned<T>.Dispose` runs `destroy()` and must run on the main thread. `Release()` returns the handle and forgets it, so a
later `Dispose` does nothing. A `using` declaration therefore protects every early `return`, and `Release()` is the one
line that says "this object is not mine anymore".

### 2. Build and list structures

```csharp
using System.Text;
using CESDK.Annotations.Lua;
using CESDK.Engine.Enums;
using CESDK.Engine.Objects;
using CESDK.Lua.Marshalling;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace StructureKit;

internal static partial class StructureCalls
{
    [LuaGlobal("getStructureCount")]
    public static partial bool TryGetCount(out int count);
}

internal static partial class Structures
{
    [LuaFunction("my_plugin_define_player")]
    public static bool DefinePlayer()
    {
        var L = LuaRuntime.AcquireState();
        CEObject handle;
        using (LuaFrame frame = new(L))
        {
            if (!L.TryGetGlobal("createStructure"u8).IsOk) return false;
            StringMarshaller.Push(L, "Player");
            if (!L.TryCall(1, 1).IsOk || !CEObject.TryRead(L, -1, out handle)) return false;
        }

        using Owned<CEObject> owned = new(handle);
        var player = owned.Value;

        player.TryCallMethod("beginUpdate"u8);
        var built = TryAddElement(player, "Health", 0, VariableType.Dword)
                    && TryAddElement(player, "Mana", 4, VariableType.Dword)
                    && TryAddElement(player, "PositionX", 8, VariableType.Single)
                    && TryAddElement(player, "PositionY", 12, VariableType.Single)
                    && TryAddElement(player, "PositionZ", 16, VariableType.Single);
        player.TryCallMethod("endUpdate"u8);

        if (!built || !player.TryCallMethod("addToGlobalStructureList"u8)) return false;

        _ = owned.Release();
        return true;
    }

    [LuaFunction("my_plugin_structures")]
    public static string List()
    {
        if (!StructureCalls.TryGetCount(out var count)) return "unavailable";
        if (count == 0) return "no structures";

        var L = LuaRuntime.AcquireState();
        StringBuilder text = new();
        for (var i = 0; i < count; i++)
        {
            using LuaFrame frame = new(L);
            if (!L.TryGetGlobal("getStructure"u8).IsOk) break;

            Int32Marshaller.Push(L, i);
            if (!L.TryCall(1, 1).IsOk || !CEObject.TryRead(L, -1, out var structure)) continue;

            if (structure.TryGetProperty<StringMarshaller, string>("Name"u8, out var name)
                && structure.TryGetProperty<Int32Marshaller, int>("Count"u8, out var elements)
                && structure.TryGetProperty<Int32Marshaller, int>("Size"u8, out var size))
                text.Append(name).Append(": ").Append(elements).Append(" element(s), ").Append(size).AppendLine(" bytes");
        }

        return text.ToString();
    }

    [LuaFunction("my_plugin_guess")]
    public static bool Guess(string structureName, nuint baseAddress, int size)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!L.TryGetGlobal("getStructure"u8).IsOk) return false;

        StringMarshaller.Push(L, structureName);
        if (!L.TryCall(1, 1).IsOk || !CEObject.TryRead(L, -1, out var structure)) return false;
        if (!structure.TryPushMethod(L, "autoGuess"u8).IsOk) return false;

        AddressMarshaller.Push(L, baseAddress);
        Int32Marshaller.Push(L, 0);
        Int32Marshaller.Push(L, size);
        return L.TryCall(3, 0).IsOk;
    }

    private static bool TryAddElement(CEObject structure, string name, int offset, VariableType type)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!structure.TryPushMethod(L, "addElement"u8).IsOk) return false;
        if (!L.TryCall(0, 1).IsOk || !CEObject.TryRead(L, -1, out var element)) return false;

        return element.TrySetProperty<StringMarshaller, string>("Name"u8, name)
               && element.TrySetProperty<Int32Marshaller, int>("Offset"u8, offset)
               && element.TrySetProperty<EnumMarshaller<VariableType>, VariableType>("Vartype"u8, type);
    }
}
```

`DefinePlayer` has three exits and each one is safe. A failed element leaves `built` false, so the `using` destroys the
half made structure. A failed `addToGlobalStructureList` does the same. Only the success path reaches `Release()`.

The `Vartype` property of an element is a number, so `EnumMarshaller<VariableType>` pushes `VariableType.Dword` as its
Cheat Engine value. A property that Cheat Engine publishes through its type information, such as `MemoryRecord.VarType`,
uses the text name instead (see [07 · Address list](../../07-address-list/README.md)).

`Guess` shows the recipe for a method with arguments: `TryPushMethod` leaves a function bound to the object, you push
the arguments, and `TryCall` runs it. The bound function is called without `self`.

### 3. Use it from Lua

```lua
print(my_plugin_define_player())                              -- true
print(my_plugin_structures())                                 -- one line per structure, such as: Player: 5 element(s), 20 bytes
print(my_plugin_guess("Player", getAddress("game.exe+2F0000"), 24))
```

## Good to know

- **The lister counts from zero.** It asks for index 0 up to the last one, like every object index in `CESDK.Engine`.
  `getStructure("Player")` looks a structure up by name, and the name is case sensitive.
- **Batch your edits.** `beginUpdate` and `endUpdate` bracket several element changes, so Cheat Engine refreshes once.
- **An element can point at another structure.** The `ChildStruct` property links an element to a structure it points
  to, which is how a `Player` gets a `Weapon`. Set it with `TrySetProperty` and a handle from `getStructure`.
- **Other element properties.** `DisplayMethod` takes `dtUnsignedInteger`, `dtSignedInteger` or `dtHexadecimal` as
  text, and `Bytesize` is writable for strings and byte arrays.
- **Dispose before `OnDisable` returns.** An `Owned<T>` that outlives the enabled plugin cannot reach Cheat Engine and
  leaks the object. Every path in the recipe finishes inside the call.

## Promise

- A failed step never leaves a half made structure behind: the owning wrapper destroys it.
- A structure that reached the global list is never destroyed by your code, because `Release()` gives up ownership.
- A Lua error inside an object call never becomes an exception: the typed members return `false`.
- The Lua stack returns to its previous height on every path, because each function holds a `LuaFrame`.

## Before you move on

- [ ] `my_plugin_define_player()` returns `true`, and Structure Dissect offers `Player`.
- [ ] `my_plugin_structures()` lists `Player` with five elements.
- [ ] You can say, for each object in your own code, who destroys it.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
