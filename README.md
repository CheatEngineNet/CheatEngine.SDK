<div align="center">

# CheatEngine.SDK

**An unofficial .NET SDK for Cheat Engine plugins.**

[![CI](https://img.shields.io/github/actions/workflow/status/CheatEngineNet/CheatEngine.SDK/main-ci.yml?branch=main&style=flat-square&logo=githubactions&logoColor=white&label=CI)](https://github.com/CheatEngineNet/CheatEngine.SDK/actions/workflows/main-ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/CheatEngineNet/CheatEngine.SDK/badge)](https://scorecard.dev/viewer/?uri=github.com/CheatEngineNet/CheatEngine.SDK)
[![NuGet](https://img.shields.io/nuget/vpre/CheatEngine.SDK?style=flat-square&logo=nuget&logoColor=white&label=NuGet)](https://www.nuget.org/packages/CheatEngine.SDK)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square)](#requirements)
[![MIT license](https://img.shields.io/badge/license-MIT-6e7781?style=flat-square)](LICENSE)

[Get started](#quick-start) · [Examples](exemples/README.md) · [API guide](exemples/api/README.md) · [Diagnostics](analyzers/docs/README.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md)

</div>

CheatEngine.SDK lets you write Cheat Engine 7.7 plugins in C#. It packages the plugin-facing libraries, source
generators, and analyzers needed to generate the entry point Cheat Engine loads and to expose C# methods to Lua. It is
an independent project and is not affiliated with Cheat Engine.

## Requirements

| Requirement   | Supported version                                                                                                      |
|---------------|------------------------------------------------------------------------------------------------------------------------|
| .NET SDK      | 10.0.401 or later to build a plugin; exactly 10.0.401 to build this repository (`global.json`, `rollForward: disable`) |
| .NET runtimes | .NET 10 `Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App`, and `Microsoft.AspNetCore.App`                        |
| Cheat Engine  | 7.7                                                                                                                    |
| Platform      | Windows x64                                                                                                            |

The qualifiable host profile is `ce-7.7.0.10621-x64-managed-hostfxr`: Cheat Engine 7.7.0.10621 x64 loading managed
plugins through `hostfxr`. No exact-host scenario has been executed on it yet; the
[qualification matrix](docs/qualification/README.md#matrix-summary) records its C3 and C4 cells as not executed. The
.NET 10 `ce.runtimeconfig.json` observed on the maintainer's host is a local modification, not an installer default,
and it affects every managed plugin of that host: record your host's runtime policy and do not edit an installed Cheat
Engine configuration just to run a plugin. See the
[live-plugin guide](tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine) and the
[support profile](docs/qualification/support-profile.md).

## Install

Create a class library and add the package:

```powershell
dotnet new classlib -n MyPlugin
cd MyPlugin
dotnet add package CheatEngine.SDK --prerelease
```

Set `<PlatformTarget>x64</PlatformTarget>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the project file. A
`[LuaFunction]` export needs the explicit unsafe opt-in for its generated registration thunk; the package supplies the
dynamic-loading default and copies its bundled Lua bridge into the plugin output on Windows.

## Quick start

Replace the generated class with a plugin class and a Lua-callable method:

```csharp
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Registration;
using CheatEngine.SDK.Lua.Runtime;

namespace MyPlugin;

[CheatEnginePlugin("My Plugin")]
public sealed class HelloPlugin : CheatEnginePlugin
{
    private LuaRegistrationLease? _commands;

    protected override void OnEnable()
    {
        using var operation = LuaRuntime.AcquireOperation();
        var registration = Commands.TryRegisterLuaFunctions(operation.State);
        if (!registration.IsSuccess)
        {
            _ = registration.Lease?.ReleaseWithOutcome(operation.State);
            throw new InvalidOperationException(registration.Kind.ToString());
        }
        _commands = registration.Lease;
    }

    protected override void OnDisable() => _commands?.Dispose();
}

internal static partial class Commands
{
    [LuaFunction("greet")]
    public static string Greet(string name) => $"Hello, {name}!";
}
```

Build the project, then keep the complete output directory together when loading `MyPlugin.dll` from Cheat Engine's
plugin settings:

```powershell
dotnet build -c Release
```

After enabling the plugin, run `print(greet("world"))` in Cheat Engine's Lua Engine window. For a fuller walkthrough,
start with [Example 01](exemples/01-first-plugin/README.md).

## Build from source

The repository pins the .NET SDK in [`global.json`](global.json). From the repository root:

```powershell
dotnet restore CheatEngine.SDK.slnx --locked-mode
dotnet build CheatEngine.SDK.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on
dotnet test --solution CheatEngine.SDK.slnx -c Release --fail-skips on
dotnet pack src/CheatEngine.SDK -c Release -o artifacts/nuget
```

Ordinary managed builds use the checked-in Windows x64 Lua bridge, so they do not require a C toolchain. Contributors
changing [`native/cheatengine-sdk-lua-bridge`](native/cheatengine-sdk-lua-bridge/README.md) need xmake and a Windows x64
C toolchain to rebuild it.

## Project layout

| Path                                           | Purpose                                                                     |
|------------------------------------------------|-----------------------------------------------------------------------------|
| [`libs/`](libs/)                               | Layered annotations, ABI, Lua, engine, and hosting libraries.               |
| [`src/CheatEngine.SDK/`](src/CheatEngine.SDK/) | The `CheatEngine.SDK` NuGet package and consumer build properties.          |
| [`source-generators/`](source-generators/)     | Generated plugin entry-point and Lua-binding components.                    |
| [`analyzers/`](analyzers/)                     | Diagnostics, code fixes, and their documentation.                           |
| [`native/`](native/)                           | The Lua test fixture and bundled Windows x64 Lua protection bridge.         |
| [`tests/`](tests/)                             | Unit tests, benchmarks, shared fixtures, and the live-plugin sample.        |
| [`exemples/`](exemples/)                       | Guides, recipes, and API documentation. The directory name is intentional.  |
| [`docs/`](docs/)                               | Qualification, ABI and catalogue documents rebuilt after the 2026-09 audit. |
| [`eng/`](eng/)                                 | Shared build configuration, and the CI, release and qualification scripts.  |

## Documentation

- [Documentation index](docs/README.md)
- [Examples and recipes](exemples/README.md)
- [API guide](exemples/api/README.md)
- [Diagnostic reference](analyzers/docs/README.md)
- [Live-plugin guide](tests/CheatEngine.SDK.LivePlugin/README.md)
- [Contributing guide](CONTRIBUTING.md)

## License

[MIT](LICENSE). Cheat Engine is licensed separately. The Lua DLL retained under [
`native/cheat-engine`](native/cheat-engine/README.md) is a test fixture under Cheat Engine's terms and is not included
in the NuGet package.
