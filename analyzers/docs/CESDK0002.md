# CESDK0002: More than one plugin class in the assembly

|                    |                                                                       |
|--------------------|-----------------------------------------------------------------------|
| Category           | `CheatEngine.SDK.Plugin`                                              |
| Default severity   | Error                                                                 |
| Enabled by default | Yes                                                                   |
| Code fix           | No                                                                    |
| Reported           | In build and full-solution analysis only (compilation-end diagnostic) |

## Cause

Two or more classes of the same project carry `[CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin]`. The diagnostic
is reported
on each of them, with the total count.

## Why

Cheat Engine treats one DLL as one plugin: it calls the single entry point `CESDK.CESDK.CEPluginInitialize`, which fills
one init record with one name and one set of callbacks. The generated entry point therefore needs exactly one class to
construct. With several candidates it does not guess: it emits nothing, and Cheat Engine cannot load the DLL.

## What is counted

Every class with the attribute counts, whether or not it also violates [CESDK0001](CESDK0001.md). A partial class counts
once, however many files it spans, and a nested plugin class counts like a top-level one. The location is the class name
in the part that carries the attribute. Classes in generated code are neither counted nor reported.

Two cases report nothing. A project that switches the generated entry point off
(`<CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>`) is not reported, because hand-written
bootstrap code then
decides which class gets constructed. A project without any plugin class is not reported either.

Because the answer needs the whole compilation, the rule runs in the compilation-end phase. Such diagnostics appear in
build output and in full-solution analysis where an IDE offers it, not while typing.

## Example

```csharp
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;

namespace MyPlugin;

[CheatEnginePlugin("Scanner")]
public sealed class ScannerPlugin : CheatEnginePlugin   // CESDK0002: one of 2 classes
{
    protected override void OnEnable() { }
    protected override void OnDisable() { }
}

[CheatEnginePlugin("Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin   // CESDK0002: one of 2 classes
{
    protected override void OnEnable() { }
    protected override void OnDisable() { }
}
```

## How to fix

Keep the attribute on the one class that represents the plugin. Give every other plugin its own project: one plugin, one
assembly. Shared code goes into a class library that both plugin projects reference.

## When to suppress

Never in a project that relies on the generated entry point: the plugin will not load.
