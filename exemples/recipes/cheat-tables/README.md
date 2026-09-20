<div align="center">

# Recipe · Cheat tables

**Save the open cheat table before the plugin goes away, and bring it back on the next enable.**

**Level** `Intermediate` · **Time** `20 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                 |
|----------------------------|---------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A profile keeper: `my_plugin_save_profile`, `my_plugin_load_profile` and an autosave that follows the plugin lifecycle          |
| **You learn**              | Binding `loadTable` and `saveTable`, the difference between replacing and merging, and validating file names that come from Lua |
| **You need**               | [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md)                                                            |
| **Cheat Engine functions** | `loadTable`, `saveTable`, `getAddressList`                                                                                      |

## Objective

Keep cheat tables as named profiles on disk: one per game, saved on demand, autosaved when the plugin is disabled, and
restored when the plugin is enabled again on an empty table.

## Why it matters

A cheat table is the work of an hour, and a Cheat Engine session can end at any moment. A plugin that owns the save and
load points turns "I lost my table" into a Lua call. The recipe also shows the two places where table code goes wrong:
a load that silently replaces what is open, and a file name taken straight from a script.

## How it works

### 1. Bind the table functions

```csharp
using CheatEngine.SDK.Annotations.Lua;

namespace ProfileRecipe;

internal static partial class TableCalls
{
    [LuaGlobal("loadTable")]
    public static partial void LoadTable(string fileName);

    [LuaGlobal("loadTable")]
    public static partial void LoadTable(string fileName, bool merge);

    [LuaGlobal("saveTable")]
    public static partial void SaveTable(string fileName);
}
```

Both functions take a file name and optional flags. The overloads share one binding per function, and the throwing form
reports a failed Cheat Engine call as a `LuaException`.

### 2. The profile store and the plugin

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace ProfileRecipe;

[CheatEnginePlugin("Profile Keeper")]
public sealed class ProfileKeeperPlugin : CheatEnginePlugin
{
    protected override void OnEnable()
    {
        var status = Profiles.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");

        if (Profiles.Store.Exists(Profiles.AutoSaveName) && Profiles.TableIsEmpty())
            Profiles.Store.Load(Profiles.AutoSaveName, merge: false);
    }

    protected override void OnDisable()
    {
        if (!Profiles.TableIsEmpty()) Profiles.Store.Save(Profiles.AutoSaveName);
        Profiles.UnregisterLuaFunctions(LuaRuntime.AcquireState());
    }
}

internal sealed class ProfileStore(string root)
{
    public bool TryGetPath(string name, out string path)
    {
        path = string.Empty;
        if (!IsValidName(name)) return false;

        path = Path.Combine(root, name + ".CT");
        return true;
    }

    public bool Exists(string name) => TryGetPath(name, out var path) && File.Exists(path);

    public bool Save(string name)
    {
        if (!TryGetPath(name, out var path)) return false;

        try
        {
            Directory.CreateDirectory(root);
            TableCalls.SaveTable(path);
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is LuaException or IOException or UnauthorizedAccessException)
        {
            HostLog.Write(HostLogLevel.Error, $"Saving the profile '{name}' failed.", exception);
            return false;
        }
    }

    public bool Load(string name, bool merge)
    {
        if (!TryGetPath(name, out var path) || !File.Exists(path)) return false;

        try
        {
            TableCalls.LoadTable(path, merge);
            return true;
        }
        catch (LuaException exception)
        {
            HostLog.Write(HostLogLevel.Error, $"Loading the profile '{name}' failed.", exception);
            return false;
        }
    }

    private static bool IsValidName(string name)
    {
        if (name.Length is not (> 0 and <= 64) || !char.IsLetterOrDigit(name[0])) return false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c is not ('_' or '-' or '.' or ' ')) return false;
        }

        return true;
    }
}

internal static partial class Profiles
{
    public const string AutoSaveName = "autosave";

    public static ProfileStore Store { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProfileKeeper", "profiles"));

    [LuaFunction("my_plugin_save_profile")]
    public static bool SaveProfile(string name) => Store.Save(name);

    [LuaFunction("my_plugin_load_profile")]
    public static bool LoadProfile(string name, bool merge) => Store.Load(name, merge);

    public static bool TableIsEmpty()
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        return L.TryExecute("return getAddressList().Count"u8, 1).IsOk && L.TryReadInteger(-1, out var count) && count == 0;
    }
}
```

Profiles live in `%APPDATA%\ProfileKeeper\profiles\<name>.CT`. `Save` checks that the file exists afterward instead of
trusting the call, and `Load` refuses a profile that is not on disk.

The name check is the important line. A Lua script decides the name, and `Path.Combine` would happily follow `..\..` out
of the profiles folder. `IsValidName` accepts a letter or digit first, then letters, digits, `_`, `-`, `.` and space, up
to 64 characters, so no separator, drive letter or parent reference gets through.

### 3. Try it

```lua
print(my_plugin_save_profile("game.exe"))            -- true: saved as game.exe.CT
print(my_plugin_load_profile("game.exe", false))     -- true: the open table is replaced by the profile
print(my_plugin_load_profile("shared-hacks", true))  -- true: the profile is added to the open table
print(my_plugin_load_profile("missing", false))      -- false: there is no such file
print(my_plugin_save_profile("../evil"))             -- false: a name cannot leave the profiles folder
```

## Replace or merge

`loadTable(fileName, merge)` decides what happens to the table that is open.

| `merge` | Result                                                | Use it for                                     |
|---------|-------------------------------------------------------|------------------------------------------------|
| `false` | The open table is cleared, then the profile is loaded | Restoring an exact state, such as the autosave |
| `true`  | The profile is added to the open table                | Layering a shared set of entries over your own |

The autosave loads with `merge: false` and only when the table is empty, so it can never discard work. It also saves
only a table that has entries, so an empty session never overwrites a good autosave. Merging keeps everything that is
already in the table, so reserve `merge: true` for an explicit call.

## Good to know

- **`getAddressList().Count` is the emptiness test.** Cheat Engine documents `Count` on the address list as the number
  of records in the table, and `TableIsEmpty` reads it with one Lua expression. See
  [07 · Address list](../../07-address-list/README.md) for working with the records themselves.
- **Trainer files are a save option.** `saveTable` has a `protect` flag that applies to `.CETRAINER` file names. This
  recipe writes plain `.CT` tables and does not use it.
- **Save from `OnDisable`, not after it.** `OnDisable` runs while the Lua runtime is attached. Once it returns, Cheat
  Engine is out of reach, so a late save has nothing to call.
- **Pick the name from the game.** The recipe passes the profile name from Lua. A script can use the process name, such
  as `game.exe`, to get one profile per game.

## Promise

- A profile name that could leave the profiles folder is rejected before any file operation.
- A failed save or load is logged through `HostLog` and returns `false`. It never throws into Cheat Engine.
- The autosave never replaces an open table and never overwrites a profile with an empty table.
- The Lua stack returns to its previous height after every call.

## Before you move on

- [ ] `my_plugin_save_profile("game.exe")` creates `game.exe.CT` in `%APPDATA%\ProfileKeeper\profiles`.
- [ ] Unticking and ticking the plugin on an empty table brings the autosave back.
- [ ] `my_plugin_save_profile("../evil")` returns `false`.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
