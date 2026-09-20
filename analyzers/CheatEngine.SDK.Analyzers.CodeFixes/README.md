# CheatEngine.SDK.Analyzers.CodeFixes

Code fixes for the `CheatEngine.SDK` diagnostics, in an assembly that only IDEs load.

## Objective

Turn the diagnostics that have a mechanical answer into one-click edits: `CESDK0001` (make the plugin class
constructible) and `CESDK1004` (guard a native callback).

## Why it exists

A code fix needs `Microsoft.CodeAnalysis.Workspaces`. The command-line compiler never loads workspace types, so an
analyzer assembly that references them fails at build time (`RS1038`). The fixes live in their own assembly, packed next
to `CheatEngine.SDK.Analyzers.dll` under `analyzers/dotnet/cs`. The compiler finds no analyzer in it, and IDEs discover the
`[ExportCodeFixProvider]` types. This is the only shipping component that references
`Microsoft.CodeAnalysis.CSharp.Workspaces`.

## How it works

The analyzer decides and the fix edits. `CheatEngine.SDK.Analyzers` writes the problem name into
`Diagnostic.Properties["CheatEngine.SDK.PluginClassProblem"]`. The provider reads it and offers at most one action per diagnostic,
so rule and fix cannot disagree. This assembly references `CheatEngine.SDK.Analyzers`, never the reverse.

| Provider                                                                             | Problem                                            | Action (equivalence key)                                                                              | Not offered when                                                                                                        |
|--------------------------------------------------------------------------------------|----------------------------------------------------|-------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| `CheatEngine.SDK.Analyzers.CodeFixes.Plugin.PluginClassShapeCodeFixProvider`         | `CESDK0001` `Abstract`, `Static`                   | Replace `abstract` or `static` with `sealed` (`CESDK0001.MakeSealed`)                                 | No declaration of the class is editable                                                                                 |
|                                                                                      | `CESDK0001` `MissingParameterlessConstructor`      | Add a public parameterless constructor (`CESDK0001.AddParameterlessConstructor`)                      | The class has a primary constructor or no body, or its base class has no accessible constructor that needs no arguments |
|                                                                                      | `CESDK0001` `InaccessibleParameterlessConstructor` | Make the constructor public (`CESDK0001.MakeConstructorPublic`)                                       | The parameterless constructor is not declared in an editable document                                                    |
| `CheatEngine.SDK.Analyzers.CodeFixes.Usage.UnmanagedCallersOnlyGuardCodeFixProvider` | `CESDK1004` on `CESDK.CESDK.CEPluginInitialize`    | Wrap the bootstrap body in `try` and `catch (Exception)` returning `0` (`CESDK1004.WrapInTryCatch`)     | The signature is not exactly public static `int CEPluginInitialize(IntPtr, int)`, or an expression body contains a directive |

The other `CESDK0001` problems are design decisions and get no fix. They are `Generic`, `NestedInGeneric`,
`NotDerivedFromPluginBase`, `Inaccessible`, `FileLocal`, `ReservedEntryPointName`, `RequiredMembers`, `ObsoleteError`
and `InvalidName`. `CESDK0002` through `CESDK0005`, `CESDK1001`, `CESDK1003`, `CESDK1005`, and `CESDK2001` through
`CESDK2007` have no code fix. Their repairs require contract, ownership, lifecycle or naming decisions the SDK cannot
safely infer.

Edits follow the symbol, not the diagnostic location. The `sealed` fix replaces the modifier in every part of a partial
class that carries it, in whichever document. The make-public fix edits the document that declares the constructor. The
add-constructor fix edits the part that carries the attribute. Parts in generated code are skipped.

New code carries the formatter and simplifier annotations, so indentation and the spelling of `Exception` follow the
document. Do not hard-code them.

The only known automatic failure value is the documented `0` from the CE bootstrap contract. A callback's return type
alone does not describe failure: in particular, `0` may be success or a count. The provider therefore leaves all other
`[UnmanagedCallersOnly]` entries untouched for the author to guard with their verified native convention. A bootstrap
expression body becomes a block body: `=> e` turns into `return e;` and `=> throw ...` into a throw statement.

```csharp
using System;
using System.Runtime.InteropServices;

namespace CESDK;

internal static class CESDK
{
    // Before the fix: public static int CEPluginInitialize(IntPtr exportedFunctions, int bootstrap) => Work(exportedFunctions);
    [UnmanagedCallersOnly]
    public static int CEPluginInitialize(IntPtr exportedFunctions, int bootstrap)
    {
        try
        {
            return Work(exportedFunctions);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int Work(IntPtr exportedFunctions) => 1;
}
```

## Promise

- The code an action writes compiles and satisfies the rule. The tests analyze the fixed source. The one exception is
  `sealed`: abstract members or a missing base class remain for the author
  (`Static_class_becomes_sealed_and_the_next_problem_shows_up`).
- Every offered action changes the code and needs no information only the author has. Cases that need such information
  get no fix: primary constructor chaining, arguments for a base constructor, an expression body split by `#if`
  (`Primary_constructor_gets_no_fix`, `Expression_body_with_preprocessor_directives_gets_no_fix`).
- A fix never drops a comment or a statement the author wrote. Comments next to replaced or removed tokens move with
  them (`Comment_on_the_dropped_second_accessibility_keyword_is_kept`,
  `Comments_around_an_expression_body_move_with_the_statement`).
- The CESDK1004 provider only emits this known bootstrap return convention. It never proposes a generic `return 0` for
  a callback whose ABI contract is unknown.
- Fix All works for both providers through `WellKnownFixAllProviders.BatchFixer` when all selected CESDK1004
  diagnostics are bootstrap entries.

## Run the tests

The fixes are tested in [`tests/CheatEngine.SDK.Analyzers.Tests`](../../tests/CheatEngine.SDK.Analyzers.Tests/README.md):
`Plugin/PluginClassShapeCodeFixTests.cs` and `Usage/UnmanagedCallersOnlyGuardCodeFixTests.cs`. There is no separate code
fix test project.

```powershell
dotnet test --project tests/CheatEngine.SDK.Analyzers.Tests
```
