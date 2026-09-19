<div align="center">

# Recipe · Symbols

**Give addresses friendly names, resolve them later, and wait for a module that has not loaded yet.**

**Level** `Intermediate` · **Time** `20 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                                                          |
|----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A symbol book that registers `Player.Health` and friends, and a watcher that waits for `game.exe`                                                                        |
| **You learn**              | The symbol functions, cleaning up on disable, and polling from a worker thread                                                                                           |
| **You need**               | [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md) and the thread rules of [09 · The main thread](../../09-main-thread/README.md)                      |
| **Cheat Engine functions** | `getAddress`, `getAddressSafe`, `getNameFromAddress`, `registerSymbol`, `unregisterSymbol`, `reinitializeSymbolhandler`, `getModuleSize`, `inModule`, `getRTTIClassName` |

## Objective

Turn `game.exe+1F4A30` into `Player.Health`. Cheat tables, Lua scripts and Auto Assembler scripts can then use the
friendly name, and the plugin removes every name it added when it is disabled.

## Why it matters

Offsets change with every game update. A registered symbol is the one place where the offset lives: everything else says
`Player.Health`. A plugin that registers symbols and forgets them leaves stale names in the session, so the cleanup is
part of the recipe and not an afterthought.

## How it works

### 1. Bind the symbol functions

```csharp
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lua;

namespace SymbolRecipe;

internal static partial class SymbolCalls
{
    [LuaGlobal("getAddress")]
    public static partial nuint GetAddress(string symbol);

    [LuaGlobal("getAddressSafe")]
    public static partial bool TryGetAddress(string symbol, out nuint address);

    [LuaGlobal("getNameFromAddress")]
    public static partial bool TryGetName(nuint address, [MaybeNullWhen(false)] out string name);

    [LuaGlobal("registerSymbol")]
    public static partial void RegisterSymbol(string name, nuint address, bool doNotSave);

    [LuaGlobal("unregisterSymbol")]
    public static partial void UnregisterSymbol(string name);

    [LuaGlobal("reinitializeSymbolhandler")]
    public static partial void ReinitializeSymbols();

    [LuaGlobal("getModuleSize")]
    public static partial bool TryGetModuleSize(string moduleName, out long size);

    [LuaGlobal("inModule")]
    public static partial bool IsInModule(nuint address);

    [LuaGlobal("getRTTIClassName")]
    public static partial bool TryGetClassName(nuint address, [MaybeNullWhen(false)] out string className);
}
```

`getAddress` raises an error for an unknown symbol, so it is a throwing form. `getAddressSafe` answers `nil` instead, so
it is the Try form, and the one you use whenever a missing symbol is normal.

### 2. The symbol book and the plugin

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Runtime;

namespace SymbolRecipe;

[CheatEnginePlugin("Symbol Book")]
public sealed class SymbolBookPlugin : CheatEnginePlugin
{
    private CancellationTokenSource? _watch;

    protected override void OnEnable()
    {
        var status = Book.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");

        _watch = new CancellationTokenSource();
        var token = _watch.Token;
        _ = Task.Run(() => WatchAsync("game.exe", token));
    }

    protected override void OnDisable()
    {
        _watch?.Cancel();
        _watch?.Dispose();
        _watch = null;
        Book.UnregisterAll();
        Book.UnregisterLuaFunctions(LuaRuntime.AcquireState());
    }

    private static async Task WatchAsync(string module, CancellationToken token)
    {
        try
        {
            var start = await ModuleWaiter.WaitForAsync(module, TimeSpan.FromMinutes(2), token).ConfigureAwait(false);
            if (start is null)
            {
                HostLog.Write(HostLogLevel.Information, $"{module} did not load in time.");
                return;
            }

            var count = MainThread.Invoke(static _ => Book.RegisterAll(), 0);
            HostLog.Write(HostLogLevel.Information, $"{module} loaded at {start}, {count} symbols registered.");
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested) HostLog.Write(HostLogLevel.Warning, "The module watch stopped.", exception);
        }
    }
}

internal static partial class Book
{
    private static readonly (string Name, string Expression)[] s_definitions =
    [
        ("Player.Health", "game.exe+1F4A30"),
        ("Player.Mana", "game.exe+1F4A34"),
        ("World.Clock", "game.exe+2A0C10"),
    ];

    private static readonly List<string> s_registered = [];

    public static int RegisterAll()
    {
        UnregisterAll();
        foreach (var (name, expression) in s_definitions)
        {
            if (!SymbolCalls.TryGetAddress(expression, out var address)) continue;

            SymbolCalls.RegisterSymbol(name, address, doNotSave: true);
            s_registered.Add(name);
        }

        return s_registered.Count;
    }

    public static void UnregisterAll()
    {
        foreach (var name in s_registered) SymbolCalls.UnregisterSymbol(name);
        s_registered.Clear();
    }

    [LuaFunction("my_plugin_register_symbols")]
    public static long RegisterSymbols() => RegisterAll();

    [LuaFunction("my_plugin_where")]
    public static string Where(long address)
    {
        var target = unchecked((nuint)address);
        var name = SymbolCalls.TryGetName(target, out var symbol) ? symbol : $"0x{target:X}";
        var place = SymbolCalls.IsInModule(target) ? "inside a module" : "outside every module";
        return SymbolCalls.TryGetClassName(target, out var type) ? $"{name}, {place}, class {type}" : $"{name}, {place}";
    }

    [LuaFunction("my_plugin_module")]
    public static string Module(string name) =>
        SymbolCalls.TryGetAddress(name, out var start) && SymbolCalls.TryGetModuleSize(name, out var size)
            ? $"{name}: {Address.FromUInt64(start)} to {Address.FromUInt64(start) + size}"
            : $"{name} is not loaded";
}

internal static class ModuleWaiter
{
    public static async Task<Address?> WaitForAsync(string module, TimeSpan timeout, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var found = MainThread.Invoke(
                    static (string name) => SymbolCalls.TryGetAddress(name, out var address) ? (nuint?)address : null,
                    module);
                if (found is { } start) return Address.FromUInt64(start);

                await Task.Delay(TimeSpan.FromMilliseconds(250), limit.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return null;
        }
    }
}
```

`RegisterAll` registers each definition it can resolve and skips the rest, so one stale offset never blocks the others.
`doNotSave: true` keeps the names out of the saved cheat table, which is right for symbols the plugin recreates on every
enable.

`ModuleWaiter` polls `getAddressSafe` every 250 ms until the module shows up, the timeout passes, or the plugin is
disabled. Each probe hops to the main thread with `MainThread.Invoke`, because Cheat Engine's Lua
state belongs to that thread, and the wait between probes stays on the worker.

> [!WARNING]
> Never block the main thread on the watcher. A main thread that waits for a worker which calls `MainThread.Invoke`
> deadlocks unless it pumps `MainThread.CheckSynchronize`. Start the watcher with `Task.Run`, as above, and let it
> report through `HostLog`. [Guide 09](../../09-main-thread/README.md) explains the rule.

### 3. Try it

```lua
print(my_plugin_register_symbols())          -- how many names were registered
print(getAddress("Player.Health"))           -- the registered name resolves like any other symbol
print(my_plugin_where(getAddress("Player.Health")))
print(my_plugin_module("game.exe"))
```

```text
2
5370759728
Player.Health, inside a module, class PlayerStats
game.exe: 0000000140000000 to 00000001401F8000
```

The numbers depend on your target. This session ran with two resolvable offsets, which is why two names registered.

## Good to know

- **Symbols are global to the session.** Every name lives in Cheat Engine's symbol table, so prefix yours
  (`Player.Health`, not `Health`).
- **Reload after a module changes.** `reinitializeSymbolhandler()` rebuilds the module and symbol list. Call it after
  the game loads a new module and before you resolve a name that lives in it.
- **`getModuleSize` pairs with `getAddress`.** The module name gives the base address, and the size gives the end.
- **`getRTTIClassName` answers `nil` for unknown memory.** Treat it as a Try form, as `Where` does.
- **A Lua timer is the other option.** A polling loop on the main thread works too, as long as every probe returns
  quickly. The worker version above keeps the interface responsive with no timer to manage.

## Promise

- A Try form returns `false` for an unknown symbol and never throws.
- `getAddress` reports an unknown symbol through a `LuaException` that carries the message Cheat Engine raised.
- After `OnDisable`, every name that `Book` registered is unregistered and the Lua functions are `nil`.
- The Lua stack returns to its previous height after every call.

## Before you move on

- [ ] `my_plugin_register_symbols()` returns the number of offsets that resolved in your game.
- [ ] `getAddress("Player.Health")` works while the plugin is on and fails after you untick it.
- [ ] The watcher stops when you untick the plugin during the wait.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
