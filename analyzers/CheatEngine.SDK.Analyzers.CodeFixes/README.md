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
|                                                                                      | `CESDK0001` `InaccessibleParameterlessConstructor` | Make the constructor public (`CESDK0001.MakeConstructorPublic`)                                       | The constructor takes optional parameters, or is not declared in an editable document                                   |
| `CheatEngine.SDK.Analyzers.CodeFixes.Usage.UnmanagedCallersOnlyGuardCodeFixProvider` | `CESDK1004`                                        | Wrap the body in `try` and `catch (Exception)` returning a failure value (`CESDK1004.WrapInTryCatch`) | An expression body contains a preprocessor directive                                                                    |

The other `CESDK0001` problems are design decisions and get no fix. They are `Generic`, `NestedInGeneric`,
`NotDerivedFromPluginBase`, `Inaccessible`, `FileLocal`, `ReservedEntryPointName`, `RequiredMembers`, `ObsoleteError`
and `InvalidName`. `CESDK0002`, `CESDK0004` and `CESDK2001` to `CESDK2004` have no code fix.

Edits follow the symbol, not the diagnostic location. The `sealed` fix replaces the modifier in every part of a partial
class that carries it, in whichever document. The make-public fix edits the document that declares the constructor. The
add-constructor fix edits the part that carries the attribute. Parts in generated code are skipped.

New code carries the formatter and simplifier annotations, so indentation and the spelling of `Exception` follow the
document. Do not hard-code them.

The failure value follows the return type:

| Return type                                                                           | Failure value                                                                                    |
|---------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------|
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` | `0`, which is `FALSE` for Cheat Engine's `BOOL` callbacks and zero results for a `lua_CFunction` |
| `bool`                                                                                | `false`                                                                                          |
| `void`                                                                                | Nothing: the empty `catch` block carries a comment                                               |
| Anything else, such as `nint`, `nuint`, `char`, pointers, enums and structs           | `default`                                                                                        |

`nint` and `nuint` get `default` rather than `0`, so `IntPtr` and `UIntPtr` spellings compile under older language
versions. A callback whose native contract treats `0` as success needs a manual change. An expression body becomes a
block body: `=> e` turns into `return e;` (`e;` for `void`) and `=> throw ...` into a throw statement.

```csharp
using System;
using System.Runtime.InteropServices;

internal static class Callbacks
{
    // Before the fix: private static int OnCall(nint state) => Work(state);
    [UnmanagedCallersOnly]
    private static int OnCall(nint state)
    {
        try
        {
            return Work(state);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int Work(nint state) => 1;
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
- Wrapping is total. An existing `try` without a catch-all is wrapped, not edited
  (`Existing_try_without_a_catch_all_is_wrapped_as_a_whole`).
- Fix All works for both providers through `WellKnownFixAllProviders.BatchFixer`. An unguarded local function inside an
  unguarded method takes a second pass (`Code_fix_providers_fix_their_rule_and_support_fix_all`,
  `Unguarded_local_function_inside_an_unguarded_method_takes_a_second_fix_all_pass`).

## Run the tests

The fixes are tested in [`tests/CheatEngine.SDK.Analyzers.Tests`](../../tests/CheatEngine.SDK.Analyzers.Tests/README.md):
`Plugin/PluginClassShapeCodeFixTests.cs` and `Usage/UnmanagedCallersOnlyGuardCodeFixTests.cs`. There is no separate code
fix test project.

```powershell
dotnet test --project tests/CheatEngine.SDK.Analyzers.Tests
```
