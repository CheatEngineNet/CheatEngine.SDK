# CheatEngine.SDK.Lua.Interop

`CheatEngine.SDK.Lua.Interop` exposes the Lua 5.3 C API to managed code as static methods over blittable function
pointers you bind to a loaded module.

## Objective

Give managed code the Lua 5.3 C API with its C names and exact C signatures. Every call site can be checked against the
Lua manual. The project owns no policy: [`CheatEngine.SDK.Lua`](../CheatEngine.SDK.Lua/README.md) owns stack
discipline, error handling, marshalling and string encoding. It ships in the `CheatEngine.SDK` package under
`lib/net10.0`.

## Why it exists

Cheat Engine keeps its Lua state inside its own `lua53-64.dll`. A plugin must call that copy, because a second copy has
its own global state and none of Cheat Engine's functions. A wrong native declaration corrupts memory instead of
throwing, so this layer transcribes the Lua headers and adds nothing.

Allocating calls use the tiny `cheatengine-sdk-lua-bridge.dll` shipped with CheatEngine.SDK. It receives pointers to the
host's existing Lua exports and places the operation below `lua_pcallk`, so Lua cannot `longjmp` through managed frames.
That includes the host-object pusher (`LuaPushClassInstance`): it is not called directly from a managed frame. Before
its
first use, the interop layer validates the bridge's fixed export list and its v1.1 contract (magic, major/minor version,
pointer and Lua scalar widths, export-table size and operation bitmap) against `Unsafe.SizeOf` values. The bridge does
not link, load or otherwise import a second Lua runtime. The prebuilt Windows x64 DLL is copied beside consumers
automatically and is loaded only from this assembly's directory; xmake and a C compiler are needed only to change the
bridge itself.

## How it works

| Namespace                               | Types                                | Role                                                            |
|-----------------------------------------|--------------------------------------|-----------------------------------------------------------------|
| `CheatEngine.SDK.Lua.Interop.Api`       | `LuaApi`                             | Binding, one forwarder per export, constants, macro equivalents |
| `CheatEngine.SDK.Lua.Interop.Loading`   | `LuaModule`                          | Finds a Lua library that is already loaded                      |
| `CheatEngine.SDK.Lua.Interop.Protected` | internal bridge binding              | Calls allocating primitives below a native protected boundary   |
| `CheatEngine.SDK.Lua.Interop.Types`     | `lua_State`, `lua_Debug`, `luaL_Reg` | Opaque state, debug record, registration entry                  |

`LuaApi` holds one table of `delegate* unmanaged[Cdecl]` pointers, resolved with `NativeLibrary.TryGetExport` from a
module handle you pass. `Initialize` and `TryInitialize` bind all or nothing. A zero handle, a module that lacks an
export, or a second module is refused, and the table stays as it was. Binding the same module again does nothing.
Calling an export before `IsInitialized` is true jumps to address zero and ends the process. The bound table is
immutable and works from any thread. States that share a global state must be used by one OS thread at a time.

The public members are `AggressiveInlining` forwarders named like the exports, the `LUA_*` constants, and the
function-like macros of `lua.h` and `lauxlib.h`. The `luaopen_*` functions are properties that return the pointer, for
`luaL_requiref`. C types map to `long` (`lua_Integer`), `double` (`lua_Number`), `nuint` (`size_t`), `nint`
(`lua_KContext`), `byte*` (`const char*`) and `delegate* unmanaged[Cdecl]` (callbacks). The `lua_Debug` layout and
`LUA_REGISTRYINDEX` assume the default `luaconf.h` values: `LUA_IDSIZE` from 57 to 64 and the default `LUAI_MAXSTACK`. A
Lua build with other values is unsupported.

Lua raises errors with `longjmp`, and the runtime cannot unwind managed frames that way. Each call documents its stack
effect (`Stack: -pops +pushes`) and its error class: `Raises: never`, `memory`, `any` or `always`. `any` can run
metamethods. Call it only where a raise cannot cross a managed frame, such as a Lua function run by `lua_pcallk`, or
when the operands are plain. `memory` also covers a failing `__gc` finalizer that an allocation triggers. `lua_error` is
bound but forbidden. The `luaL_check*` functions, `lua_yieldk`, the `luaL_Buffer` family and the C varargs functions are
not bound. They unwind with `longjmp` or cannot be blittable pointers.

`LuaModule.TryGetLoaded` calls `GetModuleHandleExW`, which answers from the loader's module list. It never loads a
library, unlike a bare-name `LoadLibrary`, which loads whatever file its search path finds first. It returns `false` off
Windows.

## Use

```csharp
using CheatEngine.SDK.Lua.Interop.Loading;
using CheatEngine.SDK.Lua.Interop.Types;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

static unsafe class Example
{
    public static bool BindOnce() => LuaModule.TryGetLoaded(out nint module) && TryInitialize(module, out _);

    public static long Evaluate(lua_State* L)
    {
        long result = 0;
        int top = lua_gettop(L);
        fixed (byte* chunk = "return 6 * 7"u8)
            if (luaL_loadstring(L, chunk) == LUA_OK && lua_pcall(L, 0, 1, 0) == LUA_OK)
                result = lua_tointeger(L, -1);
        lua_settop(L, top);
        return result;
    }
}
```

## Promise

- Signatures are exact. `LuaApiSignatureTests` compares every forwarder with its function-pointer slot and reads its IL,
  so a forwarder cannot call a sibling slot or swap arguments.
- Constants match the headers, and `LUA_REGISTRYINDEX` matches a real Lua library (`LuaConstantsTests`,
  `Registry_index_of_the_library_matches_the_managed_constant`).
- No runtime marshalling. `[assembly: DisableRuntimeMarshalling]` rejects a non-blittable signature at compile time.
- Binding is all or nothing. A module that is not Lua 5.3 never leaves a half-bound table, and a second Lua library is
  refused (`LuaApiInitializationTests`, `LuaApiBoundTableTests`). `LuaModule` never loads one
  (`TryGetLoaded_unknown_module_returns_false_and_loads_nothing`).
- `lua_Debug` matches the C layout on x64 and fits the record a real library writes (`NativeStructLayoutTests`,
  `Native_debug_record_fits_the_managed_struct`).

## Run the tests

Run `dotnet test --project tests/CheatEngine.SDK.Lua.Interop.Tests`. Tests tagged `Category=NativeLua` run against the
Lua DLL of Cheat Engine 7.7 kept in [`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be
installed. See [
`tests/CheatEngine.SDK.Lua.Interop.Tests`](../../tests/CheatEngine.SDK.Lua.Interop.Tests/README.md).

## Third-party notice

The declarations in this project are transcribed from the header files of Lua 5.3, which are distributed under the MIT
license. They cover function signatures, constant values, macro expansions and struct layouts. Documentation text is
original to this project, which contains no Cheat Engine file.

```
Copyright (C) 1994-2015 Lua.org, PUC-Rio.

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
