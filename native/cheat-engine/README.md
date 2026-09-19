# Cheat Engine Lua

The 64-bit Lua 5.3 library of Cheat Engine 7.7, kept as a test fixture.

## Objective

Give every test and benchmark the Lua that a plugin binds in production, byte for byte, on any machine and in CI.

## Why it exists

A plugin never loads its own Lua. Cheat Engine keeps its Lua state inside its `lua53-64.dll`, and the plugin calls that
copy (see [`libs/CheatEngine.SDK.Lua.Interop`](../../libs/CheatEngine.SDK.Lua.Interop/README.md)). A Lua built from
the lua.org sources is a different library, and the differences show up in scripts:

| Property                        | Cheat Engine 7.7                                | lua.org 5.3.6, default makefile flags |
|---------------------------------|-------------------------------------------------|---------------------------------------|
| Lua release                     | 5.3.0 (LuaBinaries) with Cheat Engine's locking | 5.3.6                                 |
| `string.format("%q", 0.1)`      | `"0.1"`                                         | `0x1.999999999999ap-4`                |
| `module`, `bit32`, `math.log10` | `module` only                                   | `bit32` and `math.log10` only         |
| Exports                         | 149, with `lua_compat` and C++ decorated names  | 146                                   |
| C runtime                       | Static, imports `KERNEL32.dll` only             | Dynamic                               |

Tests that bind a stock build can pass on a behavior that Cheat Engine does not have, and fail to notice one that it
does.

## How it works

| File           | Value                                                              |
|----------------|--------------------------------------------------------------------|
| `lua53-64.dll` | The file of a Cheat Engine 7.7 installation, x64, not modified     |
| Size           | 539,496 bytes                                                      |
| SHA-256        | `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2` |
| Linker stamp   | 2025-08-02                                                         |

`CheatEngine.SDK.Tests.Shared` copies the file to `native/lua53-64.dll` in the output of every test project and
benchmark that references it. `NativeLuaLibrary` binds that copy unless `CHEATENGINE_SDK_LUA53_PATH` names another DLL,
and an installed Cheat Engine is never consulted. Nothing else has to be provisioned locally or in CI, and the DLL
loads on any Windows x64 because it needs no C runtime.

The file name must stay `lua53-64.dll`: the production module lookup test resolves the module by that name.

To replace the file, copy the new build here, update the size and hash in
`tests/CheatEngine.SDK.Lua.Interop.Tests/Fixture/BundledLuaLibraryTests.cs` and in this file, and run the whole suite.

## Terms

The file is a component of Cheat Engine, by Eric Heijnen, and stays under Cheat Engine's own terms (the `license.txt` of
its installation). The MIT license of this repository does not cover it. Tests and benchmarks use it, and it is never
packed into `CheatEngine.SDK` or shipped with a plugin.

The Lua library inside is distributed under the MIT license:

```text
Copyright (C) 1994-2015 Lua.org, PUC-Rio.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Promise

- The file is exactly the recorded build, pinned by `Bundled_copy_is_the_recorded_Cheat_Engine_build`.
- The tests bind this copy and not an installed Cheat Engine unless `CHEATENGINE_SDK_LUA53_PATH` is set, pinned by
  `Fixture_binds_the_bundled_copy_unless_the_variable_overrides_it`.
